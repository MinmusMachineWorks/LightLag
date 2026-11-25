using System;

namespace LightLag
{
    /// <summary>An action group toggle or set: SAS, RCS, gear, lights, brakes, abort, custom 1–10.</summary>
    public class ActionGroupCommand : QueuedCommand
    {
        public const string Name = "ActionGroup";

        public KSPActionGroup Group;

        /// <summary>True for a toggle; false to set the group to <see cref="State"/>.</summary>
        public bool Toggle;

        public bool State;

        public override string TypeName => Name;

        public override string Label => Toggle
            ? KSP.Localization.Localizer.Format("#LightLag_Command_Toggle", GroupName(Group))
            : KSP.Localization.Localizer.Format(State ? "#LightLag_Command_On" : "#LightLag_Command_Off", GroupName(Group));

        public override string Execute(Vessel vessel)
        {
            if (Toggle) vessel.ActionGroups.ToggleGroup(Group);
            else vessel.ActionGroups.SetGroup(Group, State);
            return null;
        }

        protected override void SavePayload(ConfigNode node)
        {
            node.AddValue("group", Group.ToString());
            node.AddValue("toggle", Toggle);
            node.AddValue("state", State);
        }

        protected override void LoadPayload(ConfigNode node)
        {
            string group = node.GetValue("group");
            if (group != null && Enum.TryParse(group, out KSPActionGroup parsed)) Group = parsed;
            node.TryGetValue("toggle", ref Toggle);
            node.TryGetValue("state", ref State);
        }

        internal static string GroupName(KSPActionGroup group)
        {
            switch (group)
            {
                case KSPActionGroup.Custom01: return "AG 1";
                case KSPActionGroup.Custom02: return "AG 2";
                case KSPActionGroup.Custom03: return "AG 3";
                case KSPActionGroup.Custom04: return "AG 4";
                case KSPActionGroup.Custom05: return "AG 5";
                case KSPActionGroup.Custom06: return "AG 6";
                case KSPActionGroup.Custom07: return "AG 7";
                case KSPActionGroup.Custom08: return "AG 8";
                case KSPActionGroup.Custom09: return "AG 9";
                case KSPActionGroup.Custom10: return "AG 10";
                default: return group.ToString();
            }
        }
    }
}
