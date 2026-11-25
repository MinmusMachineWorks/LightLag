using CommNet;

namespace LightLag
{
    /// <summary>
    /// Signal delay for a vessel, read from CommNet's control path. Works with any CommNet
    /// implementation that fills <c>vessel.connection</c> (stock, RealAntennas).
    /// </summary>
    public static class DelayService
    {
        /// <summary>Speed of light in m/s, before the settings multiplier.</summary>
        public const double SpeedOfLight = 299792458.0;

        /// <summary>
        /// True when commands to this vessel are delayed: delay is enabled, CommNet is running,
        /// and the vessel has no local control.
        /// </summary>
        public static bool Applies(Vessel vessel)
        {
            LightLagParams settings = LightLagParams.Current;
            if (settings == null || !settings.delayEnabled) return false;
            if (!CommNetScenario.CommNetEnabled) return false;
            if (vessel == null || vessel.connection == null) return false;
            return !LocalControl.Has(vessel);
        }

        /// <summary>
        /// Seconds until a command sent now reaches the vessel (doubled with round-trip delay).
        /// 0 when delay doesn't apply, <see cref="double.PositiveInfinity"/> when there's no connection.
        /// </summary>
        public static double GetDelay(Vessel vessel)
        {
            if (!Applies(vessel)) return 0.0;
            CommNetVessel connection = vessel.connection;
            if (!connection.IsConnected) return double.PositiveInfinity;

            double meters = PathLength(connection.ControlPath);
            LightLagParams settings = LightLagParams.Current;
            double delay = meters / (SpeedOfLight * settings.lightSpeedMultiplier);
            return settings.roundTrip ? 2.0 * delay : delay;
        }

        /// <summary>Sum of the link lengths along a CommNet path, in meters.</summary>
        public static double PathLength(CommPath path)
        {
            if (path == null) return 0.0;
            double meters = 0.0;
            for (int i = 0; i < path.Count; i++)
            {
                CommLink link = path[i];
                if (link?.a == null || link.b == null) continue;
                meters += (link.a.position - link.b.position).magnitude;
            }
            return meters;
        }

        /// <summary>"0.42 s", "12.3 s", "4m 05s", "1h 02m", or "∞".</summary>
        public static string Format(double seconds)
        {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds)) return "∞";
            if (seconds < 10.0) return $"{seconds:0.00} s";
            if (seconds < 60.0) return $"{seconds:0.0} s";
            if (seconds < 3600.0)
            {
                int s = (int)seconds;
                return $"{s / 60}m {s % 60:00}s";
            }
            int m = (int)(seconds / 60.0);
            return $"{m / 60}h {m % 60:00}m";
        }
    }
}
