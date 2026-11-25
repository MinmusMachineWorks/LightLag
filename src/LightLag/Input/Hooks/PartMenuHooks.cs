using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP.Localization;
using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// Harmony hooks that queue the player's part-menu commands on the active vessel:
    /// <list type="bullet">
    /// <item><c>BaseEvent.Invoke()</c> and <c>Invoke(BaseEventDetails)</c>, inside the player's
    /// <c>UIPartActionButton.OnClick</c>: part-menu buttons → <see cref="PartEventCommand"/>.
    /// Counterparts clicked in the same frame join one command.</item>
    /// <item><c>UIPartActionFieldItem.SetFieldValue</c>: every part-menu control (toggle, slider,
    /// cycle, option list, number field) → <see cref="PartFieldCommand"/>. A slider drag is
    /// merged into one command; <c>UIPartActionFloatEdit.Setup</c>, which clamps a value as the
    /// menu opens, is excluded (<see cref="PlayerInput.Suppress"/>).</item>
    /// <item><c>UIPartActionFloatRange.UpdateItem</c> (postfix): a slider shows the value on its
    /// way, so it doesn't snap back while dragged. Other controls show the part's actual state
    /// until the command arrives.</item>
    /// </list>
    /// Deliberately not delayed: parts of other vessels (an EVA kerbal's reach, setting a target),
    /// vessels with local control, events and fields listed in <c>LIGHTLAG_EXEMPTIONS</c>
    /// (Exemptions.cfg) or marked <c>skip_control</c>, and <c>skip_delay</c> ones, which only need a signal.
    /// </summary>
    internal static class PartMenuHooks
    {
        private const float MergeSeconds = 1f;

        public static bool InvokePrefix(BaseEvent __instance) => Intercept(__instance, null, withData: false);

        public static bool InvokeDataPrefix(BaseEvent __instance, BaseEventDetails data) => Intercept(__instance, data, withData: true);

        private static bool Intercept(BaseEvent evt, BaseEventDetails data, bool withData)
        {
            if (!PlayerInput.Active) return true;
            Part part = evt.listParent?.part;
            PartModule module = evt.listParent?.module;
            if (!OnActiveVessel(part)) return true;

            string partTitle = PartTitle(part);
            switch (Exemptions.ForEvent(evt, module))
            {
                case PartMenuRule.Instant:
                    RunNow(evt, data, withData);
                    return false;
                case PartMenuRule.NoDelay:
                    if (!BlockedWithoutSignal(part.vessel, evt.guiName))
                        RunNow(evt, data, withData);
                    return false;
            }

            LightLagVessel lightLag = LightLagVessel.For(part.vessel);
            PartRef target = PartRef.Of(part, module);
            if (lightLag?.Queue.Last is PartEventCommand last && last.QueuedFrame == Time.frameCount
                && last.EventName == evt.name && last.Targets.Count > 0 && last.Targets[0].ModuleClass == target.ModuleClass)
            {
                if (!last.Targets.Exists(t => t.Same(target))) last.Targets.Add(target);
                return false;
            }

            var command = new PartEventCommand
            {
                EventName = evt.name,
                GuiName = string.IsNullOrEmpty(evt.guiName) ? evt.name : evt.guiName,
                PartTitle = partTitle,
                QueuedFrame = Time.frameCount,
            };
            command.Targets.Add(target);
            if (CommandHooks.TryQueue(part.vessel, command)) return false;

            // Not delayed: run it outside player context, so what it calls isn't queued instead.
            RunNow(evt, data, withData);
            return false;
        }

        /// <summary>Prefix on <c>UIPartActionFieldItem.SetFieldValue(object)</c>.</summary>
        public static bool SetFieldValuePrefix(UIPartActionFieldItem __instance, object newValue)
        {
            if (PlayerInput.Suppressed) return true;
            Part part = __instance.Part;
            BaseField field = __instance.Field;
            if (field == null || !OnActiveVessel(part)) return true;
            PartModule module = __instance.IsModule ? __instance.PartModule : null;

            switch (Exemptions.ForField(field, module))
            {
                case PartMenuRule.Instant:
                    return true;
                case PartMenuRule.NoDelay:
                    return !BlockedWithoutSignal(part.vessel, field.guiName);
            }

            LightLagVessel lightLag = LightLagVessel.For(part.vessel);
            if (lightLag == null) return true;
            object host = field.host;
            PartFieldCommand pending = lightLag.Queue.PendingField(host, field.name);
            if (pending == null && Equals(field.GetValue(host), newValue)) return true;
            if (pending != null && Equals(pending.Value, newValue)) return false;

            // A drag sends a change each frame; fold it into the change already on its way.
            if (pending != null && Time.realtimeSinceStartup - pending.LastChangeRealtime < MergeSeconds)
                lightLag.Queue.Remove(pending);

            UI_Control control = __instance.Control;
            var command = new PartFieldCommand
            {
                Target = PartRef.Of(part, module),
                FieldName = field.name,
                GuiName = string.IsNullOrEmpty(field.guiName) ? field.name : field.guiName,
                PartTitle = PartTitle(part),
                Symmetry = control != null && (control.affectSymCounterparts & __instance.Scene) != 0,
                Value = newValue,
                ValueText = ValueCodec.Format(newValue),
                Host = module,
                LastChangeRealtime = Time.realtimeSinceStartup,
                DisplayValue = Display(field, control, newValue),
            };
            return !CommandHooks.TryQueue(part.vessel, command);
        }

        /// <summary>Postfix on <c>UIPartActionFloatRange.UpdateItem</c>: show the value on its way.</summary>
        public static void FloatRangeUpdatePostfix(UIPartActionFloatRange __instance)
        {
            Part part = __instance.Part;
            BaseField field = __instance.Field;
            if (field == null || !OnActiveVessel(part)) return;
            PartFieldCommand pending = LightLagVessel.For(part.vessel)?.Queue.PendingField(field.host, field.name);
            if (!(pending?.Value is float value)) return;

            Traverse item = Traverse.Create(__instance);
            item.Field("handlingChange").SetValue(true);
            try
            {
                item.Field("fieldValue").SetValue(value);
                item.Method("SetSliderValue", new[] { typeof(float) }).GetValue(value);
                item.Field("fieldAmount").Property("text").SetValue(KSPUtil.LocalizeNumber(value, field.guiFormat) + field.guiUnits);
            }
            finally
            {
                item.Field("handlingChange").SetValue(false);
            }
        }

        private static bool OnActiveVessel(Part part) =>
            HighLogic.LoadedSceneIsFlight && part != null && part.vessel != null && part.vessel == FlightGlobals.ActiveVessel;

        /// <summary>For <c>skip_delay</c>: true (and a message) when the command can't be sent.</summary>
        private static bool BlockedWithoutSignal(Vessel vessel, string label)
        {
            if (!DelayService.Applies(vessel) || !double.IsInfinity(DelayService.GetDelay(vessel))) return false;
            Messages.Post(Localizer.Format("#LightLag_Message_NotSent", label));
            return true;
        }

        private static void RunNow(BaseEvent evt, BaseEventDetails data, bool withData) =>
            PlayerInput.Bypass(() =>
            {
                if (withData) evt.Invoke(data);
                else evt.Invoke();
            });

        private static string PartTitle(Part part) => part.partInfo?.title ?? part.name;

        private static string Display(BaseField field, UI_Control control, object value)
        {
            try
            {
                switch (control)
                {
                    case UI_Toggle toggle when value is bool b:
                        return Localizer.Format(b ? toggle.enabledText : toggle.disabledText);
                    case UI_ChooseOption choose when choose.options != null && choose.display != null:
                        int i = Array.IndexOf(choose.options, value as string);
                        if (i >= 0 && i < choose.display.Length) return Localizer.Format(choose.display[i]);
                        break;
                    case UI_Cycle cycle when value is int index && cycle.stateNames != null && index >= 0 && index < cycle.stateNames.Length:
                        return Localizer.Format(cycle.stateNames[index]);
                }
                if (value is float f) return KSPUtil.LocalizeNumber(f, field.guiFormat) + field.guiUnits;
                if (value is double d) return KSPUtil.LocalizeNumber(d, field.guiFormat) + field.guiUnits;
            }
            catch (Exception) { }
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
