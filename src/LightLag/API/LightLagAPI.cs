using System;

namespace LightLag
{
    /// <summary>
    /// LightLag's API for other mods, such as MechJebRTBridge. It's stable: members are only
    /// added, and <see cref="ApiVersion"/> goes up when they are. A mod that doesn't want a
    /// hard dependency can call it by reflection on <c>LightLag.LightLagAPI</c>.
    ///
    /// <para><b>Sending a command:</b> check <see cref="Applies"/>; if commands to the vessel
    /// aren't delayed, run yours at once. Otherwise check <see cref="IsConnected"/>: with no
    /// signal, refuse it, as LightLag does its own. Then <see cref="QueueCommand"/>: LightLag
    /// queues it like the player's own commands (the signal delay, the vessel's extra delay, the
    /// flight computer's countdown and Abort, saved with the vessel), and calls you back when it
    /// runs on board.</para>
    ///
    /// <para><b>Hooks:</b> while LightLag runs a queued command, <see cref="IsRunningCommand"/>
    /// is true. A hook that delays calls (as the bridge's Harmony prefixes do) should let calls
    /// made then through, or a command that fires another (an action group bound to a MechJeb
    /// action) would be delayed twice.</para>
    /// </summary>
    public static class LightLagAPI
    {
        /// <summary>1: the members below.</summary>
        public const int ApiVersion = 1;

        /// <summary>Light-speed delay is on in this game and CommNet is running.</summary>
        public static bool IsEnabled()
        {
            LightLagParams settings = LightLagParams.Current;
            return settings != null && settings.delayEnabled && CommNet.CommNetScenario.CommNetEnabled;
        }

        /// <summary>
        /// Commands to the vessel are delayed (or, with no signal, refused). False under local
        /// control, with delay off, or with no signal when stock's "Require Signal for Control" is off.
        /// </summary>
        public static bool Applies(Guid vesselId) => DelayService.Applies(Find(vesselId));

        /// <summary>Crew aboard that can control it, or an EVA kerbal: commands don't cross the network.</summary>
        public static bool HasLocalControl(Guid vesselId) => LocalControl.Has(Find(vesselId));

        /// <summary>CommNet gives the vessel a control path.</summary>
        public static bool IsConnected(Guid vesselId) => Find(vesselId)?.connection?.IsConnected ?? false;

        /// <summary>
        /// Seconds a command sent now takes to reach the vessel, one way. 0 when delay doesn't
        /// apply; +Infinity with no signal when one is required. The vessel's extra delay isn't
        /// included: see <see cref="GetExtraDelay"/>.
        /// </summary>
        public static double GetDelay(Guid vesselId) => DelayService.GetDelay(Find(vesselId));

        /// <summary>The player's extra delay for the vessel, in seconds (0 for an unloaded vessel).</summary>
        public static double GetExtraDelay(Guid vesselId) => LightLagVessel.For(Find(vesselId))?.ExtraDelay ?? 0.0;

        /// <summary>
        /// Queues a command for a loaded vessel. Returns its id, or <see cref="Guid.Empty"/> if it
        /// wasn't queued: delay doesn't apply, no signal, the vessel isn't loaded, or the node
        /// lacks its callback. The node holds:
        /// <list type="bullet">
        /// <item><c>ReflectionType</c>: the callback's class, assembly-qualified or full name.</item>
        /// <item><c>ReflectionPopMethod</c>: a public static method on it taking one
        /// <see cref="ConfigNode"/>. It's called with a copy of this node when the command runs
        /// on board. Its return value is ignored (RemoteTech's "stay active" bool means nothing
        /// here). To report a failure, throw: LightLag shows the message.</item>
        /// <item><c>QueueLabel</c>: what the flight computer shows. <c>Executor</c>: your mod's
        /// name, shown if there's no label and written to the log.</item>
        /// <item>Anything else you need to run it, including after a reload.</item>
        /// </list>
        /// The keys match RemoteTech's <c>QueueCommandToFlightComputer</c>, so the same node works for both.
        /// </summary>
        public static Guid QueueCommand(Guid vesselId, ConfigNode command)
        {
            Vessel vessel = Find(vesselId);
            if (vessel == null || command == null || !vessel.loaded) return Guid.Empty;
            ExternalCommand queued = ExternalCommand.From(command);
            if (queued == null)
            {
                Log.Warn($"API: a command from {command.GetValue("Executor") ?? "a mod"} has no ReflectionType or ReflectionPopMethod; not queued");
                return Guid.Empty;
            }
            return CommandHooks.Queue(vessel, queued) ? queued.Id : Guid.Empty;
        }

        /// <summary>
        /// The command is still on its way or held on board: false once it has run, was
        /// aborted, or the vessel is gone or unloaded.
        /// </summary>
        public static bool IsQueued(Guid vesselId, Guid commandId)
        {
            LightLagVessel module = LightLagVessel.For(Find(vesselId));
            if (module == null || commandId == Guid.Empty) return false;
            foreach (QueuedCommand command in module.Queue.Commands)
                if (command is ExternalCommand external && external.Id == commandId) return true;
            return false;
        }

        /// <summary>True while LightLag runs a queued command on board (see the class notes on hooks).</summary>
        public static bool IsRunningCommand => CommandQueue.Running;

        private static Vessel Find(Guid vesselId) => vesselId == Guid.Empty ? null : FlightGlobals.FindVessel(vesselId);
    }
}
