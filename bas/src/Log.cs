namespace CraftBlade
{
    static class Version { public const string Text = "0.1.0"; }

    /// <summary>Lines go to the game's Player.log with a [CraftBlade] prefix.</summary>
    static class Log
    {
        public static void Info(string s) { UnityEngine.Debug.Log("[CraftBlade] " + s); }
        public static void Warn(string s) { UnityEngine.Debug.LogWarning("[CraftBlade] " + s); }
    }
}
