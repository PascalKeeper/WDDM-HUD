// =============================================================================
// WDDM HUD by pascalkeeper
// Lightweight hybrid-graphics performance overlay for Windows 10.
// Offline · Zero telemetry · Auto hardware detect · Tray dock · Always-on status
//
// Copyright (C) 2026 Joseph Rocco Peransi / pascalkeeper
// =============================================================================

using System;
using System.Threading;
using System.Windows.Forms;
using WddmHud.Core;
using WddmHud.Hardware;
using WddmHud.UI;

namespace WddmHud
{
    internal static class Program
    {
        private const string MutexName = "Local\\WDDM_HUD_by_pascalkeeper_v1";

        [STAThread]
        private static void Main(string[] args)
        {
            // Elevated sensor bridge mode — separate from UI instance
            if (Array.Exists(args, a =>
                    string.Equals(a, "--sensor-bridge", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, "-sensor-bridge", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.ExitCode = SensorBridgeHost.Run();
                return;
            }

            // Single instance — prevents stacked overlays and double tray icons
            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    MessageBox.Show(
                        "WDDM HUD is already running.\nCheck the system tray (green status glyph).",
                        "WDDM HUD by pascalkeeper",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch { /* headless edge */ }
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Global exception gates — never silent-death the process without a log
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
            Logger.Info("Root=" + AppPaths.Root);

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
                        "\n\nTip: run dist\\WddmHud.exe again after update.",
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

        private static void RunApp(string[] args)
        {
            bool startHidden = Array.Exists(args, a =>
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, "-tray", StringComparison.OrdinalIgnoreCase));

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

            pollTimer.Tick += (_, _) =>
            {
                try
                {
                    LiveMetrics sample = metricsEngine.Sample();
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

            void Exit()
            {
                try
                {
                    pollTimer.Stop();
                    overlay.PersistGeometry();
                    tray.Dispose();
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
