// =============================================================================
// WDDM HUD by pascalkeeper — Application path resolution
// All runtime state lives under F:\imonlinegaming\WDDM_HUD_by_pascalkeeper when
// installed there; falls back to %LOCALAPPDATA%\WddmHud_by_pascalkeeper if the
// install root is missing (portable-safe).
// =============================================================================

using System;
using System.IO;

namespace WddmHud.Core
{
    /// <summary>
    /// Resolves install, config, and log directories with elite defensive fallbacks.
    /// Never throws on path construction; callers still validate writes.
    /// </summary>
    public static class AppPaths
    {
        public const string PreferredInstallRoot = @"F:\imonlinegaming\WDDM_HUD_by_pascalkeeper";
        public const string AppFolderName = "WddmHud_by_pascalkeeper";
        public const string ConfigFileName = "hud_config.json";
        public const string LogFileName = "wddm_hud.log";

        private static string? _root;
        private static string? _configDir;
        private static string? _logDir;

        /// <summary>
        /// Preferred F: install root if it exists; otherwise LocalAppData fallback.
        /// </summary>
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_root))
                    return _root!;

                try
                {
                    if (Directory.Exists(PreferredInstallRoot))
                    {
                        _root = PreferredInstallRoot;
                        return _root;
                    }
                }
                catch
                {
                    // ignored — fall through
                }

                string local = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppFolderName);

                try
                {
                    Directory.CreateDirectory(local);
                }
                catch
                {
                    // last resort: exe directory
                    local = AppContext.BaseDirectory;
                }

                _root = local;
                return _root;
            }
        }

        public static string ConfigDirectory
        {
            get
            {
                if (_configDir != null)
                    return _configDir;
                _configDir = Path.Combine(Root, "config");
                try { Directory.CreateDirectory(_configDir); } catch { /* best effort */ }
                return _configDir;
            }
        }

        public static string LogDirectory
        {
            get
            {
                if (_logDir != null)
                    return _logDir;
                _logDir = Path.Combine(Root, "logs");
                try { Directory.CreateDirectory(_logDir); } catch { /* best effort */ }
                return _logDir;
            }
        }

        public static string ConfigFilePath => Path.Combine(ConfigDirectory, ConfigFileName);
        public static string LogFilePath => Path.Combine(LogDirectory, LogFileName);
        public static string HardwareSnapshotPath => Path.Combine(ConfigDirectory, "hardware_snapshot.json");
        public static string DistDirectory => Path.Combine(Root, "dist");
    }
}
