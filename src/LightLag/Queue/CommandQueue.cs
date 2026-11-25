using System.Collections.Generic;

namespace LightLag
{
    /// <summary>
    /// A vessel's queued commands, in the order they run (by <see cref="QueuedCommand.ExecuteUT"/>,
    /// then the order they were sent).
    /// </summary>
    public class CommandQueue
    {
        private readonly List<QueuedCommand> _commands = new List<QueuedCommand>();

        public IReadOnlyList<QueuedCommand> Commands => _commands;

        public int Count => _commands.Count;

        /// <summary>The command added last, or null.</summary>
        public QueuedCommand Last { get; private set; }

        /// <summary>ExecuteUT of the next command still to run, or +Infinity.</summary>
        public double NextExecuteUT => _commands.Count > 0 ? _commands[0].ExecuteUT : double.PositiveInfinity;

        public void Add(QueuedCommand command)
        {
            int i = _commands.Count;
            while (i > 0 && _commands[i - 1].ExecuteUT > command.ExecuteUT) i--;
            _commands.Insert(i, command);
            Last = command;
        }

        public void Clear()
        {
            _commands.Clear();
            Last = null;
        }

        /// <summary>
        /// Runs every command due by <paramref name="now"/>. A command whose cancel reached the
        /// vessel before it was due is dropped instead; a cancel arriving later has no effect.
        /// </summary>
        public void Process(Vessel vessel, double now)
        {
            while (_commands.Count > 0)
            {
                QueuedCommand command = _commands[0];
                bool cancelled = command.CancelPending && command.CancelArrivalUT <= now
                                 && command.CancelArrivalUT <= command.ExecuteUT;
                if (!cancelled && command.ExecuteUT > now) return;

                _commands.RemoveAt(0);
                if (command == Last) Last = null;
                if (cancelled)
                {
                    Log.Info($"cancelled {command.TypeName} '{command.Label}' on {vessel.vesselName}");
                    Messages.Post(KSP.Localization.Localizer.Format("#LightLag_Message_Cancelled", command.Label));
                    continue;
                }

                string error = null;
                PlayerInput.Bypass(() => error = command.Execute(vessel));
                double late = now - command.ExecuteUT;
                Log.Info($"executed {command.TypeName} '{command.Label}' on {vessel.vesselName}"
                         + (late > 1.0 ? $", {late:0.#} s late" : "") + (error != null ? $": {error}" : ""));
                if (error != null)
                    Messages.Post(KSP.Localization.Localizer.Format("#LightLag_Message_Failed", command.Label, error));
            }
        }

        /// <summary>
        /// Sends a cancel for <paramref name="command"/>. It reaches the vessel after
        /// <paramref name="delay"/>, so it works only if it arrives before the command runs.
        /// </summary>
        public static bool CanCancel(QueuedCommand command, double now, double delay)
            => !command.CancelPending && !double.IsInfinity(delay) && now + delay < command.ExecuteUT;

        public void Save(ConfigNode node)
        {
            foreach (QueuedCommand command in _commands) command.Save(node.AddNode("COMMAND"));
        }

        public void Load(ConfigNode node, string vesselName)
        {
            Clear();
            foreach (ConfigNode child in node.GetNodes("COMMAND"))
            {
                QueuedCommand command = QueuedCommand.Load(child);
                if (command == null) Log.Warn($"dropped an unreadable queued command on {vesselName}: {child.GetValue("type")}");
                else Add(command);
            }
            Last = null;
        }
    }
}
