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
    /// The delay readout on the stock signal bar: a box attached to the right of CommNet's
    /// signal icons (<see cref="TelemetryUpdate"/>), in flight and in map view alike, with a
    /// flight computer button and the active vessel's delay ("2.31 s", "No signal", "Local"),
    /// plus how many commands are queued. Its text and background copy the stock MET display and
    /// signal panel; its tooltip uses the stock one and gives the full state. Clicking it opens
    /// the <see cref="FlightComputerWindow"/>. It's hidden when delay is off or CommNet isn't
    /// running, and with the rest of the UI (F2).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SignalBarWidget : MonoBehaviour
    {
        private const float Gap = 4f, Pad = 5f, UpdateSeconds = 0.1f;
        private static readonly Color NoSignalColor = new Color(1f, 0.45f, 0.4f);

        private readonly FlightComputerWindow _window = new FlightComputerWindow();
        private GameObject _root;
        private RectTransform _rootRect, _textRect;
        private TextMeshProUGUI _text;
        private TooltipController_Text _tooltip;
        private Color _textColor = Color.white;
        private float _height, _iconSize, _nextUpdate;
        private string _shown;
        private bool _uiHidden;
        private static Sprite _icon;

        public void Start()
        {
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
            StartCoroutine(Attach());
        }

        public void OnDestroy()
        {
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            _window.Release();
            if (_root != null) Destroy(_root);
        }

        private void OnHideUI() => _uiHidden = true;
        private void OnShowUI() => _uiHidden = false;

        private IEnumerator Attach()
        {
            // The flight UI builds over the first frames; the panel stays inactive without CommNet.
            for (float waited = 0; waited < 10f; waited += Time.unscaledDeltaTime)
            {
                TelemetryUpdate telemetry = TelemetryUpdate.Instance;
                if (telemetry != null && telemetry.gameObject.activeInHierarchy && ((RectTransform)telemetry.transform).rect.height > 0)
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
            var panel = (RectTransform)telemetry.transform;
            METDisplay met = FindObjectOfType<METDisplay>();
            _height = panel.rect.height;
            _iconSize = Mathf.Round(_height * 0.7f);

            _root = new GameObject("LightLagSignalDelay", typeof(RectTransform));
            _root.layer = telemetry.gameObject.layer;
            _rootRect = (RectTransform)_root.transform;
            _rootRect.SetParent(panel, false);
            _rootRect.anchorMin = _rootRect.anchorMax = new Vector2(1f, 0.5f);
            _rootRect.pivot = new Vector2(0f, 0.5f);
            _rootRect.anchoredPosition = new Vector2(Gap, 0f);
            _rootRect.sizeDelta = new Vector2(80f, _height);
            _root.AddComponent<LayoutElement>().ignoreLayout = true;

            // Background: the signal panel's own, or the MET box's.
            Image background = _root.AddComponent<Image>();
            Image source = telemetry.GetComponent<Image>();
            if (source == null && met != null && met.text != null) source = met.text.transform.parent?.GetComponent<Image>();
            if (source != null)
            {
                background.sprite = source.sprite;
                background.type = source.type;
                background.color = source.color;
                background.material = source.material;
            }
            else
            {
                background.color = new Color(0f, 0f, 0f, 0.55f);
            }

            Button button = _root.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(_window.Toggle);

            // Flight computer icon at the left.
            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.layer = _root.layer;
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(_rootRect, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(Pad, 0f);
            iconRect.sizeDelta = new Vector2(_iconSize, _iconSize);
            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = Icon();
            icon.raycastTarget = false;

            // Delay text, styled like the MET display.
            var textObject = new GameObject("Delay", typeof(RectTransform));
            textObject.layer = _root.layer;
            _textRect = (RectTransform)textObject.transform;
            _textRect.SetParent(_rootRect, false);
            _textRect.anchorMin = _textRect.anchorMax = new Vector2(0f, 0.5f);
            _textRect.pivot = new Vector2(0f, 0.5f);
            _textRect.anchoredPosition = new Vector2(Pad + _iconSize + Pad, 0f);
            _text = textObject.AddComponent<TextMeshProUGUI>();
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            _text.alignment = TextAlignmentOptions.MidlineLeft;
            if (met != null && met.text != null)
            {
                _text.font = met.text.font;
                _text.fontSharedMaterial = met.text.fontSharedMaterial;
                _text.fontSize = met.text.fontSize;
                _text.fontStyle = met.text.fontStyle;
                _textColor = met.text.color;
            }
            else
            {
                _text.fontSize = Mathf.Round(_height * 0.55f);
            }
            _text.color = _textColor;
            icon.color = _textColor;

            if (telemetry.control_tooltip != null && telemetry.control_tooltip.prefab != null)
            {
                _tooltip = _root.AddComponent<TooltipController_Text>();
                _tooltip.prefab = telemetry.control_tooltip.prefab;
                _tooltip.continuousUpdate = true;
                _tooltip.RequireInteractable = false;
            }

            _shown = null;
            _nextUpdate = 0;
            Log.Info($"signal bar: delay readout attached to {Path(panel)} ({panel.rect.width:0}×{panel.rect.height:0})");
        }

        public void Update()
        {
            if (_root == null || Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + UpdateSeconds;

            Vessel vessel = FlightGlobals.ActiveVessel;
            string text = DelayStatus.Short(vessel);
            if (_root.activeSelf != (text != null)) _root.SetActive(text != null);
            if (text == null)
            {
                _window.Close();
                return;
            }
            if (text != _shown)
            {
                _shown = text;
                _text.text = text;
                float width = _text.GetPreferredValues(text).x;
                _textRect.sizeDelta = new Vector2(width, _height);
                _rootRect.sizeDelta = new Vector2(Pad + _iconSize + Pad + width + Pad + 2f, _height);
            }
            _text.color = DelayStatus.Of(vessel, out _) == DelayStatus.State.NoSignal ? NoSignalColor : _textColor;
            _tooltip?.SetText(DelayStatus.Long(vessel) + "\n" + Localizer.Format("#LightLag_Bar_Tooltip"));
        }

        public void OnGUI()
        {
            if (_uiHidden)
            {
                _window.Release();
                return;
            }
            _window.Draw();
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        /// <summary>A stopwatch: the flight computer's icon, drawn once in white so the Image tints it.</summary>
        private static Sprite Icon()
        {
            if (_icon != null) return _icon;
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
            _icon = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _icon;
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Mathf.Clamp01(halfWidth + 0.5f - (p - (a + t * ab)).magnitude);
        }
    }
}
