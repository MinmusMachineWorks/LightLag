using System;

namespace LightLag
{
    /// <summary>A SAS mode (navball buttons: stability assist, prograde, target, maneuver, …).</summary>
    public class SasModeCommand : QueuedCommand
    {
        public const string Name = "SasMode";

        public VesselAutopilot.AutopilotMode Mode;

        public override string TypeName => Name;
        public override string Label => KSP.Localization.Localizer.Format("#LightLag_Command_SasMode", Mode.ToString());

        public override string Execute(Vessel vessel)
        {
            if (!vessel.Autopilot.CanSetMode(Mode) || !vessel.Autopilot.SetMode(Mode))
                return KSP.Localization.Localizer.Format("#LightLag_Error_SasMode", Mode.ToString());
            return null;
        }

        protected override void SavePayload(ConfigNode node) => node.AddValue("mode", Mode.ToString());

        protected override void LoadPayload(ConfigNode node)
        {
            string mode = node.GetValue("mode");
            if (mode != null && Enum.TryParse(mode, out VesselAutopilot.AutopilotMode parsed)) Mode = parsed;
        }
    }
}
