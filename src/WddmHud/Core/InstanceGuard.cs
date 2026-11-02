// =============================================================================
// WDDM HUD by pascalkeeper — Single-instance + ghost / old-version eradication
//
// Goals:
//  - Never stack multiple UI overlays or tray icons
//  - Kill leftover WddmHud.exe from older builds / other paths before starting
//  - Separate UI vs sensor-bridge process roles
//  - Handle abandoned mutexes after crash/kill
//  - Leave a PID state file for diagnostics; purge on clean exit
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;

namespace WddmHud.Core
{
    public enum InstanceRole
    {
        Ui,
        SensorBridge
    }

    public sealed class InstanceGuard : IDisposable
    {
        // Global\ so elevated bridge and standard UI sessions share the same namespace
        // Version suffix bumps if mutex semantics ever change (prevents stale contracts).
        public const string UiMutexName = @"Global\WDDM_HUD_by_pascalkeeper_UI_v2";
        public const string BridgeMutexName = @"Global\WDDM_HUD_by_pascalkeeper_BRIDGE_v2";

        private readonly InstanceRole _role;
        private Mutex? _mutex;
        private bool _ownsMutex;
        private bool _disposed;
        private readonly string _statePath;

        public bool IsPrimary { get; private set; }

        private InstanceGuard(InstanceRole role, Mutex? mutex, bool ownsMutex, bool isPrimary)
        {
            _role = role;
            _mutex = mutex;
            _ownsMutex = ownsMutex;
            IsPrimary = isPrimary;
            _statePath = GetStatePath(role);
        }

        public static string GetStatePath(InstanceRole role)
        {
            string name = role == InstanceRole.Ui ? "run_state_ui.json" : "run_state_bridge.json";
            return Path.Combine(AppPaths.ConfigDirectory, name);
        }

        /// <summary>
        /// Acquire exclusive ownership for this role. Terminates peer/ghost processes first.
        /// </summary>
        public static InstanceGuard Acquire(InstanceRole role, bool killPeers = true)
        {
            if (killPeers)
            {
                int killed = TerminatePeerProcesses(role, includeOtherRole: false);
                if (killed > 0)
                    Logger.Info($"InstanceGuard: terminated {killed} peer process(es) for role={role}");

                // Also kill same-name binaries running from a *different path* even if CLI unclear
                // (old version remnants after republish while previous still running).
                int oldKilled = TerminateForeignPathInstances(role);
                if (oldKilled > 0)
                    Logger.Info($"InstanceGuard: terminated {oldKilled} foreign-path / old-version instance(s)");

                Thread.Sleep(250);
            }

            string mutexName = role == InstanceRole.Ui ? UiMutexName : BridgeMutexName;
            Mutex? mutex = null;
            bool owns = false;
            bool primary = false;

            try
            {
                mutex = new Mutex(true, mutexName, out bool createdNew);
                if (createdNew)
                {
                    owns = true;
                    primary = true;
                }
                else
                {
                    // Existing mutex — try immediate take; AbandonedMutexException means crash leftover
                    try
                    {
                        if (mutex.WaitOne(0))
                        {
                            owns = true;
                            primary = true;
                            Logger.Info("InstanceGuard: acquired existing mutex (previous owner released).");
                        }
                    }
                    catch (AbandonedMutexException)
                    {
                        owns = true;
                        primary = true;
                        Logger.Warn("InstanceGuard: acquired ABANDONED mutex (ghost/crash cleanup).");
                    }

                    if (!primary)
                    {
                        // Still held by a live process — last resort: kill all peers again and retry once
                        Logger.Warn("InstanceGuard: mutex busy; force-killing remaining peers and retrying.");
                        TerminatePeerProcesses(role, includeOtherRole: false);
                        Thread.Sleep(400);
                        try
                        {
                            if (mutex.WaitOne(500))
                            {
                                owns = true;
                                primary = true;
                            }
                        }
                        catch (AbandonedMutexException)
                        {
                            owns = true;
                            primary = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("InstanceGuard: mutex create/acquire failed", ex);
                try { mutex?.Dispose(); } catch { }
                mutex = null;
                // Fall through as primary so user is not bricked if mutex APIs fail
                primary = true;
                owns = false;
            }

            var guard = new InstanceGuard(role, mutex, owns, primary);
            if (primary)
            {
                guard.WriteStateFile();
                guard.PurgeStaleArtifacts();
            }

            return guard;
        }

        /// <summary>
        /// Kill other WddmHud.exe processes matching this role (or all HUD processes).
        /// Never kills the current process.
        /// </summary>
        public static int TerminatePeerProcesses(InstanceRole role, bool includeOtherRole)
        {
            int self = Environment.ProcessId;
            int killed = 0;

            foreach (var info in EnumerateWddmHudProcesses())
            {
                if (info.Pid == self)
                    continue;

                bool isBridge = info.IsSensorBridge;
                // An elevated bridge often has a blank command line from here.
                // Do not treat that blank as a second HUD and kill it.
                bool isUi = !isBridge && info.CommandLine.IndexOf("WddmHud", StringComparison.OrdinalIgnoreCase) >= 0
                    && info.CommandLine.IndexOf("sensor-bridge", StringComparison.OrdinalIgnoreCase) < 0;

                bool match =
                    includeOtherRole
                    || (role == InstanceRole.Ui && isUi)
                    || (role == InstanceRole.SensorBridge && isBridge);

                if (!match)
                    continue;

                if (TryKill(info.Pid, $"peer role={role} path={info.Path}"))
                    killed++;
            }

            return killed;
        }

        /// <summary>
        /// Kill WddmHud.exe whose image path is not this process's image path
        /// (classic "old version still running after rebuild" ghost).
        /// For UI role: kills other UI instances from any path.
        /// </summary>
        public static int TerminateForeignPathInstances(InstanceRole role)
        {
            int self = Environment.ProcessId;
            string selfPath = NormalizePath(Environment.ProcessPath ?? AppContext.BaseDirectory);
            int killed = 0;

            foreach (var info in EnumerateWddmHudProcesses())
            {
                if (info.Pid == self)
                    continue;

                bool isBridge = info.IsSensorBridge;
                if (role == InstanceRole.Ui && (isBridge || string.IsNullOrEmpty(info.CommandLine)))
                    continue;
                if (role == InstanceRole.SensorBridge && !isBridge)
                    continue;

                string other = NormalizePath(info.Path);
                // Different path OR empty path (unknown) → treat as remnant
                if (string.IsNullOrEmpty(other) ||
                    !string.Equals(other, selfPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryKill(info.Pid, $"foreign/old path='{info.Path}' self='{selfPath}'"))
                        killed++;
                }
            }

            return killed;
        }

        /// <summary>
        /// Nuclear option used by installer/uninstall/cleanup scripts.
        /// Kills every WddmHud.exe (UI + bridge) except optional keepPid.
        /// </summary>
        public static int TerminateAllExcept(int keepPid = -1)
        {
            int killed = 0;
            foreach (var info in EnumerateWddmHudProcesses())
            {
                if (info.Pid == keepPid || info.Pid == Environment.ProcessId)
                    continue;
                if (TryKill(info.Pid, "terminate-all"))
                    killed++;
            }
            return killed;
        }

        private static bool TryKill(int pid, string reason)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                Logger.Info($"InstanceGuard: killing PID {pid} ({reason})");
                p.Kill(entireProcessTree: true);
                if (!p.WaitForExit(3000))
                {
                    Logger.Warn($"InstanceGuard: PID {pid} did not exit within 3s");
                }
                return true;
            }
            catch (ArgumentException)
            {
                return false; // already gone
            }
            catch (Exception ex)
            {
                Logger.Warn($"InstanceGuard: failed to kill PID {pid}: {ex.Message}");
                return false;
            }
        }

        private sealed class ProcInfo
        {
            public int Pid;
            public string Path = "";
            public string CommandLine = "";
            public bool IsSensorBridge;
        }

        private static List<ProcInfo> EnumerateWddmHudProcesses()
        {
            var list = new List<ProcInfo>();
            try
            {
                // WMI gives CommandLine (Process.GetProcesses does not)
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'WddmHud.exe'");
                foreach (ManagementObject mo in searcher.Get())
                {
                    try
                    {
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        string path = mo["ExecutablePath"]?.ToString() ?? "";
                        string cmd = mo["CommandLine"]?.ToString() ?? "";
                        bool bridge = cmd.IndexOf("sensor-bridge", StringComparison.OrdinalIgnoreCase) >= 0;
                        list.Add(new ProcInfo
                        {
                            Pid = pid,
                            Path = path,
                            CommandLine = cmd,
                            IsSensorBridge = bridge
                        });
                    }
                    catch { /* skip row */ }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("InstanceGuard: WMI process enum failed, fallback GetProcessesByName: " + ex.Message);
                try
                {
                    foreach (var p in Process.GetProcessesByName("WddmHud"))
                    {
                        try
                        {
                            list.Add(new ProcInfo
                            {
                                Pid = p.Id,
                                Path = SafeGetPath(p),
                                CommandLine = "",
                                IsSensorBridge = false // unknown — treat as UI for kill purposes when role=Ui
                            });
                        }
                        finally
                        {
                            p.Dispose();
                        }
                    }
                }
                catch { /* ignore */ }
            }

            return list;
        }

        private static string SafeGetPath(Process p)
        {
            try { return p.MainModule?.FileName ?? ""; }
            catch { return ""; }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return "";
                return Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch
            {
                return path?.Trim() ?? "";
            }
        }

        private void WriteStateFile()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                string json =
                    "{\n" +
                    $"  \"role\": \"{_role}\",\n" +
                    $"  \"pid\": {Environment.ProcessId},\n" +
                    $"  \"path\": \"{EscapeJson(Environment.ProcessPath ?? "")}\",\n" +
                    $"  \"base_dir\": \"{EscapeJson(AppContext.BaseDirectory)}\",\n" +
                    $"  \"started_utc\": \"{DateTime.UtcNow:o}\",\n" +
                    $"  \"version\": \"{EscapeJson(typeof(InstanceGuard).Assembly.GetName().Version?.ToString() ?? "1.0.0")}\"\n" +
                    "}\n";
                File.WriteAllText(_statePath, json);
            }
            catch (Exception ex)
            {
                Logger.Warn("InstanceGuard: state file write failed: " + ex.Message);
            }
        }

        private void ClearStateFile()
        {
            try
            {
                if (File.Exists(_statePath))
                    File.Delete(_statePath);
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Remove LHM .sys droppings, stale bridge JSON if no bridge alive, stale state files.
        /// </summary>
        public void PurgeStaleArtifacts()
        {
            PurgeStaleArtifactsStatic(forUiRole: _role == InstanceRole.Ui);
        }

        /// <summary>
        /// Static purge for --kill-all without holding a long-lived mutex.
        /// </summary>
        public static void PurgeStaleArtifactsStatic(bool forUiRole = true)
        {
            try
            {
                // Kernel driver artifacts extracted next to exe by LHM — not wanted as remnants
                string dist = AppPaths.DistDirectory;
                string baseDir = AppContext.BaseDirectory;
                foreach (string dir in new[] { dist, baseDir, AppPaths.Root })
                {
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        foreach (string sys in Directory.GetFiles(dir, "*.sys"))
                        {
                            try
                            {
                                string fileName = Path.GetFileName(sys);
                                if (fileName.StartsWith("WinRing0", StringComparison.OrdinalIgnoreCase)
                                    || fileName.Equals("WddmHud.sys", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                File.Delete(sys);
                                Logger.Info("InstanceGuard: removed leftover driver file " + sys);
                            }
                            catch (Exception ex)
                            {
                                Logger.Debug("Could not delete " + sys + ": " + ex.Message);
                            }
                        }
                    }
                    catch { /* ignore dir */ }
                }

                // Stale live_sensors.json if no bridge process
                bool bridgeAlive = false;
                foreach (var p in EnumerateWddmHudProcesses())
                {
                    if (p.IsSensorBridge && p.Pid != Environment.ProcessId)
                    {
                        bridgeAlive = true;
                        break;
                    }
                }

                if (!bridgeAlive && forUiRole)
                {
                    string sensors = Path.Combine(AppPaths.ConfigDirectory, "live_sensors.json");
                    try
                    {
                        if (File.Exists(sensors))
                        {
                            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(sensors);
                            if (age.TotalSeconds > 10)
                            {
                                File.Delete(sensors);
                                Logger.Info("InstanceGuard: removed stale live_sensors.json (no bridge).");
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                // Stale state files pointing at dead PIDs
                PurgeDeadStateFile(GetStatePath(InstanceRole.Ui));
                PurgeDeadStateFile(GetStatePath(InstanceRole.SensorBridge));
            }
            catch (Exception ex)
            {
                Logger.Warn("InstanceGuard: PurgeStaleArtifacts: " + ex.Message);
            }
        }

        private static void PurgeDeadStateFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string text = File.ReadAllText(path);
                // crude pid parse
                int idx = text.IndexOf("\"pid\"", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) { File.Delete(path); return; }
                int colon = text.IndexOf(':', idx);
                if (colon < 0) { File.Delete(path); return; }
                string num = "";
                for (int i = colon + 1; i < text.Length; i++)
                {
                    char c = text[i];
                    if (char.IsDigit(c)) num += c;
                    else if (num.Length > 0) break;
                }
                if (int.TryParse(num, out int pid))
                {
                    try
                    {
                        Process.GetProcessById(pid);
                        // still alive — keep
                        return;
                    }
                    catch
                    {
                        File.Delete(path);
                        Logger.Info($"InstanceGuard: removed dead state file (pid {pid}) {path}");
                    }
                }
            }
            catch { /* ignore */ }
        }

        private static string EscapeJson(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                ClearStateFile();
                if (_ownsMutex && _mutex != null)
                {
                    try { _mutex.ReleaseMutex(); } catch { }
                }
                _mutex?.Dispose();
                _mutex = null;
            }
            catch { /* ignore */ }
        }
    }
}
