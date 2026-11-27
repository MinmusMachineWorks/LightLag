using System;
using System.Reflection;
using HarmonyLib;
using KSP.Localization;

namespace LightLag
{
    /// <summary>
    /// A command another mod sent through <see cref="LightLagAPI.QueueCommand"/>. It's the mod's
    /// node, saved as it is; when it runs, LightLag calls the node's
    /// <c>ReflectionType.ReflectionPopMethod(ConfigNode)</c> with a copy of it.
    /// </summary>
    public class ExternalCommand : QueuedCommand
    {
        public const string Name = "External";

        /// <summary>What <see cref="LightLagAPI.QueueCommand"/> returned; saved with it.</summary>
        public Guid Id;
        public ConfigNode Data;

        public override string TypeName => Name;

        public override string Label =>
            Data?.GetValue("QueueLabel") is string label && label.Length > 0 ? label
            : Data?.GetValue("Executor") ?? Data?.GetValue("ReflectionPopMethod") ?? Name;

        private string TypeText => Data?.GetValue("ReflectionType");
        private string MethodText => Data?.GetValue("ReflectionPopMethod");

        /// <summary>A command for <paramref name="node"/> with a new id, or null if it names no callback.</summary>
        internal static ExternalCommand From(ConfigNode node)
        {
            var command = new ExternalCommand { Id = Guid.NewGuid(), Data = node.CreateCopy() };
            return command.IsValid ? command : null;
        }

        internal override bool IsValid => Id != Guid.Empty && !string.IsNullOrEmpty(TypeText) && !string.IsNullOrEmpty(MethodText);

        public override string Execute(Vessel vessel)
        {
            Type type = Type.GetType(TypeText) ?? AccessTools.TypeByName(TypeText);
            MethodInfo method = type?.GetMethod(MethodText, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ConfigNode) }, null);
            if (method == null)
            {
                Log.Warn($"API: {TypeText}.{MethodText}(ConfigNode) not found for '{Label}'");
                return Localizer.Format("#LightLag_Error_Unavailable");
            }
            try
            {
                method.Invoke(null, new object[] { Data.CreateCopy() });
                return null;
            }
            catch (TargetInvocationException e)
            {
                Log.Warn($"API: {TypeText}.{MethodText} threw: {e.InnerException}");
                return Localizer.Format("#LightLag_Error_Exception", e.InnerException?.Message);
            }
        }

        protected override void SavePayload(ConfigNode node)
        {
            node.AddValue("id", Id.ToString());
            if (Data != null) node.AddNode("DATA").AddNode(Data.CreateCopy());
        }

        protected override void LoadPayload(ConfigNode node)
        {
            Guid.TryParse(node.GetValue("id"), out Id);
            ConfigNode[] data = node.GetNode("DATA")?.GetNodes();
            Data = data != null && data.Length > 0 ? data[0].CreateCopy() : null;
        }
    }
}
