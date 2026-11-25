using CommNet;

namespace LightLag
{
    /// <summary>
    /// Local control means a command doesn't cross the network: an EVA kerbal, or a vessel
    /// whose crew can control it (CommNet's <see cref="VesselControlState.Kerbal"/> flag).
    /// </summary>
    public static class LocalControl
    {
        public static bool Has(Vessel vessel)
        {
            if (vessel == null) return false;
            if (vessel.isEVA) return true;
            CommNetVessel connection = vessel.connection;
            return connection != null && (connection.ControlState & VesselControlState.Kerbal) != 0;
        }
    }
}
