using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Localization;

namespace LightLag
{
    /// <summary>
    /// A call to a method a window's button makes (<see cref="Windows"/>). It's saved when it's
    /// an instance method on a part module with simple arguments: the module, the method and its
    /// parameter types, and the arguments as text. Any other call (a window's own method, a
    /// setter on a mod's object) is held in memory and lost on a reload or scene change; the
    /// queue window marks it.
    /// </summary>
    public class MethodCommand : QueuedCommand
    {
        public const string Name = "Method";

        public string TypeFullName;
        public string MethodName;
        public string[] ParamTypes = new string[0];
        public string[] ArgTexts = new string[0];
        public PartRef Target = new PartRef { ModuleIndex = -1 };
        public string LabelText;
        public string PartTitle;

        internal bool Persistent;
        internal MethodBase Method;
        internal object Instance;
        internal object[] Args = new object[0];
        internal float QueuedRealtime;

        public override string TypeName => Name;
        internal override bool Saveable => Persistent;

        public override string Label
        {
            get
            {
                string args = string.Join(", ", ArgTexts.Where(a => !string.IsNullOrEmpty(a)).Select(Short));
                string text = string.IsNullOrEmpty(args) ? LabelText : $"{LabelText} {args}";
                return string.IsNullOrEmpty(PartTitle) ? text : Localizer.Format("#LightLag_Command_PartEvent", text, PartTitle);
            }
        }

        internal static MethodCommand Create(WindowCommandSpec spec, MethodBase method, object instance, object[] args, Part part)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var command = new MethodCommand
            {
                TypeFullName = method.DeclaringType?.FullName,
                MethodName = method.Name,
                ParamTypes = parameters.Select(p => p.ParameterType.FullName).ToArray(),
                ArgTexts = args.Select((a, i) => i < parameters.Length && !parameters[i].IsOut && a != null && Windows.IsSimple(a.GetType())
                    ? ValueCodec.Format(a) : null).ToArray(),
                LabelText = spec.Label,
                PartTitle = part?.partInfo?.title,
                Persistent = spec.Persistable && instance is PartModule,
                Method = method,
                Instance = instance,
                Args = args,
                QueuedRealtime = UnityEngine.Time.realtimeSinceStartup,
            };
            if (command.Persistent)
            {
                var module = (PartModule)instance;
                command.Target = PartRef.Of(module.part, module);
            }
            return command;
        }

        private static string Short(string text)
            => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)
                ? d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : text;

        internal bool SameTarget(MethodBase method, object instance) => Method == method && ReferenceEquals(Instance, instance);

        internal bool SameArgs(object[] args) => Args.Length == args.Length && Args.Zip(args, Equals).All(x => x);

        public override string Execute(Vessel vessel)
        {
            object instance = Instance;
            MethodBase method = Method;
            object[] args = Args;
            if (Persistent)
            {
                PartModule module = Target.FindModule(Target.FindPart(vessel));
                if (module == null) return Localizer.Format("#LightLag_Error_PartGone");
                instance = module;
                if (method == null)
                {
                    Type type = AccessTools.TypeByName(TypeFullName);
                    Type[] types = ParamTypes.Select(AccessTools.TypeByName).ToArray();
                    method = type == null || types.Any(t => t == null) ? null : AccessTools.Method(type, MethodName, types);
                    if (method == null) return Localizer.Format("#LightLag_Error_Unavailable");
                    args = types.Select((t, i) => ValueCodec.Parse(i < ArgTexts.Length ? ArgTexts[i] : null, t)).ToArray();
                }
            }
            else if (instance is UnityEngine.Object unityObject && unityObject == null)
            {
                return Localizer.Format("#LightLag_Error_Unavailable");
            }

            object result;
            try
            {
                result = method.Invoke(method.IsStatic ? null : instance, args);
            }
            catch (TargetInvocationException e)
            {
                Log.Warn($"{TypeFullName}.{MethodName} threw: {e.InnerException}");
                return Localizer.Format("#LightLag_Error_Exception", e.InnerException?.Message);
            }
            if (result is bool ok && !ok)
            {
                // A bool method's out string (MKS's Launch) says why.
                ParameterInfo[] parameters = method.GetParameters();
                for (int i = 0; i < parameters.Length && i < args.Length; i++)
                    if (parameters[i].IsOut && args[i] is string why && why.Length > 0) return why;
                return Localizer.Format("#LightLag_Error_Refused");
            }
            return null;
        }

        protected override void SavePayload(ConfigNode node)
        {
            node.AddValue("class", TypeFullName);
            node.AddValue("method", MethodName);
            node.AddValue("label", LabelText);
            if (!string.IsNullOrEmpty(PartTitle)) node.AddValue("partTitle", PartTitle);
            for (int i = 0; i < ParamTypes.Length; i++)
            {
                ConfigNode arg = node.AddNode("ARG");
                arg.AddValue("type", ParamTypes[i]);
                if (i < ArgTexts.Length && ArgTexts[i] != null) arg.AddValue("value", ArgTexts[i]);
            }
            Target.Save(node.AddNode("TARGET"));
        }

        protected override void LoadPayload(ConfigNode node)
        {
            Persistent = true;
            TypeFullName = node.GetValue("class");
            MethodName = node.GetValue("method");
            LabelText = node.GetValue("label") ?? MethodName;
            PartTitle = node.GetValue("partTitle");
            ConfigNode[] args = node.GetNodes("ARG");
            ParamTypes = args.Select(a => a.GetValue("type")).ToArray();
            ArgTexts = args.Select(a => a.GetValue("value")).ToArray();
            ConfigNode target = node.GetNode("TARGET");
            if (target != null && PartRef.TryLoad(target, out PartRef t)) Target = t;
        }

        internal override bool IsValid => !string.IsNullOrEmpty(TypeFullName) && !string.IsNullOrEmpty(MethodName)
            && Target.ModuleIndex >= 0 && ParamTypes.All(p => p != null);
    }
}
