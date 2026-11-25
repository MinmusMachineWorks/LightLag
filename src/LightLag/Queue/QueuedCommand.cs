using System.Globalization;

namespace LightLag
{
    /// <summary>
    /// A discrete command on its way to a vessel. It's sent at <see cref="SentUT"/>, reaches the
    /// vessel at <see cref="ArrivalUT"/>, and runs at <see cref="ExecuteUT"/> (later than arrival
    /// when the player set an extra delay; the vessel holds it on board until then).
    /// </summary>
    public abstract class QueuedCommand
    {
        public double SentUT;
        public double ArrivalUT;
        public double ExecuteUT;

        /// <summary>When a cancel reaches the vessel, or NaN if none was sent.</summary>
        public double CancelArrivalUT = double.NaN;

        public bool CancelPending => !double.IsNaN(CancelArrivalUT);

        /// <summary>Name saved with the command, used to recreate it on load.</summary>
        public abstract string TypeName { get; }

        /// <summary>Short text for the queue window and messages.</summary>
        public abstract string Label { get; }

        /// <summary>Runs the command on the vessel. Returns null on success, or why it couldn't run.</summary>
        public abstract string Execute(Vessel vessel);

        protected virtual void SavePayload(ConfigNode node) { }
        protected virtual void LoadPayload(ConfigNode node) { }

        public void Save(ConfigNode node)
        {
            node.AddValue("type", TypeName);
            node.AddValue("sent", SentUT.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("arrival", ArrivalUT.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("execute", ExecuteUT.ToString("R", CultureInfo.InvariantCulture));
            if (CancelPending) node.AddValue("cancelArrival", CancelArrivalUT.ToString("R", CultureInfo.InvariantCulture));
            SavePayload(node);
        }

        /// <summary>Recreates a saved command, or returns null for an unknown or broken entry.</summary>
        public static QueuedCommand Load(ConfigNode node)
        {
            QueuedCommand command;
            switch (node.GetValue("type"))
            {
                case StageCommand.Name: command = new StageCommand(); break;
                case ActionGroupCommand.Name: command = new ActionGroupCommand(); break;
                case SasModeCommand.Name: command = new SasModeCommand(); break;
                default: return null;
            }
            if (!TryRead(node, "sent", out command.SentUT) || !TryRead(node, "arrival", out command.ArrivalUT)
                || !TryRead(node, "execute", out command.ExecuteUT))
                return null;
            if (TryRead(node, "cancelArrival", out double cancel)) command.CancelArrivalUT = cancel;
            command.LoadPayload(node);
            return command;
        }

        private static bool TryRead(ConfigNode node, string key, out double value)
        {
            value = 0;
            string s = node.GetValue(key);
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
