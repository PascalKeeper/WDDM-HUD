// =============================================================================
// WDDM HUD by pascalkeeper — System tray dock / minimize / quick actions
// =============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;
using WddmHud.Core;

namespace WddmHud.UI
{
    public sealed class TrayController : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly OverlayForm _overlay;
        private bool _disposed;

        public event Action? ExitRequested;
        public event Action? RefreshHardwareRequested;
        public event Action? ToggleClickThroughRequested;

        public TrayController(OverlayForm overlay)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));

            _icon = new NotifyIcon
            {
                Text = "WDDM HUD by pascalkeeper",
                Icon = SystemIcons.Application,
                Visible = false
            };

            // Build a simple green-dot icon at runtime (no external .ico dependency)
            try
            {
                Icon? glyph = CreateGlyphIcon(Color.Lime);
                if (glyph != null)
                    _icon.Icon = glyph;
            }
            catch (Exception ex)
            {
                Logger.Warn("Tray glyph icon failed; using SystemIcons.Application: " + ex.Message);
                _icon.Icon = SystemIcons.Application;
            }

            // Visible only after icon is assigned — avoids tray NRE edge cases
            _icon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show HUD", null, (_, _) => ShowOverlay());
            menu.Items.Add("Hide HUD", null, (_, _) => _overlay.Hide());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Toggle click-through", null, (_, _) => ToggleClickThroughRequested?.Invoke());
            menu.Items.Add("Refresh hardware detect", null, (_, _) => RefreshHardwareRequested?.Invoke());
            menu.Items.Add("Enable CPU sensors (admin once)…", null, (_, _) => TryStartSensorBridge());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open install folder", null, (_, _) => OpenFolder(AppPaths.Root));
            menu.Items.Add("Open logs", null, (_, _) => OpenFolder(AppPaths.LogDirectory));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (_, _) => ShowOverlay();
            _icon.BalloonTipTitle = "WDDM HUD by pascalkeeper";
        }

        public void ShowBalloon(string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            try
            {
                _icon.BalloonTipText = text;
                _icon.BalloonTipIcon = icon;
                _icon.ShowBalloonTip(2500);
            }
            catch
            {
                // balloon optional
            }
        }

        public void SetStatusTooltip(string status)
        {
            try
            {
                string t = "WDDM HUD | " + status;
                if (t.Length > 63) t = t.Substring(0, 63);
                _icon.Text = t;
            }
            catch { /* NotifyIcon text limit */ }
        }

        private void ShowOverlay()
        {
            try
            {
                _overlay.Show();
                _overlay.ForceTopMost();
            }
            catch (Exception ex)
            {
                Logger.Error("ShowOverlay failed", ex);
            }
        }

        private static void OpenFolder(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Error("OpenFolder " + path, ex);
            }
        }

        /// <summary>
        /// Starts an elevated copy of this EXE with --sensor-bridge so CPU package
        /// temps can use the MSR/kernel path. Main HUD stays non-elevated.
        /// HD 630 typically has no iGPU temperature sensor — bridge still reports load.
        /// </summary>
        private void TryStartSensorBridge()
        {
            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !System.IO.File.Exists(exe))
                    exe = System.IO.Path.Combine(AppContext.BaseDirectory, "WddmHud.exe");

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--sensor-bridge",
                    UseShellExecute = true,
                    Verb = "runas", // UAC elevation prompt — user may cancel
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
                };
                System.Diagnostics.Process.Start(psi);
                ShowBalloon("Sensor bridge elevation requested.\nApprove UAC for CPU package temps.\n(iGPU temp is often unavailable on HD 630.)");
                Logger.Info("Sensor bridge elevation launched: " + exe);
            }
            catch (Exception ex)
            {
                Logger.Error("Sensor bridge start failed (UAC cancelled or blocked)", ex);
                ShowBalloon("Sensor bridge not started (UAC cancelled or blocked).", ToolTipIcon.Warning);
            }
        }

        private static Icon? CreateGlyphIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(0, 0, 0, 0));
                using var b = new SolidBrush(color);
                g.FillEllipse(b, 2, 2, 12, 12);
                using var pen = new Pen(Color.FromArgb(30, 30, 30), 1);
                g.DrawEllipse(pen, 2, 2, 12, 12);
            }

            // Icon.FromHandle does not take ownership of the HICON in a dispose-safe way
            // across all runtimes. Convert via stream instead.
            using var ms = new System.IO.MemoryStream();
            // NotifyIcon wants multi-size .ico; for simplicity use bitmap handle clone:
            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using Icon temp = Icon.FromHandle(hIcon);
                return (Icon)temp.Clone();
            }
            finally
            {
                // Destroy the temporary HICON from GetHicon
                try { DestroyIcon(hIcon); } catch { /* ignore */ }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch { /* ignore */ }
        }
    }
}
