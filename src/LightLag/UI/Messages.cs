namespace LightLag
{
    /// <summary>On-screen messages about commands.</summary>
    internal static class Messages
    {
        public static void Post(string text) => ScreenMessages.PostScreenMessage(text, 4f, ScreenMessageStyle.UPPER_CENTER);
    }
}
