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

        [GameParameters.CustomFloatParameterUI("#LightLag_Settings_LightSpeed", toolTip = "#LightLag_Settings_LightSpeed_Tip",
            minValue = 0.01f, maxValue = 100f, logBase = 10f, stepCount = 100, displayFormat = "F2")]
        public float lightSpeedMultiplier = 1f;

        [GameParameters.CustomParameterUI("#LightLag_Settings_ThrottleOnLoss", toolTip = "#LightLag_Settings_ThrottleOnLoss_Tip")]
        public ThrottleOnSignalLoss throttleOnSignalLoss = ThrottleOnSignalLoss.Cut;

        /// <summary>Estimate the next contact or signal loss (<see cref="ContactPredictor"/>). Off by default: it's an estimate.</summary>
        [GameParameters.CustomParameterUI("#LightLag_Settings_SignalEstimates", toolTip = "#LightLag_Settings_SignalEstimates_Tip")]
        public bool signalEstimates = false;

        /// <summary>The current game's settings, or null outside a game.</summary>
        public static LightLagParams Current => HighLogic.CurrentGame?.Parameters.CustomParams<LightLagParams>();
    }
}
