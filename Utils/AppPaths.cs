namespace Thexder.Utils
{
    /// <summary>The game's folders, all next to the executable.</summary>
    internal static class AppPaths
    {
        public static string ExecutableDirectory => AppContext.BaseDirectory;

        /// <summary>The game files, rebuilt from the CD image when needed.</summary>
        public static string AssetsDirectory => Path.Combine(ExecutableDirectory, "assets");

        /// <summary>Where the CD image (.iso) goes.</summary>
        public static string IsoDirectory => Path.Combine(ExecutableDirectory, "iso");

        /// <summary>Settings, saved games and logs.</summary>
        public static string DataRoot => Path.Combine(ExecutableDirectory, "data");

        public static string LogDirectory => Path.Combine(DataRoot, "logs");

        public static string SaveDirectory => Path.Combine(DataRoot, "saves");

        public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    }
}
