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

        private static int _running;

        /// <summary>True while a queued command runs on board (<see cref="LightLagAPI.IsRunningCommand"/>).</summary>
        internal static bool Running => _running > 0;

        public int Count => _commands.Count;

        /// <summary>
        /// Whether the flight computer lists <paramref name="command"/> and the signal bar counts
        /// it: false when it runs within the "Hide short commands" setting of being sent, so a
        /// near-instant command doesn't flash up. It's queued and delayed all the same.
        /// </summary>
        public static bool Shown(QueuedCommand command)
            => command.ExecuteUT - command.SentUT >= (LightLagParams.Current?.hideCommandsBelow ?? 0f);

        /// <summary>How many commands are <see cref="Shown"/>.</summary>
        public int ShownCount
        {
            get
            {
                int n = 0;
                foreach (QueuedCommand command in _commands) if (Shown(command)) n++;
                return n;
            }
        }

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

        /// <summary>Removes a command that hasn't run, so it can be re-added with a new time.</summary>
        public bool Remove(QueuedCommand command)
        {
            if (!_commands.Remove(command)) return false;
            if (command == Last) Last = null;
            return true;
        }

        /// <summary>The newest field change still queued for this field of <paramref name="host"/>, or null.</summary>
        public PartFieldCommand PendingField(object host, string fieldName)
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
                if (_commands[i] is PartFieldCommand f && f.Host != null && ReferenceEquals(f.Host, host) && f.FieldName == fieldName)
                    return f;
            return null;
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
                _running++;
                try { PlayerInput.Bypass(() => error = command.Execute(vessel)); }
                finally { _running--; }
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
            foreach (QueuedCommand command in _commands)
                if (command.Saveable) command.Save(node.AddNode("COMMAND"));
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
