using KSP.Localization;

namespace LightLag
{
    /// <summary>The active vessel's delay state as text, for the signal bar and the flight computer.</summary>
    internal static class DelayStatus
    {
        internal enum State { Off, Local, NoSignalNotRequired, NoSignal, Delay }

        internal static State Of(Vessel vessel, out double delay)
        {
            delay = 0;
            LightLagParams settings = LightLagParams.Current;
            if (vessel == null || settings == null || !settings.delayEnabled || !CommNet.CommNetScenario.CommNetEnabled)
                return State.Off;
            if (LocalControl.Has(vessel)) return State.Local;
            if (DelayService.NoSignalNeeded(vessel)) return State.NoSignalNotRequired;
            delay = DelayService.GetDelay(vessel);
            return double.IsInfinity(delay) ? State.NoSignal : State.Delay;
        }

        /// <summary>Short text for the signal bar: "2.31 s", "Local", plus the queued count. Null when delay is off.</summary>
        internal static string Short(Vessel vessel)
        {
            State state = Of(vessel, out double delay);
            string text;
            switch (state)
            {
                case State.Off: return null;
                case State.Local: text = Localizer.Format("#LightLag_Bar_Local"); break;
                case State.NoSignalNotRequired: text = Localizer.Format("#LightLag_Bar_NoDelay"); break;
                case State.NoSignal: return null;
                default: text = DelayService.Format(delay); break;
            }
            int queued = LightLagVessel.For(vessel)?.Queue.Count ?? 0;
            return queued > 0 ? Localizer.Format("#LightLag_Bar_Queued", text, queued) : text;
        }

        /// <summary>
        /// One line: the delay, "no signal" or "local control", plus whether stick and throttle
        /// input is still arriving and how many commands are queued. Null when delay is off.
        /// </summary>
        internal static string Long(Vessel vessel)
        {
            State state = Of(vessel, out double delay);
            string text;
            switch (state)
            {
                case State.Off: return null;
                case State.Local: text = Localizer.Format("#LightLag_Readout_Local"); break;
                case State.NoSignalNotRequired: text = Localizer.Format("#LightLag_Readout_NoSignalNotRequired"); break;
                case State.NoSignal: text = Localizer.Format("#LightLag_Readout_NoSignal"); break;
                default: text = Localizer.Format("#LightLag_Readout_Delay", DelayService.Format(delay)); break;
            }
            LightLagVessel module = LightLagVessel.For(vessel);
            if ((module?.Input?.PendingCount ?? 0) > 0) text += " · " + Localizer.Format("#LightLag_Readout_Pending");
            int queued = module?.Queue.Count ?? 0;
            if (queued > 0) text += " · " + Localizer.Format("#LightLag_Readout_Queued", queued);
            return text;
        }
    }
}
