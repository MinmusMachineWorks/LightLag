using KSP.Localization;

namespace LightLag
{
    /// <summary>What happens to the throttle of an uncrewed vessel when its signal is lost.</summary>
    public enum ThrottleOnSignalLoss
    {
        Cut,
        Hold,
    }

    /// <summary>
    /// LightLag's difficulty settings. They sit in the same section as stock CommNet's settings.
    /// Nothing reads them yet: delay arrives in milestone M1.
    /// </summary>
    public class LightLagParams : GameParameters.CustomParameterNode
    {
        public override string Title => Localizer.Format("#LightLag_Settings_Title");
        public override string DisplaySection => Localizer.Format("#autoLoc_6002170"); // stock CommNet's section
        public override string Section => "Advanced";
        public override int SectionOrder => 2;
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;

        [GameParameters.CustomParameterUI("#LightLag_Settings_DelayEnabled", toolTip = "#LightLag_Settings_DelayEnabled_Tip")]
        public bool delayEnabled = true;

        [GameParameters.CustomParameterUI("#LightLag_Settings_RoundTrip", toolTip = "#LightLag_Settings_RoundTrip_Tip")]
        public bool roundTrip = false;

        [GameParameters.CustomFloatParameterUI("#LightLag_Settings_LightSpeed", toolTip = "#LightLag_Settings_LightSpeed_Tip",
            minValue = 0.01f, maxValue = 100f, logBase = 10f, stepCount = 100, displayFormat = "F2")]
        public float lightSpeedMultiplier = 1f;

        [GameParameters.CustomParameterUI("#LightLag_Settings_ThrottleOnLoss", toolTip = "#LightLag_Settings_ThrottleOnLoss_Tip")]
        public ThrottleOnSignalLoss throttleOnSignalLoss = ThrottleOnSignalLoss.Cut;

        /// <summary>The current game's settings, or null outside a game.</summary>
        public static LightLagParams Current => HighLogic.CurrentGame?.Parameters.CustomParams<LightLagParams>();
    }
}
