// =============================================================================
// WDDM HUD by pascalkeeper — System tray dock / minimize / quick actions
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
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
        private readonly ContextMenuStrip _menu;
        private bool _disposed;
        private bool _allowMenuClose;
        private DateTime _menuOpenedUtc;

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
                Icon? glyph = CreateGlyphIcon(_overlay.CurrentAccent);
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

            var menu = new ContextMenuStrip
            {
                RenderMode = ToolStripRenderMode.System,
                ShowImageMargin = false,
                BackColor = SystemColors.Menu,
                ForeColor = SystemColors.MenuText
            };
            _menu = menu;
            menu.Items.Add("Minimize to tray", null, (_, _) => _overlay.Hide());
            menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show HUD", null, (_, _) => ShowOverlay());
            menu.Items.Add(new ToolStripSeparator());

            var runOnStartup = new ToolStripMenuItem("Run on startup");
            var doNotRunOnStartup = new ToolStripMenuItem("Do not run on startup");
            void RefreshStartupMarks()
            {
                bool on = LaunchRegistration.IsRunAtStartup();
                runOnStartup.Checked = on;
                doNotRunOnStartup.Checked = !on;
            }
            runOnStartup.Click += (_, _) =>
            {
                try
                {
                    LaunchRegistration.EnableRunAtStartup(LaunchRegistration.ResolveExePath());
                    RefreshStartupMarks();
                    ShowBalloon("WDDM HUD will open when you sign in.");
                }
                catch (Exception ex)
                {
                    Logger.Error("Run on startup failed", ex);
                    ShowBalloon("Could not turn on sign-in launch.", ToolTipIcon.Warning);
                }
            };
            doNotRunOnStartup.Click += (_, _) =>
            {
                try
                {
                    LaunchRegistration.DisableRunAtStartup();
                    RefreshStartupMarks();
                    ShowBalloon("WDDM HUD will not open when you sign in.");
                }
                catch (Exception ex)
                {
                    Logger.Error("Do not run on startup failed", ex);
                    ShowBalloon("Could not turn off sign-in launch.", ToolTipIcon.Warning);
                }
            };
            menu.Items.Add(runOnStartup);
            menu.Items.Add(doNotRunOnStartup);
            menu.Items.Add("Change color", null, (_, _) => _overlay.OpenColorWheel(Cursor.Position));
            var fahrenheit = new ToolStripMenuItem("Fahrenheit");
            void RefreshFahrenheitMark()
            {
                fahrenheit.Checked = _overlay.UseFahrenheit;
            }
            fahrenheit.Click += (_, _) =>
            {
                _overlay.ToggleFahrenheit();
                RefreshFahrenheitMark();
            };
            menu.Items.Add(fahrenheit);
            menu.Opening += (_, _) =>
            {
                RefreshStartupMarks();
                RefreshFahrenheitMark();
            };
            menu.Opened += (_, _) =>
            {
                _menuOpenedUtc = DateTime.UtcNow;
                Logger.Info("Tray menu open. items=" + menu.Items.Count + " at " + menu.Bounds.X + "," + menu.Bounds.Y);
            };
            menu.Closing += OnMenuClosing;
            menu.ItemClicked += (_, _) => { _allowMenuClose = true; };
            RefreshStartupMarks();
            RefreshFahrenheitMark();
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Toggle click-through (play through HUD)", null, (_, _) => ToggleClickThroughRequested?.Invoke());
            menu.Items.Add("Refresh hardware detect", null, (_, _) => RefreshHardwareRequested?.Invoke());
            menu.Items.Add("EC CPU temperature…", null, (_, _) => StartEcBridge());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open install folder", null, (_, _) => OpenFolder(AppPaths.Root));
            menu.Items.Add("Open logs", null, (_, _) => OpenFolder(AppPaths.LogDirectory));

            // The built-in right-click opens this menu and the no-activate HUD
            // immediately takes it back down. Show it ourselves instead.
            _icon.MouseUp += (_, e) =>
            {
                if (_disposed) return;
                if (e.Button == MouseButtons.Right)
                    ShowMenu();
                else if (e.Button == MouseButtons.Left && _menu.Visible)
                {
                    _allowMenuClose = true;
                    _menu.Close();
                }
            };
            _icon.DoubleClick += (_, _) => ShowOverlay();
            _icon.BalloonTipTitle = "WDDM HUD by pascalkeeper";
            Logger.Info("Tray menu ready. items=" + _menu.Items.Count);
        }

        private void ShowMenu()
        {
            try
            {
                _allowMenuClose = false;
                _menuOpenedUtc = DateTime.UtcNow;
                if (_menu.Visible)
                {
                    _allowMenuClose = true;
                    _menu.Close();
                    return;
                }
                _menu.Show(Cursor.Position);
            }
            catch (Exception ex)
            {
                Logger.Error("Tray menu failed", ex);
            }
        }

        private void OnMenuClosing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (_allowMenuClose)
                return;
            if (e.CloseReason != ToolStripDropDownCloseReason.AppFocusChange)
                return;
            bool justOpened = (DateTime.UtcNow - _menuOpenedUtc).TotalMilliseconds < 700;
            bool overMenu = _menu.Bounds.Contains(Cursor.Position);
            if (justOpened || overMenu)
                e.Cancel = true;
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
        /// Starts an elevated copy of this EXE with --sensor-bridge. The bridge
        /// reads sensors through the granted WinRing0 driver, and falls back to
        /// the EC temperature if that driver does not open. It does not set fan
        /// speed. Main HUD stays non-elevated.
        /// </summary>
        public void StartEcBridge()
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
                    Verb = "runas",
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
                };
                System.Diagnostics.Process.Start(psi);
                ShowBalloon("Approve UAC once. The bridge reads the EC CPU temperature only. It does not load a driver and it does not change fan speed.");
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

        /// <summary>
        /// Explorer keeps a tray icon after a process is killed. Moving the
        /// pointer across the tray toolbars drops icons whose process is gone.
        /// </summary>
        public static void DismissGhostIcons()
        {
            try
            {
                IntPtr tray = FindWindow("Shell_TrayWnd", null);
                IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
                IntPtr pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
                SweepToolbar(FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null));
                SweepToolbar(FindWindowEx(notify, IntPtr.Zero, "ToolbarWindow32", null));

                IntPtr overflow = FindWindow("NotifyIconOverflowWindow", null);
                SweepToolbar(FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null));
            }
            catch (Exception ex)
            {
                Logger.Debug("Tray ghost sweep failed: " + ex.Message);
            }
        }

        private static void SweepToolbar(IntPtr toolbar)
        {
            if (toolbar == IntPtr.Zero)
                return;
            if (!GetClientRect(toolbar, out RECT rect))
                return;
            for (int y = 0; y < rect.Bottom; y += 4)
            {
                for (int x = 0; x < rect.Right; x += 4)
                    SendMessage(toolbar, 0x0200, IntPtr.Zero, (IntPtr)((y << 16) | (x & 0xFFFF)));
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? className, string? windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _allowMenuClose = true;
                if (_menu.Visible)
                    _menu.Close();
                _icon.Visible = false;
                _icon.Dispose();
                _menu.Dispose();
            }
            catch { /* ignore */ }
        }
    }
}
