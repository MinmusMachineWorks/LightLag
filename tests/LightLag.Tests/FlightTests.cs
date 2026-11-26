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
                double expected = meters / (C * (float)multiplier.GetValue(settings));
                double actual = getDelay(v);
                bool match = Math.Abs(actual - expected) <= 1e-6 * Math.Max(1.0, expected);
                Result(match ? "PASS" : "FAIL", "delay matches the control path",
                    $"path {meters:0} m over {v.connection.ControlPath.Count} links, expected {expected:0.000000} s, got {actual:0.000000} s");

                // 2. The next-contact model sees the same network CommNet does right now.
                ModelTest(mod, v);

                float savedMultiplier = (float)multiplier.GetValue(settings);
                bool savedEnabled = (bool)enabled.GetValue(settings);
                float savedThrottle = FlightInputHandler.state.mainThrottle;
                try
                {
                    // Aim for about 2 s of delay, within the setting's range.
                    float m = Mathf.Clamp((float)(meters / (C * 2.0)), 0.01f, 100f);
                    multiplier.SetValue(settings, m);
                    yield return new WaitForFixedUpdate();
                    double delay = getDelay(v);
                    if (delay < 0.5)
                    {
                        foreach (string name in TestNames.Skip(2))
                            Result("SKIP", name, $"delay only {delay:0.000} s even at multiplier {m}; move farther from the control source");
                        yield break;
                    }

                    // 2. When delay starts, unchanged input isn't counted as in flight.
                    enabled.SetValue(settings, false);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    enabled.SetValue(settings, true);
                    for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                    int? pending = PendingInputs(v);
                    Result(pending == 0 ? "PASS" : "FAIL", "no phantom input when delay starts",
                        pending.HasValue ? $"{pending} changes in flight 3 frames after delay restarted, with the controls untouched"
                                         : "LightLagVessel.Input.PendingCount not found");

                    // 3. A throttle change reaches ctrlState after the delay, not before.
                    yield return StartCoroutine(ThrottleTest(v, delay));

                    // 4. A direct SetGroup call (KSP or another mod, not the player) acts at once.
                    bool savedSas = v.ActionGroups[KSPActionGroup.SAS];
                    v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                    bool instant = v.ActionGroups[KSPActionGroup.SAS];
                    Result(instant ? "PASS" : "FAIL", "non-player commands are instant",
                        instant ? "SetGroup(SAS, true) took effect in the same frame" : "SetGroup(SAS, true) didn't take effect at once");

                    // 5 and 6. A SAS mode click on the navball arrives after the delay, and the queued command is saved.
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

                    // 7. A part-menu button (Extend) arrives after the delay.
                    yield return StartCoroutine(PartEventTest(v, mod, delay));

                    // 8. Discard in the science results dialog arrives after the delay.
                    yield return StartCoroutine(ScienceDialogTest(v, delay));

                    // 9. A window button (robotics controller Play) arrives after the delay.
                    yield return StartCoroutine(WindowButtonTest(v, mod, delay));
                }
                finally
                {
                    FlightInputHandler.state.mainThrottle = savedThrottle;
                    multiplier.SetValue(settings, savedMultiplier);
                    enabled.SetValue(settings, savedEnabled);
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
            "delay matches the control path", "next-contact model agrees with CommNet", "no phantom input when delay starts", "throttle arrives after the delay",
            "non-player commands are instant",
            "SAS mode click arrives after the delay", "queued command is saved", "part-menu button arrives after the delay",
            "science dialog Discard arrives after the delay",
            "window button arrives after the delay",
        };

        /// <summary>
        /// LightLag's next-contact model, run at the current time, must see a path from the
        /// connected vessel and put every node within 1 km of CommNet's own position for it.
        /// </summary>
        private void ModelTest(Assembly mod, Vessel v)
        {
            const string name = "next-contact model agrees with CommNet";
            MethodInfo check = mod.GetType("LightLag.ContactPredictor")?.GetMethod("ModelCheck", BindingFlags.NonPublic | BindingFlags.Static);
            if (check == null) { Result("FAIL", name, "ContactPredictor.ModelCheck not found"); return; }
            object[] args = { v, false };
            double error = (double)check.Invoke(null, args);
            bool connected = (bool)args[1];
            if (double.IsNaN(error)) { Result("SKIP", name, $"no model for {CommNetNetwork.Instance?.CommNet?.GetType().FullName}"); return; }
            bool ok = connected && error < 1000.0;
            Result(ok ? "PASS" : "FAIL", name, $"model {(connected ? "sees" : "doesn't see")} a path; worst node position error {error:0} m");
        }

        private IEnumerator PartEventTest(Vessel v, Assembly mod, double delay)
        {
            const string name = "part-menu button arrives after the delay";
            ModuleDeployablePart deployable = v.parts.SelectMany(p => p.FindModulesImplementing<ModuleDeployablePart>())
                .FirstOrDefault(d => d.deployState == ModuleDeployablePart.DeployState.RETRACTED && d.Events["Extend"] != null && d.Events["Extend"].active);
            Type playerInput = mod.GetType("LightLag.PlayerInput");
            if (deployable == null || playerInput == null)
            {
                Result("SKIP", name, deployable == null ? "no retracted solar panel or antenna with Extend" : "LightLag.PlayerInput not found");
                yield break;
            }

            // Send Extend the way a part-menu click does: inside player context.
            BaseEvent extend = deployable.Events["Extend"];
            double sent = Planetarium.GetUniversalTime();
            playerInput.GetMethod("Enter").Invoke(null, null);
            try { extend.Invoke(); }
            finally { playerInput.GetMethod("Exit").Invoke(null, new object[] { null }); }

            double? arrived = null, early = null;
            while (Planetarium.GetUniversalTime() - sent < delay + 5.0)
            {
                yield return new WaitForFixedUpdate();
                double t = Planetarium.GetUniversalTime() - sent;
                if (deployable.deployState != ModuleDeployablePart.DeployState.RETRACTED)
                {
                    if (t < delay - 3 * Time.fixedDeltaTime) early = t;
                    arrived = t;
                    break;
                }
            }
            double slack = 3 * Time.fixedDeltaTime;
            bool pass = arrived.HasValue && !early.HasValue && Math.Abs(arrived.Value - delay) <= slack;
            Result(pass ? "PASS" : "FAIL", name,
                arrived.HasValue ? $"{deployable.part.partInfo.title} Extend: delay {delay:0.000} s, arrived after {arrived.Value:0.000} s (slack {slack:0.000} s)"
                                 : $"{deployable.part.partInfo.title} Extend: delay {delay:0.000} s, not arrived after {delay + 5.0:0.0} s");

            // Put it back, outside player context, so it acts at once.
            if (arrived.HasValue)
            {
                while (deployable.deployState == ModuleDeployablePart.DeployState.EXTENDING) yield return new WaitForFixedUpdate();
                BaseEvent retract = deployable.Events["Retract"];
                if (deployable.retractable && retract != null && retract.active) retract.Invoke();
            }
        }

        private IEnumerator ScienceDialogTest(Vessel v, double delay)
        {
            const string name = "science dialog Discard arrives after the delay";
            // A rerunnable experiment with no data, run outside player context (so at once).
            ModuleScienceExperiment experiment = null;
            foreach (ModuleScienceExperiment e in v.parts.SelectMany(p => p.FindModulesImplementing<ModuleScienceExperiment>())
                         .Where(e => e.rerunnable && !e.Inoperable && !e.Deployed && e.GetData().Length == 0))
            {
                e.DeployExperiment();
                for (int i = 0; i < 250 && e.GetData().Length == 0; i++) yield return new WaitForFixedUpdate();
                if (e.GetData().Length > 0) { experiment = e; break; }
            }
            if (experiment == null)
            {
                Result("SKIP", name, "no rerunnable experiment that can run here");
                yield break;
            }

            // Its results dialog page opens on its own; press Discard the way the button does.
            var dialog = KSP.UI.Screens.Flight.Dialogs.ExperimentsResultDialog.Instance;
            for (int i = 0; i < 50 && (dialog == null || dialog.currentPage == null); i++)
            {
                yield return null;
                dialog = KSP.UI.Screens.Flight.Dialogs.ExperimentsResultDialog.Instance;
            }
            ExperimentResultDialogPage page = dialog?.pages.FirstOrDefault(p => p.host == experiment.part);
            if (page == null)
            {
                Result("SKIP", name, $"{experiment.experimentID} ran, but its results dialog didn't open");
                yield break;
            }
            double sent = Planetarium.GetUniversalTime();
            page.OnDiscardData(page.pageData);

            double? gone = null, early = null;
            while (Planetarium.GetUniversalTime() - sent < delay + 5.0)
            {
                yield return new WaitForFixedUpdate();
                double t = Planetarium.GetUniversalTime() - sent;
                if (experiment.GetData().Length == 0)
                {
                    if (t < delay - 3 * Time.fixedDeltaTime) early = t;
                    gone = t;
                    break;
                }
            }
            double slack = 3 * Time.fixedDeltaTime;
            bool pass = gone.HasValue && !early.HasValue && Math.Abs(gone.Value - delay) <= slack;
            Result(pass ? "PASS" : "FAIL", name,
                gone.HasValue ? $"{experiment.experimentID} Discard: delay {delay:0.000} s, data gone after {gone.Value:0.000} s (slack {slack:0.000} s)"
                              : $"{experiment.experimentID} Discard: delay {delay:0.000} s, data still on board after {delay + 5.0:0.0} s");
        }

        private IEnumerator WindowButtonTest(Vessel v, Assembly mod, double delay)
        {
            const string name = "window button arrives after the delay";
            PartModule controller = v.parts.SelectMany(p => p.Modules.Cast<PartModule>())
                .FirstOrDefault(m => m.GetType().FullName == "Expansions.Serenity.ModuleRoboticController");
            Type playerInput = mod.GetType("LightLag.PlayerInput");
            if (controller == null || playerInput == null)
            {
                Result("SKIP", name, controller == null ? "no robotics controller (Breaking Ground) on the vessel" : "LightLag.PlayerInput not found");
                yield break;
            }
            MethodInfo toggle = controller.GetType().GetMethod("TogglePlay", Type.EmptyTypes);
            PropertyInfo playing = controller.GetType().GetProperty("SequenceIsPlaying");
            bool before = (bool)playing.GetValue(controller);

            // Press Play/pause the way the window's button does: inside player context.
            double sent = Planetarium.GetUniversalTime();
            playerInput.GetMethod("Enter").Invoke(null, null);
            try { toggle.Invoke(controller, null); }
            finally { playerInput.GetMethod("Exit").Invoke(null, new object[] { null }); }

            double? arrived = null, early = null;
            while (Planetarium.GetUniversalTime() - sent < delay + 5.0)
            {
                yield return new WaitForFixedUpdate();
                double t = Planetarium.GetUniversalTime() - sent;
                if ((bool)playing.GetValue(controller) != before)
                {
                    if (t < delay - 3 * Time.fixedDeltaTime) early = t;
                    arrived = t;
                    break;
                }
            }
            double slack = 3 * Time.fixedDeltaTime;
            bool pass = arrived.HasValue && !early.HasValue && Math.Abs(arrived.Value - delay) <= slack;
            Result(pass ? "PASS" : "FAIL", name,
                arrived.HasValue ? $"robotics Play/pause: delay {delay:0.000} s, arrived after {arrived.Value:0.000} s (slack {slack:0.000} s)"
                                 : $"robotics Play/pause: delay {delay:0.000} s, not arrived after {delay + 5.0:0.0} s (the window patches may not be applied; check the log for 'window')");

            // Put it back, outside player context, so it acts at once.
            if (arrived.HasValue) toggle.Invoke(controller, null);
        }

        private static int? PendingInputs(Vessel v)
        {
            VesselModule module = v.vesselModules.FirstOrDefault(m => m.GetType().FullName == "LightLag.LightLagVessel");
            object input = module?.GetType().GetProperty("Input")?.GetValue(module);
            return (int?)input?.GetType().GetProperty("PendingCount")?.GetValue(input);
        }

        private IEnumerator ThrottleTest(Vessel v, double delay)
        {
            if (v.parts.Any(p => p.FindModulesImplementing<ModuleEngines>().Any(e => e.EngineIgnited)))
            {
                Result("SKIP", "throttle arrives after the delay", "an engine is active; deactivate engines so the throttle test is safe");
                yield break;
            }
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

            // 6. While it's queued, the vessel module's save holds it.
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
