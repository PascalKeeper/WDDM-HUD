// =============================================================================
// WDDM HUD by pascalkeeper — Thread-safe rotating file logger
// Zero network. Failures never crash the overlay.
// =============================================================================

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace WddmHud.Core
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// Lightweight, lock-based file logger with size-based rotation.
    /// </summary>
    public static class Logger
    {
        private static readonly object Sync = new object();
        private static LogLevel _minLevel = LogLevel.Info;
        private const long MaxBytes = 2 * 1024 * 1024; // 2 MB

        public static void SetMinLevel(LogLevel level) => _minLevel = level;

        public static void Debug(string message) => Write(LogLevel.Debug, message);
        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warn(string message) => Write(LogLevel.Warn, message);
        public static void Error(string message) => Write(LogLevel.Error, message);

        public static void Error(string message, Exception ex)
        {
            // Always capture full stack for launch diagnostics
            string detail = ex.GetType().Name + ": " + ex.Message;
            if (!string.IsNullOrEmpty(ex.StackTrace))
                detail += Environment.NewLine + ex.StackTrace;
            if (ex.InnerException != null)
                detail += Environment.NewLine + "Inner: " + ex.InnerException;
            Write(LogLevel.Error, message + " | " + detail);
        }

        private static void Write(LogLevel level, string message)
        {
            if (level < _minLevel)
                return;

            string line = string.Format(
                "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [T{2}] {3}",
                DateTime.Now,
                level.ToString().ToUpperInvariant(),
                Thread.CurrentThread.ManagedThreadId,
                message);

            try
            {
                lock (Sync)
                {
                    string path = AppPaths.LogFilePath;
                    RotateIfNeeded(path);
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take down the HUD.
            }

#if DEBUG
            try { System.Diagnostics.Debug.WriteLine(line); } catch { }
#endif
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;
                var fi = new FileInfo(path);
                if (fi.Length < MaxBytes)
                    return;
                string bak = path + ".1";
                if (File.Exists(bak))
                    File.Delete(bak);
                File.Move(path, bak);
            }
            catch
            {
                // rotation best-effort
            }
        }
    }
}
