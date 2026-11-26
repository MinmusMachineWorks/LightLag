using System.Collections;
using KSP.Localization;
using KSP.UI.Screens.Flight;
using KSP.UI.TooltipTypes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LightLag
{
    /// <summary>
    /// The delay readout in the stock signal bar: one more item in CommNet's row of signal
    /// icons (<see cref="TelemetryUpdate"/>), in flight and in map view alike. It's a stopwatch
    /// icon (the flight computer button) and the active vessel's delay ("2.31 s", "Local",
    /// "No delay"), plus how many commands are queued. With no signal the stopwatch stays, grey
    /// and without text (stock's red control icon says why), so the flight computer can still be
    /// opened. While delay applies it runs the <see cref="ContactPredictor"/> (next contact with
    /// no signal, signal loss with one), whose estimate is in the tooltip and the flight computer. The row lays its icons out itself (it grows
    /// when stock shows the globe button), so the readout joins that layout and the panel's own
    /// background takes it in. If the row has no layout group, the readout is placed after the
    /// last icon and the background widened by hand. Its text copies the MET display; its
    /// tooltip is the stock one and gives the full state. Clicking it opens the
    /// <see cref="FlightComputerPanel"/>. It's hidden when delay is off or CommNet isn't running,
    /// and with the rest of the UI (F2). The panel's structure is logged once per game session.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SignalBarWidget : MonoBehaviour
    {
        private const float Spacing = 4f, UpdateSeconds = 0.1f;
        private static bool _dumped;

        private static readonly Color SignalColor = new Color(0.45f, 1f, 0.3f), NoSignalColor = new Color(0.55f, 0.55f, 0.55f);

        private FlightComputerPanel _window;
        private readonly ContactPredictor _contact = new ContactPredictor();
        private Image _icon;
        private GameObject _root;
        private RectTransform _rootRect, _row, _background;
        private TextMeshProUGUI _text;
        private TooltipController_Text _tooltip;
        private Color _textColor = Color.white;
        private float _height, _widened, _nextUpdate;
        private bool _inLayout;
        private string _shown;
        private static Sprite _sprite;

        public void Start()
        {
            StartCoroutine(Attach());
        }

        public void OnDestroy()
        {
            _window?.Destroy();
            Widen(0f);
            if (_root != null) Destroy(_root);
        }

        private IEnumerator Attach()
        {
            // The flight UI builds over the first frames; the panel stays inactive without CommNet.
            for (float waited = 0; waited < 10f; waited += Time.unscaledDeltaTime)
            {
                TelemetryUpdate telemetry = TelemetryUpdate.Instance;
                if (telemetry != null && telemetry.gameObject.activeInHierarchy && telemetry.signal_icon != null
                    && ((RectTransform)telemetry.signal_icon.transform).rect.height > 0)
                {
                    Build(telemetry);
                    yield break;
                }
                yield return null;
            }
            Log.Info("signal bar: CommNet's signal panel isn't shown, so there's no delay readout");
        }

        private void Build(TelemetryUpdate telemetry)
        {
            if (!_dumped)
            {
                _dumped = true;
                Dump(telemetry.transform, 0);
            }
            var signal = (RectTransform)telemetry.signal_icon.transform;
            // The row is the nearest ancestor that lays the icons out (an icon may sit in a wrapper).
            _row = (RectTransform)signal.parent;
            for (Transform t = signal.parent; t != null; t = t.parent)
            {
                if (t.GetComponent<HorizontalLayoutGroup>() != null) { _row = (RectTransform)t; break; }
                if (t.GetComponent<TelemetryUpdate>() != null) break;
            }
            _height = signal.rect.height;
            METDisplay met = FindObjectOfType<METDisplay>();

            // The readout: [icon][text], laid out by its own horizontal group.
            _root = new GameObject("LightLagDelay", typeof(RectTransform));
            _root.layer = signal.gameObject.layer;
            _rootRect = (RectTransform)_root.transform;
            _rootRect.SetParent(_row, false);
            _rootRect.SetAsLastSibling();
            var group = _root.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 3f;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            group.padding = new RectOffset(2, 4, 0, 0);
            var element = _root.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = _height;

            // Transparent hit area for the click and the tooltip.
            Image hit = _root.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.layer = _root.layer;
            iconObject.transform.SetParent(_rootRect, false);
            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = Icon();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var iconElement = iconObject.AddComponent<LayoutElement>();
            float iconSize = Mathf.Round(_height * 0.8f);
            iconElement.preferredWidth = iconElement.minWidth = iconSize;
            iconElement.preferredHeight = iconElement.minHeight = iconSize;

            var textObject = new GameObject("Delay", typeof(RectTransform));
            textObject.layer = _root.layer;
            textObject.transform.SetParent(_rootRect, false);
            _text = textObject.AddComponent<TextMeshProUGUI>();
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            _text.alignment = TextAlignmentOptions.MidlineLeft;
            if (met != null && met.text != null)
            {
                _text.font = met.text.font;
                _text.fontSharedMaterial = met.text.fontSharedMaterial;
                _text.fontStyle = met.text.fontStyle;
                _textColor = met.text.color;
            }
            _text.fontSize = Mathf.Round(_height * 0.6f);
            _text.color = _textColor;
            // The stock icons are green; the stopwatch matches them.
            icon.color = SignalColor;
            _icon = icon;

            Button button = _root.AddComponent<Button>();
            button.targetGraphic = icon;
            _window = new FlightComputerPanel(telemetry.control_tooltip?.prefab);
            button.onClick.AddListener(_window.Toggle);

            if (telemetry.control_tooltip != null && telemetry.control_tooltip.prefab != null)
            {
                _tooltip = _root.AddComponent<TooltipController_Text>();
                _tooltip.prefab = telemetry.control_tooltip.prefab;
                _tooltip.continuousUpdate = true;
                _tooltip.RequireInteractable = false;
            }

            _inLayout = _row.GetComponent<HorizontalLayoutGroup>() != null;
            if (!_inLayout)
            {
                // No layout: sit after the rightmost icon, and widen the panel's background by hand.
                element.ignoreLayout = true;
                _rootRect.anchorMin = _rootRect.anchorMax = new Vector2(0f, 0.5f);
                _rootRect.pivot = new Vector2(0f, 0.5f);
                _background = FindBackground(telemetry.control_icon.rectTransform);
            }

            _shown = null;
            _nextUpdate = 0;
            Log.Info($"signal bar: delay readout added to {Path(_row)}, " +
                     (_inLayout ? "in its layout" : $"placed by hand, background {(_background != null ? Path(_background) : "not found")}"));
        }

        public void Update()
        {
            if (_root == null) return;
            _window.UpdateLock();
            Vessel vessel = FlightGlobals.ActiveVessel;
            DelayStatus.State state = DelayStatus.Of(vessel, out _);
            if (state == DelayStatus.State.NoSignal || state == DelayStatus.State.Delay)
                _contact.Update(vessel, state == DelayStatus.State.Delay);
            else _contact.Reset();
            if (Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + UpdateSeconds;

            // Without a signal, stock's red "no control" icon already says so: the stopwatch
            // stays, greyed and without text, as the flight computer button.
            bool off = state == DelayStatus.State.Off;
            if (_root.activeSelf == off)
            {
                _root.SetActive(!off);
                if (off) Widen(0f);
                _shown = null;
            }
            if (off)
            {
                _window.Close();
                return;
            }
            _window.Refresh(vessel, _contact);

            bool noSignal = state == DelayStatus.State.NoSignal;
            string text = noSignal ? "" : DelayStatus.Short(vessel);
            if (text != _shown)
            {
                _shown = text;
                _text.text = text;
                _text.gameObject.SetActive(!noSignal);
                _icon.color = noSignal ? NoSignalColor : SignalColor;
                if (!_inLayout) PlaceByHand();
            }
            string tooltip = DelayStatus.Long(vessel);
            string contact = FlightComputerPanel.ContactText(_contact);
            if (!string.IsNullOrEmpty(contact)) tooltip += "\n" + contact;
            _tooltip?.SetText(tooltip + "\n" + Localizer.Format("#LightLag_Bar_Tooltip"));
        }

        /// <summary>Without a layout group: after the rightmost active icon, with the background widened to match.</summary>
        private void PlaceByHand()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rootRect);
            float width = LayoutUtility.GetPreferredWidth(_rootRect);
            float right = _row.rect.xMin;
            var corners = new Vector3[4];
            foreach (RectTransform child in _row)
            {
                if (child == _rootRect || !child.gameObject.activeSelf) continue;
                child.GetWorldCorners(corners);
                right = Mathf.Max(right, _row.InverseTransformPoint(corners[2]).x);
            }
            _rootRect.anchoredPosition = new Vector2(right - _row.rect.xMin + Spacing, 0f);
            _rootRect.sizeDelta = new Vector2(width, _height);
            Widen(width + Spacing);
        }

        /// <summary>Widens the background by <paramref name="extra"/>, keeping its left edge in place.</summary>
        private void Widen(float extra)
        {
            if (_background == null || Mathf.Approximately(extra, _widened)) return;
            float delta = extra - _widened;
            _background.sizeDelta += new Vector2(delta, 0f);
            _background.anchoredPosition += new Vector2(delta * _background.pivot.x, 0f);
            _widened = extra;
        }

        /// <summary>The nearest ancestor of the icons, up to the panel, with an Image and a fixed width.</summary>
        private static RectTransform FindBackground(RectTransform icon)
        {
            for (Transform t = icon.parent; t != null; t = t.parent)
            {
                var rect = t as RectTransform;
                if (rect != null && t.GetComponent<Image>() != null && Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x))
                    return rect;
                if (t.GetComponent<TelemetryUpdate>() != null) break;
            }
            return null;
        }

        private static void Dump(Transform t, int depth)
        {
            if (depth > 3) return;
            var rect = t as RectTransform;
            string components = string.Join(",", System.Array.ConvertAll(t.GetComponents<Component>(), c => c == null ? "?" : c.GetType().Name));
            Log.Info($"signal bar: {new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} " +
                     (rect != null ? $"rect={rect.rect.width:0}x{rect.rect.height:0} anchors=({rect.anchorMin.x:0.##},{rect.anchorMax.x:0.##}) pivot={rect.pivot.x:0.##} " : "") +
                     $"[{components}]");
            foreach (Transform child in t) Dump(child, depth + 1);
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        /// <summary>A stopwatch: the flight computer's icon, drawn once in white so the Image tints it.</summary>
        private static Sprite Icon()
        {
            if (_sprite != null) return _sprite;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var center = new Vector2(size / 2f, size / 2f - 3f);
            float radius = 22f, ring = 5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float alpha = Mathf.Clamp01(ring / 2f + 0.5f - Mathf.Abs((p - center).magnitude - radius));
                // Crown above the dial.
                if (Mathf.Abs(p.x - center.x) <= 4.5f && p.y >= center.y + radius + 1f && p.y <= center.y + radius + 9f) alpha = 1f;
                if (Mathf.Abs(p.x - center.x) <= 8f && p.y >= center.y + radius + 7f && p.y <= center.y + radius + 11f) alpha = 1f;
                // Hands: up to twelve, and toward two.
                alpha = Mathf.Max(alpha, Segment(p, center, center + new Vector2(0f, 15f), 2.5f));
                alpha = Mathf.Max(alpha, Segment(p, center, center + new Vector2(10f, 6f), 2.5f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply();
            _sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _sprite;
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Mathf.Clamp01(halfWidth + 0.5f - (p - (a + t * ab)).magnitude);
        }
    }
}
