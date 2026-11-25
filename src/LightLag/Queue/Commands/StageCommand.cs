using KSP.UI.Screens;

namespace LightLag
{
    /// <summary>
    /// Staging (the stage key). KSP's staging only acts on the active vessel, so if the player
    /// has switched away by the time it runs, the command fails with a message.
    /// </summary>
    public class StageCommand : QueuedCommand
    {
        public const string Name = "Stage";

        /// <summary>The stage key also toggles the Stage action group; that toggle rides along.</summary>
        public bool ToggleStageGroup;

        /// <summary>Frame the command was queued in; not saved.</summary>
        internal int QueuedFrame = -1;

        public override string TypeName => Name;
        public override string Label => KSP.Localization.Localizer.Format("#LightLag_Command_Stage");

        public override string Execute(Vessel vessel)
        {
            if (vessel != FlightGlobals.ActiveVessel) return KSP.Localization.Localizer.Format("#LightLag_Error_NotActive");
            StageManager.ActivateNextStage();
            if (ToggleStageGroup) vessel.ActionGroups.ToggleGroup(KSPActionGroup.Stage);
            return null;
        }

        protected override void SavePayload(ConfigNode node) => node.AddValue("toggleStageGroup", ToggleStageGroup);

        protected override void LoadPayload(ConfigNode node) => node.TryGetValue("toggleStageGroup", ref ToggleStageGroup);
    }
}
