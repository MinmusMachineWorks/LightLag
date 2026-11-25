using System;
using System.Globalization;
using KSP.Localization;
using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// A part-menu control setting a field: a toggle, slider, cycle or option list (thrust
    /// limiter, gimbal limit, crossfeed, …). When it runs it sets the field the way the menu
    /// would: on symmetry counterparts too if the control asks for it, and with the module's
    /// change callbacks. A slider drag is merged into one command carrying the last value.
    /// </summary>
    public class PartFieldCommand : QueuedCommand
    {
        public const string Name = "PartField";

        public PartRef Target;
        public string FieldName;
        public string GuiName;
        public string PartTitle;
        public bool Symmetry;

        /// <summary>The value as saved; parsed against the field's type when the command runs.</summary>
        public string ValueText;

        /// <summary>The value as sent, while the game hasn't been reloaded; not saved.</summary>
        internal object Value;

        /// <summary>The module the command was sent to, while the game hasn't been reloaded; not saved.</summary>
        internal PartModule Host;

        /// <summary>Real time of the last change merged into this command; not saved.</summary>
        internal float LastChangeRealtime;

        public override string TypeName => Name;

        public override string Label => Localizer.Format("#LightLag_Command_PartField", GuiName, DisplayValue, PartTitle);

        internal string DisplayValue { get; set; }

        public override string Execute(Vessel vessel)
        {
            Part part = Target.FindPart(vessel);
            PartModule module = Target.FindModule(part);
            BaseField field = Target.ModuleIndex < 0 ? part?.Fields[FieldName] : module?.Fields[FieldName];
            if (field == null) return Localizer.Format("#LightLag_Error_PartGone");

            object host = field.host;
            object old = field.GetValue(host);
            object value = Value ?? ValueCodec.Parse(ValueText, old?.GetType());
            if (value == null) return Localizer.Format("#LightLag_Error_PartValue", ValueText);

            if (Symmetry)
            {
                foreach (Part counterpart in part.symmetryCounterparts)
                {
                    BaseField other = Target.ModuleIndex < 0
                        ? counterpart.Fields[FieldName]
                        : Target.FindModule(counterpart)?.Fields[FieldName];
                    if (other == null) continue;
                    other.SetValue(value, other.host);
                    other.uiControlFlight?.onSymmetryFieldChanged?.Invoke(field, value);
                }
            }
            field.SetValue(value, host);
            field.uiControlFlight?.onFieldChanged?.Invoke(field, old);
            return null;
        }

        protected override void SavePayload(ConfigNode node)
        {
            Target.Save(node);
            node.AddValue("field", FieldName);
            node.AddValue("guiName", GuiName);
            node.AddValue("partTitle", PartTitle);
            node.AddValue("symmetry", Symmetry);
            node.AddValue("value", ValueText);
            node.AddValue("displayValue", DisplayValue);
        }

        protected override void LoadPayload(ConfigNode node)
        {
            PartRef.TryLoad(node, out Target);
            FieldName = node.GetValue("field");
            GuiName = node.GetValue("guiName") ?? FieldName;
            PartTitle = node.GetValue("partTitle") ?? "";
            node.TryGetValue("symmetry", ref Symmetry);
            ValueText = node.GetValue("value");
            DisplayValue = node.GetValue("displayValue") ?? ValueText;
        }

        internal override bool IsValid => !string.IsNullOrEmpty(FieldName) && ValueText != null;
    }

    /// <summary>Saves and parses field values of the types part menus use.</summary>
    internal static class ValueCodec
    {
        public static string Format(object value)
        {
            switch (value)
            {
                case null: return null;
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case Vector2 v: return ConfigNode.WriteVector(v);
                case Vector3 v: return ConfigNode.WriteVector(v);
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        public static object Parse(string text, Type type)
        {
            if (text == null || type == null) return null;
            try
            {
                if (type == typeof(string)) return text;
                if (type == typeof(Vector2)) return ConfigNode.ParseVector2(text);
                if (type == typeof(Vector3)) return ConfigNode.ParseVector3(text);
                if (type.IsEnum) return Enum.Parse(type, text);
                return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
