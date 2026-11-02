// =============================================================================
// WDDM HUD by pascalkeeper
// Lightweight hybrid-graphics performance overlay for Windows 10.
// Offline · Zero telemetry · Auto hardware detect · Tray dock · Always-on status
//
// Copyright (C) 2026 Joseph Rocco Peransi / pascalkeeper
// =============================================================================

using System;
using System.Windows.Forms;
using WddmHud.Core;
using WddmHud.Hardware;
using WddmHud.UI;

namespace WddmHud
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool isBridge = Array.Exists(args, a =>
                string.Equals(a, "--sensor-bridge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, "-sensor-bridge", StringComparison.OrdinalIgnoreCase));

            // --kill-all: cleanup utility (installer / scripts / emergency)
            if (Array.Exists(args, a =>
                    string.Equals(a, "--kill-all", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, "-kill-all", StringComparison.OrdinalIgnoreCase)))
            {
                int n = InstanceGuard.TerminateAllExcept(Environment.ProcessId);
                InstanceGuard.PurgeStaleArtifactsStatic();
                TrayController.DismissGhostIcons();
                Logger.Info($"--kill-all complete; terminated {n} instance(s)");
                Environment.ExitCode = 0;
                return;
            }

            // Elevated sensor bridge — exclusive bridge mutex; never stacks bridges
            if (isBridge)
            {
                using var bridgeGuard = InstanceGuard.Acquire(InstanceRole.SensorBridge, killPeers: true);
                if (!bridgeGuard.IsPrimary)
                {
                    Logger.Warn("Sensor bridge: another bridge still owns the mutex after cleanup; exiting.");
                    Environment.ExitCode = 3;
                    return;
                }
                Environment.ExitCode = SensorBridgeHost.Run();
                return;
            }

            // UI: kill ghosts/old versions, then exclusive UI mutex
            using var uiGuard = InstanceGuard.Acquire(InstanceRole.Ui, killPeers: true);
            if (!uiGuard.IsPrimary)
            {
                try
                {
                    MessageBox.Show(
                        "WDDM HUD is already running (could not replace the live instance).\n" +
                        "Check the system tray, or run:\n" +
                        "  WddmHud.exe --kill-all\n" +
                        "then launch again.",
                        "WDDM HUD by pascalkeeper",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch { /* headless */ }
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (_, e) =>
            {
                Logger.Error("UI thread exception", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Logger.Error("Unhandled domain exception", ex);
                else
                    Logger.Error("Unhandled domain exception: " + e.ExceptionObject);
            };

            Logger.Info("========== WDDM HUD starting ==========");
            Logger.Info("BaseDirectory=" + AppContext.BaseDirectory);
            Logger.Info("ProcessPath=" + (Environment.ProcessPath ?? ""));
            Logger.Info("Root=" + AppPaths.Root);
            Logger.Info("PID=" + Environment.ProcessId);

            try
            {
                RunApp(args);
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal startup failure", ex);
                try
                {
                    MessageBox.Show(
                        "WDDM HUD failed to start:\n" + ex.GetType().Name + ": " + ex.Message +
                        "\n\nFull stack written to:\n" + AppPaths.LogFilePath +
                        "\n\nTip: run dist\\WddmHud.exe --kill-all then launch again.",
                        "WDDM HUD by pascalkeeper",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch { }
            }
            finally
            {
                Logger.Info("========== WDDM HUD exiting ==========");
            }
        }

        private static bool SensorFileIsFresh()
        {
            try
            {
                string path = SensorBridgeFile.Path;
                if (!File.Exists(path))
                    return false;
                return (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds < 5;
            }
            catch
            {
                return false;
            }
        }

        private static void RunApp(string[] args)
        {
            bool startHidden = Array.Exists(args, a =>
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, "-tray", StringComparison.OrdinalIgnoreCase));

            TrayController.DismissGhostIcons();
            Logger.Info("RunApp: loading config");
            HudConfig config = HudConfig.Load();
            if (config == null)
                throw new InvalidOperationException("HudConfig.Load returned null");

            Logger.Info("RunApp: detecting hardware");
            HardwareSnapshot hw = HardwareDetector.Detect(forceRefresh: true);
            if (hw == null)
                throw new InvalidOperationException("HardwareDetector.Detect returned null");

            Logger.Info("RunApp: creating MetricsEngine");
            using var metricsEngine = new MetricsEngine(hw);

            Logger.Info("RunApp: creating OverlayForm");
            var overlay = new OverlayForm(config);

            Logger.Info("RunApp: creating TrayController");
            var tray = new TrayController(overlay);

            // Seed first paint with detection summary
            Logger.Info("RunApp: seeding overlay metrics");
            try
            {
                overlay.ApplyMetrics(new LiveMetrics
                {
                    StatusGlyph = "●",
                    StatusText = hw.HasHybridGraphics ? "HYBRID OK" : "ACTIVE",
                    StatusDetail = hw.Summary ?? "",
                    CpuName = hw.CpuName ?? "",
                    IgpuName = hw.PrimaryIgpuName ?? "N/A",
                    DgpuName = hw.PrimaryDgpuName ?? "N/A",
                    HybridDetected = hw.HasHybridGraphics,
                    OnAcPower = hw.OnAcPower,
                    SystemRamTotalGiB = hw.SystemRamBytes / (1024.0 * 1024.0 * 1024.0),
                    FpsSource = "warming up"
                }, config);
            }
            catch (Exception ex)
            {
                Logger.Error("Seed ApplyMetrics failed", ex);
            }

            try
            {
                tray.SetStatusTooltip(hw.HasHybridGraphics ? "Hybrid OK" : "Active");
                tray.ShowBalloon("Hardware detected.\n" + Truncate(hw.Summary ?? "", 120));
            }
            catch (Exception ex)
            {
                Logger.Error("Tray status/balloon failed", ex);
            }

            var pollTimer = new System.Windows.Forms.Timer
            {
                Interval = Math.Clamp(config.PollIntervalMs, 250, 5000)
            };
            LiveMetrics? latest = null;

            pollTimer.Tick += (_, _) =>
            {
                try
                {
                    LiveMetrics sample = metricsEngine.Sample();
                    latest = sample;
                    // Refresh AC status occasionally from snapshot cache (cheap)
                    sample.OnAcPower = HardwareDetector.Detect(forceRefresh: false).OnAcPower;
                    sample.HybridDetected = hw.HasHybridGraphics;
                    if (string.IsNullOrEmpty(sample.IgpuName) || sample.IgpuName == "N/A")
                        sample.IgpuName = hw.PrimaryIgpuName;
                    if (string.IsNullOrEmpty(sample.DgpuName) || sample.DgpuName == "N/A")
                        sample.DgpuName = hw.PrimaryDgpuName;

                    overlay.ApplyMetrics(sample, config);
                    tray.SetStatusTooltip(sample.StatusText + " | dGPU " +
                        (sample.DgpuTempC.HasValue ? sample.DgpuTempC.Value.ToString("0") + "C" : "n/a"));
                }
                catch (Exception ex)
                {
                    Logger.Error("Poll tick failed", ex);
                }
            };

            var fanTimer = new System.Windows.Forms.Timer { Interval = 250 };
            fanTimer.Tick += (_, _) =>
            {
                try
                {
                    if (latest == null)
                        return;
                    var bridge = SensorBridgeFile.TryReadFresh(2.0);
                    if (bridge == null)
                        return;
                    int? fan1 = bridge.Fan1Rpm is > 0 and < 20000 ? bridge.Fan1Rpm : null;
                    int? fan2 = bridge.Fan2Rpm is > 0 and < 20000 ? bridge.Fan2Rpm : null;
                    if (fan1 == latest.Fan1Rpm && fan2 == latest.Fan2Rpm)
                        return;
                    latest.Fan1Rpm = fan1;
                    latest.Fan2Rpm = fan2;
                    if (!string.IsNullOrWhiteSpace(bridge.Fan1Name))
                        latest.Fan1Name = bridge.Fan1Name;
                    if (!string.IsNullOrWhiteSpace(bridge.Fan2Name))
                        latest.Fan2Name = bridge.Fan2Name;
                    overlay.ApplyMetrics(latest, config);
                }
                catch (Exception ex)
                {
                    Logger.Error("Fan tick failed", ex);
                }
            };

            void Exit()
            {
                try
                {
                    pollTimer.Stop();
                    fanTimer.Stop();
                    overlay.PersistGeometry();
                    tray.Dispose();
                    TrayController.DismissGhostIcons();
                    overlay.Close();
                }
                catch (Exception ex)
                {
                    Logger.Error("Exit cleanup", ex);
                }
                Application.Exit();
            }

            overlay.ExitRequested += Exit;
            tray.ExitRequested += Exit;

            tray.ToggleClickThroughRequested += () =>
            {
                bool next = !config.ClickThrough;
                overlay.SetClickThrough(next);
                tray.ShowBalloon(next ? "Click-through ON" : "Click-through OFF");
            };
            overlay.ToggleClickThroughRequested += () =>
            {
                bool next = !config.ClickThrough;
                overlay.SetClickThrough(next);
            };

            tray.RefreshHardwareRequested += () =>
            {
                try
                {
                    hw = HardwareDetector.Detect(forceRefresh: true);
                    tray.ShowBalloon("Hardware refreshed.\n" + Truncate(hw.Summary, 120));
                }
                catch (Exception ex)
                {
                    Logger.Error("Hardware refresh failed", ex);
                }
            };

            pollTimer.Start();
            fanTimer.Start();

            if (startHidden || config.StartMinimizedToTray)
            {
                // Do not Show() — tray only
                Logger.Info("Starting minimized to tray");
            }
            else
            {
                overlay.Show();
                overlay.ForceTopMost();
            }

            // UAC must not run before the window exists. A fresh sensor file
            // means the elevated reader is already up, so do not prompt again.
            if (!SensorFileIsFresh())
                ThreadPool.QueueUserWorkItem(_ => tray.StartEcBridge());

            Application.Run();

            pollTimer.Dispose();
            tray.Dispose();
            if (!overlay.IsDisposed)
                overlay.Dispose();
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max - 1) + "…";
        }
    }
}
