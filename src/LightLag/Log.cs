using UnityEngine;

namespace LightLag
{
    /// <summary>Log lines are prefixed with <c>[LightLag]</c>, which the test harness matches.</summary>
    internal static class Log
    {
        private const string Prefix = "[LightLag] ";

        internal static void Info(string message) => Debug.Log(Prefix + message);
        internal static void Warn(string message) => Debug.LogWarning(Prefix + message);
        internal static void Error(string message) => Debug.LogError(Prefix + message);
    }
}
