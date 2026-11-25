using System;
using System.Globalization;

namespace LightLag
{
    /// <summary>
    /// Per-vessel LightLag state, on loaded vessels in flight. A plain VesselModule, not a
    /// CommNetVessel subclass, so it works whichever CommNet implementation is installed.
    /// <list type="bullet">
    /// <item>Keeps <see cref="FlyByWireDelay.OnPreAutopilot"/> first in the vessel's
    /// <c>OnPreAutopilotUpdate</c> callbacks, so other mods' callbacks see delayed input.</item>
    /// <item>Holds the vessel's <see cref="CommandQueue"/> and extra delay, saved with the vessel
    /// (<c>VESSELMODULES</c> in the save), and runs due commands each physics frame while the
    /// vessel is off rails.</item>
    /// <item>Drops time warp before a queued command runs.</item>
    /// </list>
    /// Commands queued for a vessel that is then unloaded wait in the save; any that came due
    /// meanwhile run, in order, when it's loaded again.
    /// </summary>
    public class LightLagVessel : VesselModule
    {
        private FlyByWireDelay _input;
        private FlightInputCallback _callback;
        private bool _checkedOverdue;

        public FlyByWireDelay Input => _input;

        /// <summary>Discrete commands on their way to this vessel, or held on board.</summary>
        public CommandQueue Queue { get; } = new CommandQueue();

        /// <summary>
        /// Seconds after sending before a discrete command runs, at the least. The vessel holds
        /// a command that arrives sooner. Applies to commands sent from now on.
        /// </summary>
        public double ExtraDelay;

        public static LightLagVessel For(Vessel vessel) => vessel?.FindVesselModuleImplementing<LightLagVessel>();

        public override Activation GetActivation() => Activation.FlightScene | Activation.LoadedVessels;

        public override bool ShouldBeActive() => vessel != null && vessel.loaded;

        protected override void OnStart()
        {
            _input = new FlyByWireDelay(vessel);
            _callback = _input.OnPreAutopilot;
            EnsureFirst();
        }

        public void FixedUpdate()
        {
            EnsureFirst();
            if (vessel == null || vessel.packed || Queue.Count == 0) return;

            double now = Planetarium.GetUniversalTime();
            if (!_checkedOverdue)
            {
                _checkedOverdue = true;
                int overdue = 0;
                foreach (QueuedCommand c in Queue.Commands) if (c.ExecuteUT < now - 1.0) overdue++;
                if (overdue > 0)
                    Messages.Post(KSP.Localization.Localizer.Format("#LightLag_Message_Overdue", overdue, vessel.vesselName));
            }
            Queue.Process(vessel, now);
        }

        public void Update()
        {
            if (vessel != null && Queue.Count > 0) WarpGuard.Check(Queue, Planetarium.GetUniversalTime());
        }

        public void OnDisable() => Detach();

        public void OnDestroy() => Detach();

        protected override void OnSave(ConfigNode node)
        {
            node.AddValue("extraDelay", ExtraDelay.ToString("R", CultureInfo.InvariantCulture));
            Queue.Save(node);
        }

        protected override void OnLoad(ConfigNode node)
        {
            string extra = node.GetValue("extraDelay");
            if (extra != null && double.TryParse(extra, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                ExtraDelay = Math.Max(0.0, parsed);
            Queue.Load(node, vessel != null ? vessel.vesselName : "a vessel");
            _checkedOverdue = false;
        }

        private void EnsureFirst()
        {
            if (vessel == null || _callback == null) return;
            Delegate[] callbacks = vessel.OnPreAutopilotUpdate?.GetInvocationList();
            if (callbacks != null && callbacks.Length > 0 && (FlightInputCallback)callbacks[0] == _callback) return;
            vessel.OnPreAutopilotUpdate -= _callback;
            vessel.OnPreAutopilotUpdate = _callback + vessel.OnPreAutopilotUpdate;
        }

        private void Detach()
        {
            if (vessel != null && _callback != null) vessel.OnPreAutopilotUpdate -= _callback;
        }
    }
}
