using System.Collections.Generic;
using KSP.Localization;

namespace LightLag
{
    /// <summary>
    /// A part-menu button (a <c>KSPEvent</c>: extend, retract, deploy, run experiment, …) on one
    /// or more parts. Symmetry counterparts clicked together share one command.
    /// </summary>
    public class PartEventCommand : QueuedCommand
    {
        public const string Name = "PartEvent";

        public string EventName;
        public string GuiName;
        public string PartTitle;
        public readonly List<PartRef> Targets = new List<PartRef>();

        /// <summary>Frame the command was queued in; not saved.</summary>
        internal int QueuedFrame = -1;

        public override string TypeName => Name;

        public override string Label => Targets.Count > 1
            ? Localizer.Format("#LightLag_Command_PartEventMany", GuiName, PartTitle, Targets.Count)
            : Localizer.Format("#LightLag_Command_PartEvent", GuiName, PartTitle);

        public override string Execute(Vessel vessel)
        {
            int failed = 0;
            foreach (PartRef target in Targets)
            {
                Part part = target.FindPart(vessel);
                BaseEvent evt = null;
                if (part != null)
                {
                    BaseEventList events = target.ModuleIndex < 0 ? part.Events : target.FindModule(part)?.Events;
                    evt = events?[EventName];
                }
                if (evt == null || !evt.active) { failed++; continue; }
                evt.Invoke();
            }
            if (failed == 0) return null;
            return Localizer.Format("#LightLag_Error_PartEvent", failed, Targets.Count);
        }

        protected override void SavePayload(ConfigNode node)
        {
            node.AddValue("event", EventName);
            node.AddValue("guiName", GuiName);
            node.AddValue("partTitle", PartTitle);
            foreach (PartRef target in Targets) target.Save(node.AddNode("TARGET"));
        }

        protected override void LoadPayload(ConfigNode node)
        {
            EventName = node.GetValue("event");
            GuiName = node.GetValue("guiName") ?? EventName;
            PartTitle = node.GetValue("partTitle") ?? "";
            foreach (ConfigNode t in node.GetNodes("TARGET"))
                if (PartRef.TryLoad(t, out PartRef target)) Targets.Add(target);
        }

        internal override bool IsValid => !string.IsNullOrEmpty(EventName) && Targets.Count > 0;
    }
}
