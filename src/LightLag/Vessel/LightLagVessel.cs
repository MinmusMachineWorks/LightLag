using System;

namespace LightLag
{
    /// <summary>
    /// Per-vessel LightLag state, on loaded vessels in flight. A plain VesselModule, not a
    /// CommNetVessel subclass, so it works whichever CommNet implementation is installed.
    /// It keeps <see cref="FlyByWireDelay.OnPreAutopilot"/> first in the vessel's
    /// <c>OnPreAutopilotUpdate</c> callbacks, so other mods' callbacks see delayed input.
    /// </summary>
    public class LightLagVessel : VesselModule
    {
        private FlyByWireDelay _input;
        private FlightInputCallback _callback;

        public FlyByWireDelay Input => _input;

        public override Activation GetActivation() => Activation.FlightScene | Activation.LoadedVessels;

        public override bool ShouldBeActive() => vessel != null && vessel.loaded;

        protected override void OnStart()
        {
            _input = new FlyByWireDelay(vessel);
            _callback = _input.OnPreAutopilot;
            EnsureFirst();
        }

        public void FixedUpdate() => EnsureFirst();

        public void OnDisable() => Detach();

        public void OnDestroy() => Detach();

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
