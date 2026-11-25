using System;
using System.Linq;
using UnityEngine;

namespace LightLag.Tests
{
    /// <summary>
    /// Runs once in the Space Center scene and checks that LightLag loaded. Each result goes to
    /// KSP.log as <c>[LightLag Test] PASS|FAIL name — detail</c>, ending with a SUMMARY line.
    /// Black-box: it finds the mod through KSP's loaded assemblies, not by reference.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, once: true)]
    public class SmokeTests : MonoBehaviour
    {
        private const string Prefix = "[LightLag Test] ";
        private int _pass, _fail;

        public void Start()
        {
            AssemblyLoader.LoadedAssembly mod = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.name == "LightLag");
            Check("assembly loaded", mod != null, mod?.assembly.GetName().Version.ToString() ?? "LightLag.dll not loaded");

            Type paramsType = mod?.assembly.GetType("LightLag.LightLagParams");
            Check("settings type present", paramsType != null, paramsType?.FullName ?? "LightLag.LightLagParams missing");

            bool registered = paramsType != null && HighLogic.CurrentGame?.Parameters.CustomParams(paramsType) != null;
            Check("settings registered in the current game", registered, registered ? "found" : "CustomParams returned null");

            Debug.Log($"{Prefix}SUMMARY pass={_pass} fail={_fail}");
        }

        private void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            Debug.Log($"{Prefix}{(ok ? "PASS" : "FAIL")} {name} — {detail}");
        }
    }
}
