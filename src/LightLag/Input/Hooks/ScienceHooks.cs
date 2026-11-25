using System;

namespace LightLag
{
    /// <summary>
    /// The science results dialog (Review Data). A prefix on <c>ExperimentsResultDialog.addPage</c>
    /// wraps each page's Transmit, Discard (also used by Reset) and Process-in-lab callbacks
    /// before the dialog adds its own. Those callbacks run only from the dialog's buttons and the
    /// transmit warning's confirm button, so every call is the player's.
    ///
    /// When delay applies to the data's vessel, the click queues a <see cref="ScienceCommand"/>
    /// and runs the page's Keep callback instead: the data stays on board, and the part's menu
    /// returns to normal, until the command arrives. The dialog still closes the page. Keep
    /// itself acts at once, since it changes nothing on board.
    ///
    /// A callback that isn't a single method on a part module holding the data can't be saved,
    /// so it runs at once (logged). Other buttons inside windows opened from a part menu
    /// (KerbNet, the robotics controller, mod windows) aren't hooked yet: see COVERAGE.md.
    /// </summary>
    internal static class ScienceHooks
    {
        /// <summary>Prefix on <c>ExperimentsResultDialog.addPage</c>.</summary>
        public static void AddPagePrefix(ExperimentResultDialogPage page)
        {
            if (page == null || !HighLogic.LoadedSceneIsFlight) return;
            page.OnTransmitData = Wrap(page, ScienceCommand.Kind.Transmit, page.OnTransmitData);
            page.OnDiscardData = Wrap(page, ScienceCommand.Kind.Discard, page.OnDiscardData);
            page.OnSendToLab = Wrap(page, ScienceCommand.Kind.Lab, page.OnSendToLab);
        }

        private static Callback<ScienceData> Wrap(ExperimentResultDialogPage page, ScienceCommand.Kind kind, Callback<ScienceData> original)
        {
            if (original == null) return null;
            Callback<ScienceData> keep = page.OnKeepData;
            return data =>
            {
                if (!Route(page, kind, original, keep, data)) original(data);
            };
        }

        /// <summary>Returns true when the click was queued or blocked, false to run it now.</summary>
        private static bool Route(ExperimentResultDialogPage page, ScienceCommand.Kind kind,
            Callback<ScienceData> original, Callback<ScienceData> keep, ScienceData data)
        {
            Vessel vessel = page.host != null ? page.host.vessel : null;
            if (data == null || vessel == null || !DelayService.Applies(vessel)) return false;

            Delegate[] targets = original.GetInvocationList();
            if (targets.Length != 1 || !(targets[0].Target is PartModule module) || module.part == null
                || ScienceCommand.Find(module as IScienceDataContainer, data.subjectID) == null)
            {
                Log.Warn($"science dialog: {kind} on {page.host.partInfo?.name} has a callback that can't be queued; running it now");
                return false;
            }

            var command = new ScienceCommand
            {
                Action = kind,
                Module = PartRef.Of(module.part, module),
                Method = targets[0].Method.Name,
                SubjectId = data.subjectID,
                DataTitle = string.IsNullOrEmpty(data.title) ? data.subjectID : data.title,
            };
            if (!CommandHooks.TryQueue(vessel, command)) return false;
            keep?.Invoke(data);
            return true;
        }
    }
}
