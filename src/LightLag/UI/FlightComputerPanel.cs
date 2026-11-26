using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using KSP.UI;
using KSP.UI.TooltipTypes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightLag
{
    /// <summary>
    /// The flight computer: the active vessel's signal and command queue, in LightLag's own
    /// "mission console" look (black glass, monospace, signal green, amber for what's on its
    /// way). Built in code with uGUI on KSP's app canvas, so it follows the UI and app scale,
    /// hides with the UI (F2), and takes clicks over it. Opened from the signal bar
    /// (<see cref="SignalBarWidget"/>), which also drives <see cref="Refresh"/>.
    ///
    /// Shows the delay and the control path hop by hop, whether stick and throttle input is
    /// still arriving and until when (it can't be cancelled), each queued command with its
    /// countdown and an abort button (an abort is itself a signal, so it's allowed only if it can
    /// arrive before the command runs), and the vessel's extra delay.
    /// </summary>
    internal class FlightComputerPanel
    {
        private const string InputLock = "LightLag_FlightComputer";
        private const float Width = 380f;

        internal static readonly Color Green = new Color(0.49f, 1f, 0.54f);
        private static readonly Color Dim = new Color(0.31f, 0.68f, 0.36f);
        private static readonly Color Amber = new Color(1f, 0.71f, 0.28f);
        private static readonly Color Red = new Color(1f, 0.48f, 0.48f);
        private static readonly Color Glass = new Color(0.043f, 0.059f, 0.047f, 0.95f);
        private static readonly Color Border = new Color(0.184f, 0.42f, 0.227f);
        private static readonly Color Line = new Color(0.118f, 0.239f, 0.141f);
        private static readonly Color RowGlass = new Color(0.063f, 0.102f, 0.071f, 1f);
        private static readonly Color AbortGlass = new Color(0.3f, 0.09f, 0.09f, 1f);
        private static readonly Color Disabled = new Color(0.35f, 0.4f, 0.36f);

        /// <summary>Where the window sits (top-left corner, from the canvas's top-left), kept for the session.</summary>
        private static Vector2 _position = new Vector2(260f, -90f);
        private static TMP_FontAsset _font;

        private readonly Tooltip_Text _tooltipPrefab;
        private GameObject _root;
        private RectTransform _rect;
        private TextMeshProUGUI _title, _queued, _delay, _path, _distance, _input, _empty;
        private RectTransform _rows;
        private TMP_InputField _extra;
        private readonly Dictionary<QueuedCommand, Row> _rowFor = new Dictionary<QueuedCommand, Row>();
        private Guid _vesselId;

        private sealed class Row
        {
            public GameObject Root;
            public TextMeshProUGUI Label, Countdown, AbortText;
            public Button Abort;
        }

        public FlightComputerPanel(Tooltip_Text tooltipPrefab) => _tooltipPrefab = tooltipPrefab;

        public bool Visible => _root != null && _root.activeSelf;

        public void Toggle()
        {
            if (Visible) Close();
            else Open();
        }

        public void Open()
        {
            if (_root == null && !Build()) return;
            _root.SetActive(true);
            _rect.SetAsLastSibling();
            _vesselId = Guid.Empty;
            Refresh(FlightGlobals.ActiveVessel);
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
            InputLockManager.RemoveControlLock(InputLock);
        }

        public void Destroy()
        {
            InputLockManager.RemoveControlLock(InputLock);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        /// <summary>Every frame: typing in the extra delay field mustn't stage the vessel or toggle its groups.</summary>
        public void UpdateLock()
        {
            if (Visible && _extra != null && _extra.isFocused)
                InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, InputLock);
            else
                InputLockManager.RemoveControlLock(InputLock);
        }

        public void Refresh(Vessel vessel)
        {
            if (!Visible) return;
            LightLagVessel module = LightLagVessel.For(vessel);
            if (module == null) return;
            double now = Planetarium.GetUniversalTime();
            DelayStatus.State state = DelayStatus.Of(vessel, out double delay);

            if (vessel.id != _vesselId)
            {
                _vesselId = vessel.id;
                _title.text = Localizer.Format("#LightLag_Console_Title", vessel.vesselName);
                if (!_extra.isFocused) _extra.text = module.ExtraDelay.ToString("0.###", CultureInfo.InvariantCulture);
            }

            int queued = module.Queue.Count;
            _queued.text = queued > 0 ? Localizer.Format("#LightLag_Console_Queued", queued) : "";

            switch (state)
            {
                case DelayStatus.State.Delay:
                    _delay.text = Localizer.Format("#LightLag_Console_Delay", Mono(DelayService.Format(delay)));
                    _delay.color = Green;
                    break;
                case DelayStatus.State.NoSignal:
                    _delay.text = Localizer.Format("#LightLag_Console_NoSignal");
                    _delay.color = Red;
                    break;
                case DelayStatus.State.Local:
                    _delay.text = Localizer.Format("#LightLag_Console_Local");
                    _delay.color = Green;
                    break;
                default:
                    _delay.text = Localizer.Format("#LightLag_Console_NoDelay");
                    _delay.color = Green;
                    break;
            }
            SetPath(vessel, state == DelayStatus.State.Delay);

            // Stick and throttle are analog: holding a key sends a stream of changes, so they're
            // one line (still arriving, and until when), not a list of commands.
            FlyByWireDelay input = module.Input;
            bool arriving = input != null && input.PendingCount > 0;
            _input.gameObject.SetActive(arriving);
            if (arriving)
                _input.text = Localizer.Format("#LightLag_Console_Input", Mono(DelayService.Format(Math.Max(0, input.LastArrivalUT - now))));

            RefreshRows(vessel, module, now, double.IsInfinity(delay) ? double.PositiveInfinity : delay);
            _empty.gameObject.SetActive(queued == 0);
        }

        private void SetPath(Vessel vessel, bool show)
        {
            CommNet.CommPath path = show ? vessel.connection?.ControlPath : null;
            bool any = path != null && path.Count > 0;
            _path.gameObject.SetActive(any);
            _distance.gameObject.SetActive(any);
            if (!any) return;

            double speed = DelayService.SpeedOfLight * (LightLagParams.Current?.lightSpeedMultiplier ?? 1.0);
            var sb = new System.Text.StringBuilder();
            double meters = 0;
            for (int i = 0; i < path.Count; i++)
            {
                CommNet.CommLink link = path[i];
                if (link?.a == null || link.b == null) continue;
                double hop = (link.a.position - link.b.position).magnitude;
                meters += hop;
                if (sb.Length == 0) sb.Append(i == 0 ? vessel.vesselName : link.a.displayName);
                sb.Append(" <color=#").Append(ColorUtility.ToHtmlStringRGB(Dim)).Append(">— ")
                  .Append(Mono(DelayService.Format(hop / speed))).Append(" —</color> ").Append(link.b.displayName);
            }
            _path.text = sb.ToString();
            _distance.text = Localizer.Format("#LightLag_Console_Distance", (meters / 1000.0).ToString("N0", CultureInfo.CurrentCulture), path.Count);
        }

        private void RefreshRows(Vessel vessel, LightLagVessel module, double now, double delay)
        {
            IReadOnlyList<QueuedCommand> commands = module.Queue.Commands;
            foreach (QueuedCommand gone in _rowFor.Keys.Where(c => !commands.Contains(c)).ToList())
            {
                UnityEngine.Object.Destroy(_rowFor[gone].Root);
                _rowFor.Remove(gone);
            }
            for (int i = 0; i < commands.Count; i++)
            {
                QueuedCommand command = commands[i];
                if (!_rowFor.TryGetValue(command, out Row row))
                {
                    row = NewRow(vessel, command);
                    _rowFor[command] = row;
                }
                row.Root.transform.SetSiblingIndex(i);
                row.Label.text = command.Saveable ? command.Label : Localizer.Format("#LightLag_Queue_NotSaved", command.Label);

                if (command.CancelPending)
                {
                    row.Countdown.text = Localizer.Format("#LightLag_Console_Aborting", Mono(DelayService.Format(Math.Max(0, command.CancelArrivalUT - now))));
                    row.Countdown.color = Red;
                }
                else if (now < command.ArrivalUT)
                {
                    row.Countdown.text = Localizer.Format("#LightLag_Console_Arrives", Mono(DelayService.Format(command.ArrivalUT - now)));
                    row.Countdown.color = Amber;
                }
                else
                {
                    row.Countdown.text = Localizer.Format("#LightLag_Console_Held", Mono(DelayService.Format(Math.Max(0, command.ExecuteUT - now))));
                    row.Countdown.color = Green;
                }
                bool canAbort = CommandQueue.CanCancel(command, now, delay);
                row.Abort.interactable = canAbort;
                row.AbortText.color = canAbort ? Red : Disabled;
            }
        }

        private Row NewRow(Vessel vessel, QueuedCommand command)
        {
            var row = new Row();
            GameObject root = Panel("Command", _rows, RowGlass);
            row.Root = root;
            HorizontalLayoutGroup h = Horizontal(root, 6f, new RectOffset(0, 4, 3, 3));
            h.childAlignment = TextAnchor.MiddleLeft;

            GameObject bar = Panel("Bar", root.transform, Amber);
            Size(bar, 2f, 18f);

            row.Label = Text("Label", root.transform, "", 13f, Green, FontStyles.UpperCase);
            row.Label.overflowMode = TextOverflowModes.Ellipsis;
            row.Label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            row.Countdown = Text("Countdown", root.transform, "", 13f, Amber);
            row.Countdown.alignment = TextAlignmentOptions.MidlineRight;

            row.Abort = Button("Abort", root.transform, Localizer.Format("#LightLag_Console_Abort"), AbortGlass, Red, out row.AbortText);
            row.Abort.onClick.AddListener(() =>
            {
                Vessel v = FlightGlobals.ActiveVessel;
                double now = Planetarium.GetUniversalTime();
                double delay = DelayService.GetDelay(v);
                if (!CommandQueue.CanCancel(command, now, delay)) return;
                command.CancelArrivalUT = now + delay;
                Log.Info($"cancel sent for {command.TypeName} '{command.Label}' on {v.vesselName}, arrives in {delay:0.###} s");
            });
            return row;
        }

        private bool Build()
        {
            Canvas canvas = UIMasterController.Instance?.appCanvas;
            if (canvas == null) return false;
            if (_font == null) _font = FindFont();

            // Border: a green frame one pixel around the glass.
            _root = new GameObject("LightLagFlightComputer", typeof(RectTransform));
            _root.layer = canvas.gameObject.layer;
            _rect = (RectTransform)_root.transform;
            _rect.SetParent(canvas.transform, false);
            _rect.anchorMin = _rect.anchorMax = _rect.pivot = new Vector2(0f, 1f);
            _rect.anchoredPosition = _position;
            _rect.sizeDelta = new Vector2(Width, 100f);
            _root.AddComponent<Image>().color = Border;
            Vertical(_root, 0f, new RectOffset(1, 1, 1, 1));
            _root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject glass = Panel("Glass", _rect, Glass);
            Vertical(glass, 5f, new RectOffset(10, 10, 6, 10));

            // Header: title, queued count, close. Dragging it moves the window.
            GameObject header = Group("Header", glass.transform);
            Horizontal(header, 8f, new RectOffset(0, 0, 0, 2)).childAlignment = TextAnchor.MiddleLeft;
            header.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            header.AddComponent<WindowDrag>().Target = _rect;
            _title = Text("Title", header.transform, "", 13f, Green, FontStyles.UpperCase);
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _queued = Text("Queued", header.transform, "", 13f, Amber, FontStyles.UpperCase);
            Button close = Button("Close", header.transform, "×", new Color(0f, 0f, 0f, 0f), Dim, out _);
            close.onClick.AddListener(Close);
            Rule(glass.transform);

            _delay = Text("Delay", glass.transform, "", 24f, Green);
            _path = Text("Path", glass.transform, "", 13f, Green);
            _path.enableWordWrapping = true;
            _distance = Text("Distance", glass.transform, "", 11f, Dim);
            _input = Text("Input", glass.transform, "", 13f, Amber, FontStyles.UpperCase);

            Text("QueueLabel", glass.transform, Localizer.Format("#LightLag_Console_Queue"), 11f, Dim, FontStyles.UpperCase);
            GameObject rows = Group("Rows", glass.transform);
            Vertical(rows, 3f, new RectOffset(0, 0, 0, 0));
            _rows = (RectTransform)rows.transform;
            _empty = Text("Empty", glass.transform, Localizer.Format("#LightLag_Queue_Empty"), 13f, Dim, FontStyles.UpperCase);
            Rule(glass.transform);

            // Extra delay.
            GameObject extraRow = Group("ExtraDelay", glass.transform);
            Horizontal(extraRow, 8f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleLeft;
            TextMeshProUGUI extraLabel = Text("Label", extraRow.transform, Localizer.Format("#LightLag_Queue_ExtraDelay"), 13f, Dim, FontStyles.UpperCase);
            extraLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _extra = InputField(extraRow.transform);
            _extra.onValueChanged.AddListener(text =>
            {
                LightLagVessel module = LightLagVessel.For(FlightGlobals.ActiveVessel);
                if (module != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0)
                    module.ExtraDelay = seconds;
            });
            if (_tooltipPrefab != null)
            {
                var tip = extraRow.AddComponent<TooltipController_Text>();
                tip.prefab = _tooltipPrefab;
                tip.RequireInteractable = false;
                tip.SetText(Localizer.Format("#LightLag_Queue_ExtraDelay_Tip"));
                extraRow.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            }

            _root.SetActive(false);
            return true;
        }

        // ---- building blocks ----

        /// <summary>Monospaced digits, so countdowns don't jitter in a proportional font.</summary>
        private static string Mono(string text) => _font != null && IsMono(_font) ? text : $"<mspace=0.6em>{text}</mspace>";

        private static bool IsMono(TMP_FontAsset font) => font.name.IndexOf("mono", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>A monospace font if the game has one loaded, else the default.</summary>
        private static TMP_FontAsset FindFont()
        {
            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            TMP_FontAsset mono = fonts.FirstOrDefault(IsMono);
            TMP_FontAsset font = mono ?? UnityEngine.Object.FindObjectOfType<KSP.UI.Screens.Flight.METDisplay>()?.text?.font ?? TMP_Settings.defaultFontAsset;
            Log.Info($"flight computer: font {font?.name ?? "none"} (of {fonts.Length} loaded{(mono == null ? ", none monospace" : "")})");
            return font;
        }

        private static GameObject Group(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject Panel(string name, Transform parent, Color color)
        {
            GameObject go = Group(name, parent);
            go.AddComponent<Image>().color = color;
            return go;
        }

        private static void Rule(Transform parent) => Size(Panel("Rule", parent, Line), -1f, 1f);

        private static void Size(GameObject go, float width, float height)
        {
            var e = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (width >= 0) e.minWidth = e.preferredWidth = width;
            if (height >= 0) e.minHeight = e.preferredHeight = height;
        }

        private static VerticalLayoutGroup Vertical(GameObject go, float spacing, RectOffset padding)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        private static HorizontalLayoutGroup Horizontal(GameObject go, float spacing, RectOffset padding)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return h;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, FontStyles style = FontStyles.Normal)
        {
            GameObject go = Group(name, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.richText = true;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.text = text;
            return t;
        }

        private static Button Button(string name, Transform parent, string label, Color background, Color textColor, out TextMeshProUGUI text)
        {
            GameObject go = Panel(name, parent, background);
            Horizontal(go, 0f, new RectOffset(6, 6, 1, 1)).childAlignment = TextAnchor.MiddleCenter;
            var button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            button.colors = colors;
            text = Text("Text", go.transform, label, 12f, textColor, FontStyles.UpperCase);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static TMP_InputField InputField(Transform parent)
        {
            GameObject go = Panel("Field", parent, RowGlass);
            Size(go, 80f, 20f);
            go.AddComponent<Outline>().effectColor = Border;

            GameObject area = Group("TextArea", go.transform);
            var areaRect = (RectTransform)area.transform;
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(6f, 1f);
            areaRect.offsetMax = new Vector2(-6f, -1f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI text = Text("Text", area.transform, "", 13f, Green);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.MidlineRight;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = areaRect;
            field.textComponent = text;
            if (_font != null) field.fontAsset = _font;
            field.pointSize = 13f;
            field.contentType = TMP_InputField.ContentType.DecimalNumber;
            field.characterLimit = 8;
            field.customCaretColor = true;
            field.caretColor = Green;
            field.selectionColor = new Color(Dim.r, Dim.g, Dim.b, 0.5f);
            field.targetGraphic = go.GetComponent<Image>();
            return field;
        }

        /// <summary>Drags the window by its header, kept on screen.</summary>
        private sealed class WindowDrag : MonoBehaviour, IDragHandler, IEndDragHandler
        {
            public RectTransform Target;

            public void OnDrag(PointerEventData eventData)
            {
                Canvas canvas = Target.GetComponentInParent<Canvas>();
                float scale = canvas != null ? canvas.scaleFactor : 1f;
                Target.anchoredPosition += eventData.delta / scale;
                UIMasterController.ClampToScreen(Target, Vector2.zero);
            }

            public void OnEndDrag(PointerEventData eventData) => _position = Target.anchoredPosition;
        }
    }
}
