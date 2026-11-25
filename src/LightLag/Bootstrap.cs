using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// One-time bootstrap. Applies each Harmony patch on its own, so that one failing patch
    /// (for example after a KSP or mod update changed a signature) can't stop the others.
    /// No patches are registered yet.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, once: true)]
    public class Bootstrap : MonoBehaviour
    {
        internal const string HarmonyId = "com.minmusmachineworks.lightlag";

        public void Start()
        {
            var harmony = new Harmony(HarmonyId);
            Log.Info($"loaded {Version}, Harmony id {harmony.Id}");
        }

        internal static string Version =>
            typeof(Bootstrap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Bootstrap).Assembly.GetName().Version.ToString();

        /// <summary>Patches <paramref name="target"/> ("Namespace.Type:Method"); logs and returns false on failure.</summary>
        internal static bool Patch(Harmony harmony, string target, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            try
            {
                MethodInfo original = AccessTools.Method(target);
                if (original == null)
                {
                    Log.Error($"patch target not found: {target}");
                    return false;
                }
                harmony.Patch(original, prefix, postfix);
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
