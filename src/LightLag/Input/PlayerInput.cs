using System;

namespace LightLag
{
    /// <summary>
    /// Tells whether the code running now was started by the player. Harmony brackets each
    /// player entry point with <see cref="Enter"/> (prefix) and <see cref="Exit"/> (finalizer):
    /// <list type="bullet">
    /// <item><c>FlightInputHandler.Update</c>: flight keys (stage, SAS, RCS, gear, lights, brakes, abort, custom groups).</item>
    /// <item><c>KSP.UI.Screens.Flight.ActionGroupToggleButton.SetToggle</c>: the SAS, RCS, gear, lights and brakes buttons.</item>
    /// <item><c>VesselAutopilotUI.OnClickButton</c>: the SAS mode buttons on the navball.</item>
    /// <item><c>UIPartActionButton.OnClick</c>: part-menu buttons.</item>
    /// </list>
    /// Part-menu fields are reached only from the menu, so <see cref="PartMenuHooks"/> treat
    /// every <c>UIPartActionFieldItem.SetFieldValue</c> as the player's, except inside
    /// <see cref="Suppressed"/> sections (a menu clamping a value as it opens).
    /// <see cref="CommandHooks"/> only queue a command while <see cref="Active"/>. Calls from KSP
    /// itself (SAS dropping to stability assist, missions) and from other mods (MechJeb staging)
    /// run outside these entry points and stay instant.
    /// </summary>
    internal static class PlayerInput
    {
        private static int _depth;
        private static int _suppressed;

        public static bool Active => _depth > 0 && _suppressed == 0;

        /// <summary>True inside code that runs part-menu setters without the player.</summary>
        public static bool Suppressed => _suppressed > 0;

        public static void Suppress() => _suppressed++;

        public static void Unsuppress(Exception __exception)
        {
            if (_suppressed > 0) _suppressed--;
        }

        public static void Enter() => _depth++;

        public static void Exit(Exception __exception)
        {
            if (_depth > 0) _depth--;
        }

        /// <summary>Runs <paramref name="action"/> outside player context, so the hooks let it through.</summary>
        public static void Bypass(Action action)
        {
            int saved = _depth;
            _depth = 0;
            _suppressed++;
            try { action(); }
            finally
            {
                _depth = saved;
                _suppressed--;
            }
        }
    }
}
