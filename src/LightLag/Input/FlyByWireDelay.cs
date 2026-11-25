using System.Collections.Generic;
using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// Delays the player's stick, translation, throttle, wheel and trim input to one vessel.
    ///
    /// KSP copies the player's input into <c>vessel.ctrlState</c> and then calls
    /// <c>OnPreAutopilotUpdate</c>, <c>OnAutopilotUpdate</c> (stock SAS),
    /// <c>OnPostAutopilotUpdate</c> and <c>OnFlyByWire</c> (MechJeb and other autopilots), in
    /// that order (<c>Vessel.FeedInputFeed</c>). <see cref="OnPreAutopilot"/> runs first in
    /// <c>OnPreAutopilotUpdate</c>: it records the player's input with an arrival time and
    /// replaces it with the input that has arrived by now. SAS and autopilots run after it, so
    /// their own corrections act on board without delay.
    ///
    /// Only changes are queued: a state equal to the last one queued is skipped. The queue isn't
    /// saved; input still in flight is lost on a scene change. When delay stops applying (local
    /// control, delay turned off, or no signal with stock's "Require Signal for Control" off),
    /// input still in flight is dropped and the player's input acts at once.
    /// </summary>
    public class FlyByWireDelay
    {
        private struct Pending
        {
            internal double ArrivalUT;
            internal FlightCtrlState State;
        }

        private readonly Vessel _vessel;
        private readonly Queue<Pending> _queue = new Queue<Pending>();
        private FlightCtrlState _lastQueued;
        private FlightCtrlState _delivered;
        private bool _wasDelayed;

        public FlyByWireDelay(Vessel vessel)
        {
            _vessel = vessel;
        }

        /// <summary>Inputs sent but not yet arrived.</summary>
        public int PendingCount => _queue.Count;

        /// <summary>The callback for <c>vessel.OnPreAutopilotUpdate</c>.</summary>
        public void OnPreAutopilot(FlightCtrlState state)
        {
            if (!DelayService.Applies(_vessel))
            {
                if (_wasDelayed) Log.Info($"{_vessel.vesselName}: delay off (local control, no signal required, or disabled); {_queue.Count} pending inputs dropped");
                _queue.Clear();
                _lastQueued = null;
                _delivered = null;
                _wasDelayed = false;
                return;
            }

            double now = Planetarium.GetUniversalTime();
            double delay = DelayService.GetDelay(_vessel);
            bool connected = !double.IsInfinity(delay);

            if (!_wasDelayed)
            {
                // Start from what the vessel is doing now, so it doesn't jump when delay begins.
                _delivered = Copy(state);
                _wasDelayed = true;
                Log.Info($"{_vessel.vesselName}: delay on, {DelayService.Format(delay)}");
            }

            // Only the active vessel carries the player's input.
            if (connected && _vessel == FlightGlobals.ActiveVessel && !SameInput(state, _lastQueued))
            {
                _lastQueued = Copy(state);
                _queue.Enqueue(new Pending { ArrivalUT = now + delay, State = _lastQueued });
            }

            // Half a physics frame of slack, so a near-zero delay doesn't add a frame of lag.
            double due = now + 0.5 * Time.fixedDeltaTime;
            while (_queue.Count > 0 && _queue.Peek().ArrivalUT <= due)
                _delivered = _queue.Dequeue().State;

            if (!connected && _queue.Count == 0)
            {
                _lastQueued = null;
                SignalLoss.Apply(_delivered, LightLagParams.Current.throttleOnSignalLoss);
            }

            state.CopyFrom(_delivered);
        }

        private static FlightCtrlState Copy(FlightCtrlState source)
        {
            var copy = new FlightCtrlState();
            copy.CopyFrom(source);
            return copy;
        }

        private static bool SameInput(FlightCtrlState a, FlightCtrlState b)
        {
            if (b == null) return false;
            if (a.mainThrottle != b.mainThrottle || a.pitch != b.pitch || a.yaw != b.yaw || a.roll != b.roll
                || a.pitchTrim != b.pitchTrim || a.yawTrim != b.yawTrim || a.rollTrim != b.rollTrim
                || a.X != b.X || a.Y != b.Y || a.Z != b.Z
                || a.wheelSteer != b.wheelSteer || a.wheelSteerTrim != b.wheelSteerTrim
                || a.wheelThrottle != b.wheelThrottle || a.wheelThrottleTrim != b.wheelThrottleTrim
                || a.killRot != b.killRot || a.gearUp != b.gearUp || a.gearDown != b.gearDown || a.headlight != b.headlight)
                return false;
            float[] ca = a.custom_axes, cb = b.custom_axes;
            if (ca == null || cb == null) return ca == cb;
            if (ca.Length != cb.Length) return false;
            for (int i = 0; i < ca.Length; i++)
                if (ca[i] != cb[i]) return false;
            return true;
        }
    }
}
