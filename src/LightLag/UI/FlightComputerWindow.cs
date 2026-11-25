using System;
using System.Globalization;
using KSP.Localization;
using UnityEngine;

namespace LightLag
{
    /// <summary>
    /// The flight computer: the active vessel's signal and command queue, opened from the delay
    /// readout on the signal bar (<see cref="SignalBarWidget"/>). Shows the delay and the control
    /// path hop by hop, whether stick and throttle input is still arriving and until when (it
    /// can't be cancelled), the vessel's extra delay, and each queued command with its countdown
    /// and a cancel button (the cancel is itself a signal, so it's allowed only if it can arrive
    /// before the command runs). Drawn with IMGUI in KSP's skin.
    /// </summary>
    internal class FlightComputerWindow
    {
        private const int WindowId = 0x4C4C5157;
        private const string ExtraDelayControl = "LightLag_ExtraDelay";
        private const string InputLock = "LightLag_FlightComputer";

        private Rect _rect = new Rect(260, 100, 380, 120);
        private Guid _vesselId;
        private string _extraText = "0";

        public bool Visible { get; private set; }

        public void Toggle()
        {
            Visible = !Visible;
            if (!Visible) Release();
        }

        public void Close()
        {
            Visible = false;
            Release();
        }

        public void Draw()
        {
            if (!Visible) return;
            Vessel vessel = FlightGlobals.ActiveVessel;
            LightLagVessel module = LightLagVessel.For(vessel);
            if (module == null) return;
            if (vessel.id != _vesselId)
            {
                _vesselId = vessel.id;
                _extraText = module.ExtraDelay.ToString("0.###", CultureInfo.InvariantCulture);
            }

            GUISkin saved = GUI.skin;
            GUI.skin = HighLogic.Skin;
            _rect = GUILayout.Window(WindowId, _rect, id => Contents(vessel, module),
                Localizer.Format("#LightLag_Queue_Title", vessel.vesselName), GUILayout.Width(380));
            GUI.skin = saved;

            // Typing in the extra delay field mustn't stage the vessel or toggle its groups.
            if (GUI.GetNameOfFocusedControl() == ExtraDelayControl)
                InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, InputLock);
            else
                InputLockManager.RemoveControlLock(InputLock);
        }

        public void Release() => InputLockManager.RemoveControlLock(InputLock);

        private void Contents(Vessel vessel, LightLagVessel module)
        {
            double now = Planetarium.GetUniversalTime();
            double delay = DelayService.GetDelay(vessel);

            GUILayout.Label(DelayStatus.Long(vessel) ?? Localizer.Format("#LightLag_Queue_NoDelay"));
            PathLines(vessel);

            // Stick and throttle are analog: holding a key sends a stream of changes, so they're
            // shown as one line (still arriving, and until when), not as a list of commands.
            FlyByWireDelay input = module.Input;
            if (input != null && input.PendingCount > 0)
                GUILayout.Label(Localizer.Format("#LightLag_Queue_InputInFlight",
                    DelayService.Format(Math.Max(0, input.LastArrivalUT - now))));

            GUILayout.BeginHorizontal();
            GUILayout.Label(Localizer.Format("#LightLag_Queue_ExtraDelay"), GUILayout.ExpandWidth(true));
            GUI.SetNextControlName(ExtraDelayControl);
            string text = GUILayout.TextField(_extraText, GUILayout.Width(70));
            if (text != _extraText)
            {
                _extraText = text;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0)
                    module.ExtraDelay = seconds;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Localizer.Format("#LightLag_Queue_ExtraDelay_Tip"));

            if (module.Queue.Count == 0)
                GUILayout.Label(Localizer.Format("#LightLag_Queue_Empty"));

            foreach (QueuedCommand command in module.Queue.Commands)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(command.Saveable ? command.Label : Localizer.Format("#LightLag_Queue_NotSaved", command.Label), GUILayout.Width(130));
                GUILayout.Label(Status(command, now), GUILayout.ExpandWidth(true));
                bool wasEnabled = GUI.enabled;
                GUI.enabled = CommandQueue.CanCancel(command, now, delay);
                if (GUILayout.Button(Localizer.Format("#LightLag_Queue_Cancel"), GUILayout.Width(70)))
                {
                    command.CancelArrivalUT = now + delay;
                    Log.Info($"cancel sent for {command.TypeName} '{command.Label}' on {vessel.vesselName}, arrives in {delay:0.###} s");
                }
                GUI.enabled = wasEnabled;
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button(Localizer.Format("#LightLag_Queue_Close"))) Close();
            GUI.DragWindow();
        }

        /// <summary>One line per hop of the control path: where it goes, how far, and its share of the delay.</summary>
        private static void PathLines(Vessel vessel)
        {
            if (DelayStatus.Of(vessel, out _) != DelayStatus.State.Delay) return;
            CommNet.CommPath path = vessel.connection?.ControlPath;
            if (path == null || path.Count == 0) return;
            LightLagParams settings = LightLagParams.Current;
            double speed = DelayService.SpeedOfLight * (settings?.lightSpeedMultiplier ?? 1.0);
            for (int i = 0; i < path.Count; i++)
            {
                CommNet.CommLink link = path[i];
                if (link?.a == null || link.b == null) continue;
                double meters = (link.a.position - link.b.position).magnitude;
                GUILayout.Label(Localizer.Format("#LightLag_Computer_Hop", link.b.displayName,
                    (meters / 1000.0).ToString("N0", CultureInfo.CurrentCulture), DelayService.Format(meters / speed)));
            }
        }

        private static string Status(QueuedCommand command, double now)
        {
            if (command.CancelPending)
                return Localizer.Format("#LightLag_Queue_Cancelling", DelayService.Format(Math.Max(0, command.CancelArrivalUT - now)));
            if (now < command.ArrivalUT)
                return Localizer.Format("#LightLag_Queue_Arrives", DelayService.Format(command.ArrivalUT - now),
                    DelayService.Format(command.ExecuteUT - now));
            return Localizer.Format("#LightLag_Queue_Held", DelayService.Format(Math.Max(0, command.ExecuteUT - now)));
        }
    }
}
