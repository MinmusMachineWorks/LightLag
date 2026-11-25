using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CommNet;
using KSP.UI.Screens;
using UnityEngine;

namespace LightLag.Tests
{
    /// <summary>
    /// Flight-scene tests, started from a toolbar button. Results go to KSP.log as
    /// <c>[LightLag Test] PASS|FAIL|SKIP name — detail</c>, ending with a SUMMARY line.
    /// Black-box: LightLag is reached by reflection, and its effect is read from
    /// <c>vessel.ctrlState</c>. Read tests/README.md for the test vessel.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FlightTests : MonoBehaviour
    {
        private const string Prefix = "[LightLag Test] ";
        private const double C = 299792458.0;

        private ApplicationLauncherButton _button;
        private bool _running;
        private int _pass, _fail, _skip;

        public void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            if (ApplicationLauncher.Ready) AddButton();
        }

        public void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            if (_button != null) ApplicationLauncher.Instance?.RemoveModApplication(_button);
        }

        private void AddButton()
        {
            if (_button != null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(
                Run, Run, null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, Texture2D.whiteTexture);
        }

        private void Run()
        {
            if (_running) return;
            StartCoroutine(RunAll());
        }

        private IEnumerator RunAll()
        {
            _running = true;
            _pass = _fail = _skip = 0;
            try
            {
                Assembly mod = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.name == "LightLag")?.assembly;
                Type service = mod?.GetType("LightLag.DelayService");
                Type paramsType = mod?.GetType("LightLag.LightLagParams");
                if (service == null || paramsType == null)
                {
                    Result("FAIL", "setup", "LightLag.DelayService or LightLagParams not found");
                    yield break;
                }
                var getDelay = (Func<Vessel, double>)Delegate.CreateDelegate(typeof(Func<Vessel, double>), service.GetMethod("GetDelay"));
                object settings = HighLogic.CurrentGame.Parameters.CustomParams(paramsType);
                FieldInfo multiplier = paramsType.GetField("lightSpeedMultiplier");
                FieldInfo roundTrip = paramsType.GetField("roundTrip");
                FieldInfo enabled = paramsType.GetField("delayEnabled");

                Vessel v = FlightGlobals.ActiveVessel;
                string skip = SkipReason(v, (bool)enabled.GetValue(settings));
                if (skip != null)
                {
                    foreach (string name in TestNames) Result("SKIP", name, skip);
                    yield break;
                }

                // 1. The delay LightLag reports matches the control path's length.
                double meters = v.connection.ControlPath.Sum(l => (l.a.position - l.b.position).magnitude);
                double expected = meters / (C * (float)multiplier.GetValue(settings)) * ((bool)roundTrip.GetValue(settings) ? 2.0 : 1.0);
                double actual = getDelay(v);
                bool match = Math.Abs(actual - expected) <= 1e-6 * Math.Max(1.0, expected);
                Result(match ? "PASS" : "FAIL", "delay matches the control path",
                    $"path {meters:0} m over {v.connection.ControlPath.Count} links, expected {expected:0.000000} s, got {actual:0.000000} s");

                float savedMultiplier = (float)multiplier.GetValue(settings);
                bool savedRoundTrip = (bool)roundTrip.GetValue(settings);
                float savedThrottle = FlightInputHandler.state.mainThrottle;
                try
                {
                    // Aim for about 2 s of delay, within the setting's range.
                    roundTrip.SetValue(settings, false);
                    float m = Mathf.Clamp((float)(meters / (C * 2.0)), 0.01f, 100f);
                    multiplier.SetValue(settings, m);
                    yield return new WaitForFixedUpdate();
                    double delay = getDelay(v);
                    if (delay < 0.5)
                    {
                        foreach (string name in TestNames.Skip(1))
                            Result("SKIP", name, $"delay only {delay:0.000} s even at multiplier {m}; move farther from the control source");
                        yield break;
                    }

                    yield return StartCoroutine(ThrottleTest(v, delay));

                    // 3. A direct SetGroup call (KSP or another mod, not the player) acts at once.
                    bool savedSas = v.ActionGroups[KSPActionGroup.SAS];
                    v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                    bool instant = v.ActionGroups[KSPActionGroup.SAS];
                    Result(instant ? "PASS" : "FAIL", "non-player commands are instant",
                        instant ? "SetGroup(SAS, true) took effect in the same frame" : "SetGroup(SAS, true) didn't take effect at once");

                    // 4 and 5. A SAS mode click on the navball arrives after the delay, and the queued command is saved.
                    if (instant)
                    {
                        yield return new WaitForFixedUpdate();
                        yield return StartCoroutine(SasModeTest(v, delay));
                    }
                    else
                    {
                        Result("SKIP", "SAS mode click arrives after the delay", "SAS couldn't be turned on");
                        Result("SKIP", "queued command is saved", "SAS couldn't be turned on");
                    }
                    v.ActionGroups.SetGroup(KSPActionGroup.SAS, savedSas);
                }
                finally
                {
                    FlightInputHandler.state.mainThrottle = savedThrottle;
                    multiplier.SetValue(settings, savedMultiplier);
                    roundTrip.SetValue(settings, savedRoundTrip);
                }
            }
            finally
            {
                Debug.Log($"{Prefix}SUMMARY pass={_pass} fail={_fail} skip={_skip}");
                _running = false;
            }
        }

        private static readonly string[] TestNames =
        {
            "delay matches the control path", "throttle arrives after the delay", "non-player commands are instant",
            "SAS mode click arrives after the delay", "queued command is saved",
        };

        private IEnumerator ThrottleTest(Vessel v, double delay)
        {
            if (v.parts.Any(p => p.FindModulesImplementing<ModuleEngines>().Any(e => e.EngineIgnited)))
            {
                Result("SKIP", "throttle arrives after the delay", "an engine is active; deactivate engines so the throttle test is safe");
                yield break;
            }
            // 2. A throttle change reaches ctrlState after the delay, not before.
            float target = FlightInputHandler.state.mainThrottle < 0.5f ? 0.75f : 0.25f;
            double sent = Planetarium.GetUniversalTime();
            FlightInputHandler.state.mainThrottle = target;
            double? arrived = null, early = null;
            while (Planetarium.GetUniversalTime() - sent < delay + 5.0)
            {
                yield return new WaitForFixedUpdate();
                double t = Planetarium.GetUniversalTime() - sent;
                if (Math.Abs(v.ctrlState.mainThrottle - target) < 1e-4f)
                {
                    if (t < delay - 3 * Time.fixedDeltaTime) early = t;
                    arrived = t;
                    break;
                }
            }
            double slack = 3 * Time.fixedDeltaTime;
            bool ok = arrived.HasValue && !early.HasValue && Math.Abs(arrived.Value - delay) <= slack;
            Result(ok ? "PASS" : "FAIL", "throttle arrives after the delay",
                arrived.HasValue ? $"delay {delay:0.000} s, arrived after {arrived.Value:0.000} s (slack {slack:0.000} s)"
                                 : $"delay {delay:0.000} s, not arrived after {delay + 5.0:0.0} s");
        }

        private IEnumerator SasModeTest(Vessel v, double delay)
        {
            const string name = "SAS mode click arrives after the delay";
            VesselAutopilotUI ui = FindObjectOfType<VesselAutopilotUI>();
            MethodInfo click = typeof(VesselAutopilotUI).GetMethod("OnClickButton", BindingFlags.Instance | BindingFlags.NonPublic);
            if (ui == null || click == null)
            {
                Result("SKIP", name, "navball SAS buttons not found");
                Result("SKIP", "queued command is saved", "navball SAS buttons not found");
                yield break;
            }

            VesselAutopilot.AutopilotMode before = v.Autopilot.Mode;
            VesselAutopilot.AutopilotMode target = before != VesselAutopilot.AutopilotMode.StabilityAssist
                ? VesselAutopilot.AutopilotMode.StabilityAssist
                : VesselAutopilot.AutopilotMode.Prograde;
            if (!v.Autopilot.CanSetMode(target))
            {
                Result("SKIP", name, $"SAS can't hold {target}; use a probe core with prograde hold");
                Result("SKIP", "queued command is saved", $"SAS can't hold {target}");
                yield break;
            }

            double sent = Planetarium.GetUniversalTime();
            click.Invoke(ui, new object[] { (int)target });

            // 5. While it's queued, the vessel module's save holds it.
            VesselModule module = v.vesselModules.FirstOrDefault(m => m.GetType().FullName == "LightLag.LightLagVessel");
            MethodInfo onSave = module?.GetType().GetMethod("OnSave", BindingFlags.Instance | BindingFlags.NonPublic);
            if (onSave == null)
            {
                Result("FAIL", "queued command is saved", "LightLagVessel.OnSave not found");
            }
            else
            {
                var node = new ConfigNode("MODULE");
                onSave.Invoke(module, new object[] { node });
                ConfigNode saved = node.GetNodes("COMMAND").LastOrDefault();
                bool ok = saved != null && saved.GetValue("type") == "SasMode" && saved.GetValue("mode") == target.ToString();
                Result(ok ? "PASS" : "FAIL", "queued command is saved",
                    ok ? $"COMMAND type=SasMode mode={target}" : $"saved node: {node.ToString().Replace("\n", " ")}");
            }

            double? arrived = null, early = null;
            while (Planetarium.GetUniversalTime() - sent < delay + 5.0)
            {
                yield return new WaitForFixedUpdate();
                double t = Planetarium.GetUniversalTime() - sent;
                if (v.Autopilot.Mode == target)
                {
                    if (t < delay - 3 * Time.fixedDeltaTime) early = t;
                    arrived = t;
                    break;
                }
            }
            double slack = 3 * Time.fixedDeltaTime;
            bool pass = arrived.HasValue && !early.HasValue && Math.Abs(arrived.Value - delay) <= slack;
            Result(pass ? "PASS" : "FAIL", name,
                arrived.HasValue ? $"{before} → {target}: delay {delay:0.000} s, arrived after {arrived.Value:0.000} s (slack {slack:0.000} s)"
                                 : $"{before} → {target}: delay {delay:0.000} s, not arrived after {delay + 5.0:0.0} s");
            if (arrived.HasValue && v.Autopilot.CanSetMode(before)) v.Autopilot.SetMode(before);
        }

        private static string SkipReason(Vessel v, bool delayEnabled)
        {
            if (v == null) return "no active vessel";
            if (!delayEnabled) return "light-speed delay is off in the settings";
            if (!CommNetScenario.CommNetEnabled) return "CommNet is off";
            if (v.isEVA || v.connection == null || (v.connection.ControlState & VesselControlState.Kerbal) != 0)
                return "the vessel has local control; use an uncrewed probe";
            if (!v.connection.IsConnected) return "the vessel has no connection";
            return null;
        }

        private void Result(string status, string name, string detail)
        {
            if (status == "PASS") _pass++; else if (status == "FAIL") _fail++; else _skip++;
            Debug.Log($"{Prefix}{status} {name} — {detail}");
        }
    }
}
