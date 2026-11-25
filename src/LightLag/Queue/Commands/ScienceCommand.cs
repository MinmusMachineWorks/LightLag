using System.Collections.Generic;
using HarmonyLib;
using KSP.Localization;

namespace LightLag
{
    /// <summary>
    /// A button in the science results dialog (Review Data): Transmit, Discard or Reset, or
    /// Process in lab. It runs the data's module callback (the one the dialog page was given)
    /// on the same data, found again by subject when the command arrives.
    /// </summary>
    public class ScienceCommand : QueuedCommand
    {
        public const string Name = "Science";

        public enum Kind { Transmit, Discard, Lab }

        public Kind Action;
        public PartRef Module;
        /// <summary>The module's callback, a method taking the <see cref="ScienceData"/>.</summary>
        public string Method;
        public string SubjectId;
        public string DataTitle;

        public override string TypeName => Name;

        public override string Label => Localizer.Format("#LightLag_Command_Science_" + Action, DataTitle);

        public override string Execute(Vessel vessel)
        {
            Part part = Module.FindPart(vessel);
            PartModule module = Module.FindModule(part);
            if (module == null) return Localizer.Format("#LightLag_Error_PartGone");
            ScienceData data = Find(module as IScienceDataContainer, SubjectId);
            if (data == null) return Localizer.Format("#LightLag_Error_NoData");
            var callback = AccessTools.Method(module.GetType(), Method, new[] { typeof(ScienceData) });
            if (callback == null) return Localizer.Format("#LightLag_Error_NoData");
            callback.Invoke(module, new object[] { data });
            return null;
        }

        internal static ScienceData Find(IScienceDataContainer container, string subjectId)
        {
            ScienceData[] all = container?.GetData();
            if (all == null) return null;
            foreach (ScienceData d in all)
                if (d != null && d.subjectID == subjectId) return d;
            return null;
        }

        protected override void SavePayload(ConfigNode node)
        {
            node.AddValue("action", Action);
            node.AddValue("method", Method);
            node.AddValue("subject", SubjectId);
            node.AddValue("title", DataTitle);
            Module.Save(node.AddNode("TARGET"));
        }

        protected override void LoadPayload(ConfigNode node)
        {
            if (System.Enum.TryParse(node.GetValue("action"), out Kind action)) Action = action;
            Method = node.GetValue("method");
            SubjectId = node.GetValue("subject");
            DataTitle = node.GetValue("title") ?? SubjectId;
            ConfigNode target = node.GetNode("TARGET");
            if (target != null && PartRef.TryLoad(target, out PartRef module)) Module = module;
            else Module = new PartRef { ModuleIndex = -1 };
        }

        internal override bool IsValid => !string.IsNullOrEmpty(Method) && !string.IsNullOrEmpty(SubjectId) && Module.ModuleIndex >= 0;
    }
}
