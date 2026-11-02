// =============================================================================
// WDDM HUD by pascalkeeper — Start Menu shortcut and sign-in launch
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
//
// Sign-in launch is HKCU Run only. No scheduled task. No admin. Default is off.
// =============================================================================

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WddmHud.Core
{
    public static class LaunchRegistration
    {
        public const string RunValueName = "WDDM HUD by pascalkeeper";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string ResolveExePath()
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return exe;

            string beside = Path.Combine(AppContext.BaseDirectory, "WddmHud.exe");
            if (File.Exists(beside))
                return beside;

            return Path.Combine(AppPaths.DistDirectory, "WddmHud.exe");
        }

        public static bool IsRunAtStartup()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return !string.IsNullOrWhiteSpace(key?.GetValue(RunValueName) as string);
            }
            catch (Exception ex)
            {
                Logger.Warn("Run-at-startup read failed: " + ex.Message);
                return false;
            }
        }

        public static void EnableRunAtStartup(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("Exe path is required.", nameof(exePath));

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("HKCU Run key could not be opened.");
            key.SetValue(RunValueName, "\"" + exePath + "\"");
            Logger.Info("Run at startup ON: " + exePath);
        }

        public static void DisableRunAtStartup()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            Logger.Info("Run at startup OFF");
        }

        /// <summary>
        /// Writes Start Menu\Programs\WDDM HUD by pascalkeeper\WDDM HUD.lnk
        /// so the exe opens from the Start Menu. Overwrites a stale target.
        /// </summary>
        public static string EnsureStartMenuShortcut()
        {
            string exe = ResolveExePath();
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string folder = Path.Combine(programs, "WDDM HUD by pascalkeeper");
            Directory.CreateDirectory(folder);
            string linkPath = Path.Combine(folder, "WDDM HUD.lnk");

            Type shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell is not available.");
            object shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("WScript.Shell could not be created.");
            try
            {
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    shell,
                    new object[] { linkPath })
                    ?? throw new InvalidOperationException("CreateShortcut returned nothing.");
                Type shortcutType = shortcut.GetType();
                SetProp(shortcutType, shortcut, "TargetPath", exe);
                string? dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir))
                    SetProp(shortcutType, shortcut, "WorkingDirectory", dir);
                SetProp(shortcutType, shortcut, "Description", "WDDM HUD by pascalkeeper");
                SetProp(shortcutType, shortcut, "WindowStyle", 1);
                shortcutType.InvokeMember(
                    "Save",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    shortcut,
                    args: null);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }

            Logger.Info("Start Menu shortcut: " + linkPath + " -> " + exe);
            return linkPath;
        }

        /// <summary>
        /// Turns sign-in launch on, checks the value, turns it off, then puts back
        /// whatever was there. A failed test does not leave a new startup entry.
        /// </summary>
        public static int RunStartupSelfTest()
        {
            string? previous = null;
            bool needsRestore = false;
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false))
                    previous = key?.GetValue(RunValueName) as string;

                string exe = ResolveExePath();
                EnableRunAtStartup(exe);
                needsRestore = true;

                string? written;
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false))
                    written = key?.GetValue(RunValueName) as string;

                if (!IsRunAtStartup() || written == null ||
                    written.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Logger.Error("Startup self-test: sign-in value did not point at " + exe);
                    return 1;
                }

                DisableRunAtStartup();
                needsRestore = false;
                if (IsRunAtStartup())
                {
                    Logger.Error("Startup self-test: sign-in value still present after disable");
                    needsRestore = true;
                    return 1;
                }

                if (!string.IsNullOrWhiteSpace(previous))
                {
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("HKCU Run key could not be restored.");
                    key.SetValue(RunValueName, previous);
                    Logger.Info("Startup self-test restored the previous sign-in value.");
                }

                Logger.Info("Startup self-test passed. Sign-in launch stays off unless it was already on.");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Startup self-test failed", ex);
                return 1;
            }
            finally
            {
                if (needsRestore)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(previous))
                        {
                            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)!;
                            key.SetValue(RunValueName, previous);
                        }
                        else
                        {
                            DisableRunAtStartup();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Startup self-test could not restore the sign-in value", ex);
                    }
                }
            }
        }

        private static void SetProp(Type type, object target, string name, object value)
        {
            type.InvokeMember(
                name,
                BindingFlags.SetProperty,
                binder: null,
                target,
                new[] { value });
        }
    }
}
