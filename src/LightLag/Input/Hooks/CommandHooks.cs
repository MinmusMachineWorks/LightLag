using KSP.UI.Screens;
using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// Harmony prefixes that turn the player's discrete commands into <see cref="QueuedCommand"/>s:
    /// <list type="bullet">
    /// <item><c>KSP.UI.Screens.StageManager.ActivateNextStage</c>: staging.</item>
    /// <item><c>ActionGroupList.ToggleGroup</c> and <c>SetGroup</c>: action groups, SAS, RCS, gear, lights, brakes, abort.</item>
    /// <item><c>VesselAutopilot.SetMode</c>: SAS modes.</item>
    /// </list>
    /// A call is intercepted only when it comes from the player (<see cref="PlayerInput.Active"/>)
    /// and delay applies to the vessel. Then:
    /// <list type="bullet">
    /// <item>no signal, with stock's "Require Signal for Control" on: the command isn't sent, and
    /// a message says so (with it off, delay doesn't apply and the call runs at once);</item>
    /// <item>a delay under half a physics frame, with no extra delay set: the call runs at once;</item>
    /// <item>otherwise the command goes into the vessel's queue and the original call is skipped.</item>
    /// </list>
    /// Calls from KSP itself and from other mods are deliberately left undelayed: they run on
    /// board (SAS turning itself off, MechJeb staging) and the bridge covers MechJeb's commands (M4).
    /// </summary>
    internal static class CommandHooks
    {
        /// <summary>Prefix on <c>StageManager.ActivateNextStage</c>.</summary>
        public static bool ActivateNextStagePrefix()
        {
            if (!PlayerInput.Active) return true;
            Vessel vessel = FlightGlobals.ActiveVessel;
            return !TryQueue(vessel, new StageCommand { QueuedFrame = Time.frameCount });
        }

        /// <summary>Prefix on <c>ActionGroupList.ToggleGroup</c>.</summary>
        public static bool ToggleGroupPrefix(ActionGroupList __instance, KSPActionGroup group)
        {
            if (!PlayerInput.Active) return true;
            Vessel vessel = __instance.v;

            // The stage key stages and then toggles the Stage group in the same frame;
            // when the staging was queued, the toggle rides along with it.
            if (group == KSPActionGroup.Stage && vessel != null)
            {
                LightLagVessel module = LightLagVessel.For(vessel);
                if (module?.Queue.Last is StageCommand stage && stage.QueuedFrame == Time.frameCount)
                {
                    stage.ToggleStageGroup = true;
                    return false;
                }
            }
            return !TryQueue(vessel, new ActionGroupCommand { Group = group, Toggle = true });
        }

        /// <summary>Prefix on <c>ActionGroupList.SetGroup</c>.</summary>
        public static bool SetGroupPrefix(ActionGroupList __instance, KSPActionGroup group, bool active)
        {
            if (!PlayerInput.Active) return true;
            return !TryQueue(__instance.v, new ActionGroupCommand { Group = group, Toggle = false, State = active });
        }

        /// <summary>
        /// Prefix on <c>VesselAutopilot.SetMode</c>. A queued mode returns false, so the navball
        /// buttons keep showing the vessel's actual mode until the command arrives.
        /// </summary>
        public static bool SetModePrefix(VesselAutopilot __instance, VesselAutopilot.AutopilotMode mode, ref bool __result)
        {
            if (!PlayerInput.Active) return true;
            if (!TryQueue(__instance.Vessel, new SasModeCommand { Mode = mode })) return true;
            __result = false;
            return false;
        }

        /// <summary>
        /// Queues <paramref name="command"/> for <paramref name="vessel"/>, or blocks it without a
        /// signal. Returns false when the original call should run now.
        /// </summary>
        internal static bool TryQueue(Vessel vessel, QueuedCommand command)
        {
            if (vessel == null || !DelayService.Applies(vessel)) return false;
            LightLagVessel module = LightLagVessel.For(vessel);
            if (module == null) return false;

            double delay = DelayService.GetDelay(vessel);
            if (double.IsInfinity(delay))
            {
                Messages.Post(KSP.Localization.Localizer.Format("#LightLag_Message_NotSent", command.Label));
                return true;
            }
            if (delay <= 0.5 * Time.fixedDeltaTime && module.ExtraDelay <= 0.0) return false;
            Add(vessel, module, command, delay);
            return true;
        }

        /// <summary>
        /// Queues <paramref name="command"/> whatever the delay, for <see cref="LightLagAPI.QueueCommand"/>.
        /// False, with nothing queued, when delay doesn't apply or there's no signal.
        /// </summary>
        internal static bool Queue(Vessel vessel, QueuedCommand command)
        {
            if (vessel == null || !DelayService.Applies(vessel)) return false;
            LightLagVessel module = LightLagVessel.For(vessel);
            double delay = DelayService.GetDelay(vessel);
            if (module == null || double.IsInfinity(delay)) return false;
            Add(vessel, module, command, delay);
            return true;
        }

        private static void Add(Vessel vessel, LightLagVessel module, QueuedCommand command, double delay)
        {
            double now = Planetarium.GetUniversalTime();
            command.SentUT = now;
            command.ArrivalUT = now + delay;
            command.ExecuteUT = System.Math.Max(command.ArrivalUT, now + module.ExtraDelay);
            module.Queue.Add(command);
            Log.Info($"queued {command.TypeName} '{command.Label}' for {vessel.vesselName}: arrives in {delay:0.###} s, runs in {command.ExecuteUT - now:0.###} s");
        }
    }
}
