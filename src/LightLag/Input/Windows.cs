using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LightLag
{
    /// <summary>A method that's delayed when the player calls it from a window.</summary>
    internal sealed class WindowCommandSpec
    {
        public string Window;
        public string Label;
        /// <summary>Calls in a drag fold into the one already on its way.</summary>
        public bool Slider;
        /// <summary>Field or property on the instance holding its part module, part or vessel; null when the instance is the part module.</summary>
        public string HostMember;
        /// <summary>The call can be saved: an instance method on a part module with simple arguments.</summary>
        public bool Persistable;
    }

    /// <summary>Applies <see cref="Windows"/> once Module Manager and the game database have loaded.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, once: true)]
    public class WindowPatcher : MonoBehaviour
    {
        public void Start() => Windows.Apply();
    }

    /// <summary>
    /// Buttons inside windows (the robotics controller, mod windows). Each <c>LIGHTLAG_WINDOW</c>
    /// node names the window's entry methods, the code its buttons run in, and the methods those
    /// buttons call that command the vessel:
    /// <code>
    /// LIGHTLAG_WINDOW
    /// {
    ///     name = KAS winch remote
    ///     entry = KAS.ControllerWinchRemote.OnGUI
    ///     command = KAS.KASLinkWinch.ReleaseCable, Release cable
    ///     slider = Some.Module.SetSpeed, Speed
    ///     command = Some.Window.Apply @ module, Apply
    /// }
    /// </code>
    /// An entry is patched with <see cref="PlayerInput"/> (prefix and finalizer), so calls made
    /// inside it are the player's. A command is <c>Type.Method</c>, with every overload and
    /// override patched, then optionally <c>@ member</c> (a field or property on the instance that
    /// holds its part module, part or vessel, when the instance isn't a part module) and
    /// <c>, label</c>. Only methods returning void or bool can be delayed; a queued bool method
    /// reports true. <see cref="WindowHooks"/> does the queuing.
    /// </summary>
    internal static class Windows
    {
        private static readonly Dictionary<MethodBase, WindowCommandSpec> _specs = new Dictionary<MethodBase, WindowCommandSpec>();
        private static readonly HashSet<MethodBase> _entries = new HashSet<MethodBase>();
        private static List<Type> _allTypes;
        private static bool _applied;

        internal static WindowCommandSpec For(MethodBase method)
            => method != null && _specs.TryGetValue(method, out WindowCommandSpec spec) ? spec : null;

        internal static void Apply()
        {
            if (_applied) return;
            _applied = true;
            var harmony = new Harmony(Bootstrap.HarmonyId);
            ConfigNode[] nodes = GameDatabase.Instance?.GetConfigNodes("LIGHTLAG_WINDOW") ?? new ConfigNode[0];
            var enter = new HarmonyMethod(typeof(PlayerInput), nameof(PlayerInput.Enter));
            var exit = new HarmonyMethod(typeof(PlayerInput), nameof(PlayerInput.Exit));
            var voidPrefix = new HarmonyMethod(typeof(WindowHooks), nameof(WindowHooks.VoidPrefix));
            var boolPrefix = new HarmonyMethod(typeof(WindowHooks), nameof(WindowHooks.BoolPrefix));
            int windows = 0;

            foreach (ConfigNode node in nodes)
            {
                string window = node.GetValue("name") ?? "(unnamed)";
                var entries = new List<MethodBase>();
                bool missing = false;
                foreach (string entry in node.GetValues("entry"))
                {
                    List<MethodBase> found = Resolve(entry.Trim(), window, ref missing);
                    entries.AddRange(found);
                }
                if (missing && entries.Count == 0) { Log.Info($"window '{window}': not installed, skipped"); continue; }

                foreach (MethodBase method in entries)
                {
                    if (!_entries.Add(method)) continue;
                    try { harmony.Patch(method, prefix: enter, finalizer: exit); }
                    catch (Exception e) { Log.Warn($"window '{window}': can't patch entry {Describe(method)}: {e.Message}"); }
                }

                int commands = 0;
                foreach (var (text, slider) in node.GetValues("command").Select(c => (c, false))
                             .Concat(node.GetValues("slider").Select(c => (c, true))))
                {
                    ParseCommand(text, out string target, out string host, out string label);
                    bool m = false;
                    foreach (MethodBase method in Resolve(target, window, ref m))
                    {
                        if (_specs.ContainsKey(method)) continue;
                        Type returns = (method as MethodInfo)?.ReturnType ?? typeof(void);
                        if (returns != typeof(void) && returns != typeof(bool))
                        {
                            Log.Warn($"window '{window}': {Describe(method)} returns {returns.Name}; only void and bool methods can be delayed");
                            continue;
                        }
                        ParameterInfo[] parameters = method.GetParameters();
                        _specs[method] = new WindowCommandSpec
                        {
                            Window = window,
                            Label = string.IsNullOrEmpty(label) ? Nicify(method.Name) : label,
                            Slider = slider,
                            HostMember = host,
                            Persistable = !method.IsStatic && host == null && typeof(PartModule).IsAssignableFrom(method.DeclaringType)
                                && parameters.All(p => !p.ParameterType.IsByRef && IsSimple(p.ParameterType)),
                        };
                        try
                        {
                            harmony.Patch(method, prefix: returns == typeof(bool) ? boolPrefix : voidPrefix);
                            commands++;
                        }
                        catch (Exception e)
                        {
                            _specs.Remove(method);
                            Log.Warn($"window '{window}': can't patch {Describe(method)}: {e.Message}");
                        }
                    }
                }
                windows++;
                Log.Info($"window '{window}': {entries.Count} entry methods, {commands} delayed methods");
            }
            Log.Info($"window commands: {windows} windows, {_specs.Count} delayed methods, from {nodes.Length} LIGHTLAG_WINDOW nodes");
            _allTypes = null;
        }

        /// <summary>"Type.Method [@ host] [, label]".</summary>
        private static void ParseCommand(string text, out string target, out string host, out string label)
        {
            label = null;
            host = null;
            int comma = text.IndexOf(',');
            if (comma >= 0) { label = text.Substring(comma + 1).Trim(); text = text.Substring(0, comma); }
            int at = text.IndexOf('@');
            if (at >= 0) { host = text.Substring(at + 1).Trim(); text = text.Substring(0, at); }
            target = text.Trim();
        }

        /// <summary>Every non-abstract overload of Type.Method, on the type and on its subclasses.</summary>
        private static List<MethodBase> Resolve(string spec, string window, ref bool missing)
        {
            var result = new List<MethodBase>();
            int dot = spec.LastIndexOf('.');
            if (dot <= 0) { Log.Warn($"window '{window}': '{spec}' isn't Type.Method"); return result; }
            string typeName = spec.Substring(0, dot), methodName = spec.Substring(dot + 1);
            Type type = FindType(typeName);
            if (type == null) { missing = true; return result; }

            IEnumerable<Type> types = new[] { type };
            if (!type.IsSealed)
            {
                if (_allTypes == null) _allTypes = AccessTools.AllTypes().ToList();
                types = types.Concat(_allTypes.Where(t => t != type && type.IsAssignableFrom(t) && t.IsClass));
            }
            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type t in types)
            {
                MethodInfo[] methods;
                try { methods = t.GetMethods(flags); }
                catch (Exception) { continue; }
                result.AddRange(methods.Where(m => m.Name == methodName && !m.IsAbstract && !m.ContainsGenericParameters));
            }
            if (result.Count == 0) Log.Warn($"window '{window}': {typeName} has no method {methodName}");
            return result;
        }

        private static Type FindType(string name)
        {
            if (_allTypes == null) _allTypes = AccessTools.AllTypes().ToList();
            return _allTypes.FirstOrDefault(t => t.FullName == name) ?? _allTypes.FirstOrDefault(t => t.Name == name);
        }

        internal static bool IsSimple(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Vector2) || t == typeof(Vector3);

        internal static string Describe(MethodBase m) => $"{m.DeclaringType?.FullName}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})";

        /// <summary>"SetSequencePosition" → "Set sequence position"; property setters lose "set_".</summary>
        private static string Nicify(string name)
        {
            if (name.StartsWith("set_")) name = "Set" + name.Substring(4);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) { sb.Append(' '); sb.Append(char.ToLowerInvariant(c)); }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
