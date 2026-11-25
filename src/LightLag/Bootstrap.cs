using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// One-time bootstrap. Applies each Harmony patch on its own, so that one failing patch
    /// (for example after a KSP or mod update changed a signature) can't stop the others.
    /// The patches:
    /// <list type="bullet">
    /// <item>Player entry points, bracketed by <see cref="PlayerInput"/> (prefix and finalizer):
    /// <c>FlightInputHandler.Update</c>, <c>ActionGroupToggleButton.SetToggle</c>, <c>VesselAutopilotUI.OnClickButton</c>.</item>
    /// <item>Command prefixes in <see cref="CommandHooks"/>: <c>StageManager.ActivateNextStage</c>,
    /// <c>ActionGroupList.ToggleGroup</c> and <c>SetGroup</c>, <c>VesselAutopilot.SetMode</c>.</item>
    /// </list>
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, once: true)]
    public class Bootstrap : MonoBehaviour
    {
        internal const string HarmonyId = "com.minmusmachineworks.lightlag";

        public void Start()
        {
            var harmony = new Harmony(HarmonyId);
            Log.Info($"loaded {Version}, Harmony id {harmony.Id}");

            var enter = new HarmonyMethod(typeof(PlayerInput), nameof(PlayerInput.Enter));
            var exit = new HarmonyMethod(typeof(PlayerInput), nameof(PlayerInput.Exit));
            int ok = 0, total = 0;
            void Count(bool result) { total++; if (result) ok++; }

            Count(Patch(harmony, "FlightInputHandler:Update", prefix: enter, finalizer: exit));
            Count(Patch(harmony, "KSP.UI.Screens.Flight.ActionGroupToggleButton:SetToggle", prefix: enter, finalizer: exit));
            Count(Patch(harmony, "VesselAutopilotUI:OnClickButton", prefix: enter, finalizer: exit));

            Count(Patch(harmony, "KSP.UI.Screens.StageManager:ActivateNextStage",
                prefix: new HarmonyMethod(typeof(CommandHooks), nameof(CommandHooks.ActivateNextStagePrefix))));
            Count(Patch(harmony, "ActionGroupList:ToggleGroup",
                prefix: new HarmonyMethod(typeof(CommandHooks), nameof(CommandHooks.ToggleGroupPrefix))));
            Count(Patch(harmony, "ActionGroupList:SetGroup",
                prefix: new HarmonyMethod(typeof(CommandHooks), nameof(CommandHooks.SetGroupPrefix))));
            Count(Patch(harmony, "VesselAutopilot:SetMode",
                prefix: new HarmonyMethod(typeof(CommandHooks), nameof(CommandHooks.SetModePrefix))));

            Log.Info($"applied {ok} of {total} patches");
        }

        internal static string Version =>
            typeof(Bootstrap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Bootstrap).Assembly.GetName().Version.ToString();

        /// <summary>Patches <paramref name="target"/> ("Namespace.Type:Method"); logs and returns false on failure.</summary>
        internal static bool Patch(Harmony harmony, string target, HarmonyMethod prefix = null, HarmonyMethod postfix = null,
            HarmonyMethod finalizer = null)
        {
            try
            {
                MethodInfo original = AccessTools.Method(target);
                if (original == null)
                {
                    Log.Error($"patch target not found: {target}");
                    return false;
                }
                harmony.Patch(original, prefix, postfix, finalizer: finalizer);
                Log.Info($"patched {target}");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"patching {target} failed: {e}");
                return false;
            }
        }
    }
}
