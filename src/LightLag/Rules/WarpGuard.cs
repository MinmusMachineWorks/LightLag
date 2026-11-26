namespace LightLag
{
    /// <summary>
    /// Drops on-rails time warp to 1× before a queued command runs on a loaded vessel, so warp
    /// can't carry the game past it (and staging never runs on rails). It looks ahead by three
    /// frames at the current warp rate, plus a second. A stock warp-to under way (the map's "Warp
    /// Here", or the flight computer's warp buttons) is cancelled first, so it doesn't speed back
    /// up. Physics warp is left alone: commands run normally under it.
    /// </summary>
    internal static class WarpGuard
    {
        public static void Check(CommandQueue queue, double now)
        {
            if (TimeWarp.fetch == null || TimeWarp.CurrentRateIndex == 0 || TimeWarp.WarpMode != TimeWarp.Modes.HIGH
                || queue.Count == 0) return;
            double lookahead = TimeWarp.CurrentRate * UnityEngine.Time.deltaTime * 3.0 + 1.0;
            if (queue.NextExecuteUT - now > lookahead) return;
            Log.Info($"dropping time warp ({TimeWarp.CurrentRate}×): a queued command runs in {queue.NextExecuteUT - now:0.#} s");
            TimeWarp.fetch.CancelAutoWarp();
            TimeWarp.SetRate(0, true);
        }
    }
}
