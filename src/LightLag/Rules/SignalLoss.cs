namespace LightLag
{
    /// <summary>
    /// What an uncrewed vessel does once its signal is lost and every input already sent has
    /// arrived. Inputs that were in flight when the signal dropped still arrive first. Applies
    /// only while stock CommNet's "Require Signal for Control" is on; with it off, a vessel
    /// without a signal is controlled at once (<see cref="DelayService.Applies"/>).
    /// </summary>
    public static class SignalLoss
    {
        /// <summary>
        /// Centers the stick, translation and wheel steering. Trim stays (it's held on board).
        /// The main and wheel throttles are cut or held, per the setting.
        /// </summary>
        public static void Apply(FlightCtrlState state, ThrottleOnSignalLoss throttle)
        {
            state.pitch = 0f;
            state.yaw = 0f;
            state.roll = 0f;
            state.X = 0f;
            state.Y = 0f;
            state.Z = 0f;
            state.wheelSteer = 0f;
            if (throttle == ThrottleOnSignalLoss.Cut)
            {
                state.mainThrottle = 0f;
                state.wheelThrottle = 0f;
            }
        }
    }
}
