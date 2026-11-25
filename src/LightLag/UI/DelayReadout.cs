using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// A one-line readout for the active vessel in flight: its signal delay, "no signal", or
    /// "local control", plus how many inputs are still in flight. Drawn with IMGUI under the
    /// stock time and signal display; hidden in map view, with the UI hidden (F2), and when
    /// delay is off or CommNet isn't running.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class DelayReadout : MonoBehaviour
    {
        private GUIStyle _style;
        private bool _uiHidden;

        public void Start()
        {
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
        }

        public void OnDestroy()
        {
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
        }

        private void OnHideUI() => _uiHidden = true;
        private void OnShowUI() => _uiHidden = false;

        public void OnGUI()
        {
            if (_uiHidden || MapView.MapIsEnabled || Event.current.type != EventType.Repaint) return;
            string text = Text(FlightGlobals.ActiveVessel);
            if (text == null) return;

            float scale = GameSettings.UI_SCALE;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { wordWrap = false };
            _style.fontSize = Mathf.RoundToInt(13 * scale);

            var rect = new Rect(8 * scale, 64 * scale, 320 * scale, 22 * scale);
            var shadow = new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height);
            _style.normal.textColor = Color.black;
            GUI.Label(shadow, text, _style);
            _style.normal.textColor = new Color(0.65f, 1f, 0.6f);
            GUI.Label(rect, text, _style);
        }

        internal static string Text(Vessel vessel)
        {
            LightLagParams settings = LightLagParams.Current;
            if (vessel == null || settings == null || !settings.delayEnabled || !CommNet.CommNetScenario.CommNetEnabled)
                return null;
            if (LocalControl.Has(vessel)) return Localize("#LightLag_Readout_Local");

            double delay = DelayService.GetDelay(vessel);
            int pending = vessel.FindVesselModuleImplementing<LightLagVessel>()?.Input?.PendingCount ?? 0;
            string text = double.IsInfinity(delay)
                ? Localize("#LightLag_Readout_NoSignal")
                : KSP.Localization.Localizer.Format("#LightLag_Readout_Delay", DelayService.Format(delay));
            if (pending > 0) text += " · " + KSP.Localization.Localizer.Format("#LightLag_Readout_Pending", pending);
            return text;
        }

        private static string Localize(string tag) => KSP.Localization.Localizer.Format(tag);
    }
}
