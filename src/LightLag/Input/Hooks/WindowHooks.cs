using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// Prefixes on the methods <see cref="Windows"/> lists as commands. A call made inside a
    /// window's entry method (<see cref="PlayerInput.Active"/>) to a vessel with delay is queued
    /// as a <see cref="MethodCommand"/> and the call is skipped; a bool method reports true. Calls
    /// from anywhere else (the module itself, KSP, other mods) run at once.
    ///
    /// Two rules keep held buttons and drags from flooding the queue: a call with arguments
    /// identical to the latest one still on its way to the same target is dropped (KAS's
    /// hold-to-run buttons repeat their call every frame until the motor starts), and a
    /// <c>slider</c> call within a second of the last replaces it.
    /// </summary>
    internal static class WindowHooks
    {
        private const float MergeSeconds = 1f;

        public static bool VoidPrefix(object __instance, object[] __args, MethodBase __originalMethod)
            => !Intercept(__instance, __args, __originalMethod);

        public static bool BoolPrefix(object __instance, object[] __args, MethodBase __originalMethod, ref bool __result)
        {
            if (!Intercept(__instance, __args, __originalMethod)) return true;
            __result = true;
            return false;
        }

        /// <summary>True when the call was queued, blocked or dropped as a repeat.</summary>
        private static bool Intercept(object instance, object[] args, MethodBase method)
        {
            if (!PlayerInput.Active) return false;
            WindowCommandSpec spec = Windows.For(method);
            if (spec == null) return false;

            if (!FindHost(instance, spec.HostMember, out Part part, out Vessel vessel))
            {
                Log.Warn($"window '{spec.Window}': no vessel for {Windows.Describe(method)}; running it now");
                return false;
            }
            if (!DelayService.Applies(vessel)) return false;
            LightLagVessel lightLag = LightLagVessel.For(vessel);
            if (lightLag == null) return false;

            object[] copy = args != null ? (object[])args.Clone() : new object[0];
            MethodCommand latest = lightLag.Queue.Commands.OfType<MethodCommand>()
                .LastOrDefault(c => !c.CancelPending && c.SameTarget(method, instance));
            if (latest != null && copy.Length > 0 && latest.SameArgs(copy)) return true;
            if (spec.Slider && latest != null && Time.realtimeSinceStartup - latest.QueuedRealtime < MergeSeconds)
                lightLag.Queue.Remove(latest);

            MethodCommand command = MethodCommand.Create(spec, method, instance, copy, part);
            return CommandHooks.TryQueue(vessel, command);
        }

        private static bool FindHost(object instance, string member, out Part part, out Vessel vessel)
        {
            part = null;
            vessel = null;
            object host = instance;
            if (member != null && instance != null)
            {
                Traverse t = Traverse.Create(instance);
                host = t.Field(member).FieldExists() ? t.Field(member).GetValue() : t.Property(member).GetValue();
            }
            switch (host)
            {
                case PartModule m when m != null: part = m.part; break;
                case Part p when p != null: part = p; break;
                case Vessel v when v != null: vessel = v; break;
            }
            if (part != null) vessel = part.vessel;
            if (vessel == null && instance == null) vessel = FlightGlobals.ActiveVessel;
            return vessel != null;
        }
    }
}
