using System;
using System.Collections.Generic;

namespace LightLag
{
    /// <summary>How a part-menu event or field is treated.</summary>
    internal enum PartMenuRule
    {
        /// <summary>Queued with the signal delay (the default).</summary>
        Delay,

        /// <summary>Needs a signal but isn't delayed (<c>category = "skip_delay"</c>).</summary>
        NoDelay,

        /// <summary>Acts at once with no checks (<c>category = "skip_control"</c>, or listed in <c>LIGHTLAG_EXEMPTIONS</c>).</summary>
        Instant,
    }

    /// <summary>
    /// Which part-menu events and fields aren't delayed. Read from every <c>LIGHTLAG_EXEMPTIONS</c>
    /// node in the game database (GameData/LightLag/Exemptions.cfg, plus other mods' patches),
    /// and from RemoteTech's <c>skip_delay</c> and <c>skip_control</c> categories.
    /// </summary>
    internal static class Exemptions
    {
        private static HashSet<string> _events;
        private static HashSet<string> _fields;
        private static readonly Dictionary<(Type, string, bool), bool> Cache = new Dictionary<(Type, string, bool), bool>();

        public static PartMenuRule ForEvent(BaseEvent evt, PartModule module) =>
            Rule(evt.category, module, evt.name, isEvent: true);

        public static PartMenuRule ForField(BaseField field, PartModule module) =>
            Rule(field.category, module, field.name, isEvent: false);

        private static PartMenuRule Rule(string category, PartModule module, string member, bool isEvent)
        {
            if (category != null)
            {
                if (category.Contains("skip_control")) return PartMenuRule.Instant;
                if (category.Contains("skip_delay")) return PartMenuRule.NoDelay;
            }
            return Listed(module != null ? module.GetType() : typeof(Part), member, isEvent) ? PartMenuRule.Instant : PartMenuRule.Delay;
        }

        private static bool Listed(Type type, string member, bool isEvent)
        {
            if (Cache.TryGetValue((type, member, isEvent), out bool listed)) return listed;
            Load();
            HashSet<string> set = isEvent ? _events : _fields;
            listed = set.Contains("*." + member);
            for (Type t = type; !listed && t != null && t != typeof(object); t = t.BaseType)
                listed = set.Contains(t.Name + "." + member);
            Cache[(type, member, isEvent)] = listed;
            return listed;
        }

        private static void Load()
        {
            if (_events != null) return;
            _events = new HashSet<string>(StringComparer.Ordinal);
            _fields = new HashSet<string>(StringComparer.Ordinal);
            ConfigNode[] nodes = GameDatabase.Instance?.GetConfigNodes("LIGHTLAG_EXEMPTIONS") ?? new ConfigNode[0];
            foreach (ConfigNode node in nodes)
            {
                foreach (string e in node.GetValues("event")) _events.Add(e.Trim());
                foreach (string f in node.GetValues("field")) _fields.Add(f.Trim());
            }
            Log.Info($"part-menu exemptions: {_events.Count} events, {_fields.Count} fields, from {nodes.Length} LIGHTLAG_EXEMPTIONS nodes");
        }
    }
}
