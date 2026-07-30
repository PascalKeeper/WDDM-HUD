// =============================================================================
// WDDM HUD by pascalkeeper — Clean owner-drawn overlay
// - Only paints metrics that have real values (no N/A clutter)
// - Resize scales content from a fixed design size; top-left stays put
// - RAM temperature removed (no sensor on AW17R4)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WddmHud.Core;
using WddmHud.Hardware;

namespace WddmHud.UI
{
    public sealed class OverlayForm : Form
    {
        /// <summary>Design canvas size — all layout is authored at this resolution then scaled.</summary>
        public const int DesignWidth = 340;
        public const int DesignHeight = 220;

        private readonly HudConfig _config;
        private readonly Panel _resizeGrip;
        private System.Windows.Forms.Timer? _topmostTimer;

        private LiveMetrics? _metrics;
        private HudConfig? _paintConfig;

        private bool _dragging;
        private Point _dragStartCursor;
        private Point _dragStartLoc;
        private bool _resizing;
        private Size _resizeStartSize;
        private Point _resizeStartCursor;

        private float _scale = 1f;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        // Near-black key for transparency (not pure black so green text remains visible)
        private static readonly Color KeyColor = Color.FromArgb(1, 1, 1);
        private static readonly Color TextOk = Color.FromArgb(0, 255, 140);
        private static readonly Color TextWarn = Color.FromArgb(255, 190, 60);
        private static readonly Color TextCrit = Color.FromArgb(255, 90, 90);
        private static readonly Color TextDim = Color.FromArgb(0, 180, 100);
        private static readonly Color PanelFill = Color.FromArgb(18, 22, 18);

        public event Action? ExitRequested;
        public event Action? ToggleClickThroughRequested;

        public OverlayForm(HudConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.Sanitize();
            _paintConfig = _config;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ControlBox = false;
            DoubleBuffered = true;
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            Text = "WDDM HUD by pascalkeeper";

            int w = Math.Clamp(_config.WindowWidth, 200, 900);
            int h = Math.Clamp(_config.WindowHeight, 120, 700);
            SetBounds(_config.WindowX, _config.WindowY, w, h);
            try { Opacity = Math.Clamp(_config.Opacity, 0.40, 1.0); } catch { }

            try
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                UpdateStyles();
            }
            catch { /* ignore */ }

            _resizeGrip = new Panel
            {
                Size = new Size(14, 14),
                BackColor = Color.FromArgb(0, 160, 70),
                Cursor = Cursors.SizeNWSE,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            Controls.Add(_resizeGrip);
            PlaceGrip();

            MouseDown += OnDragDown;
            MouseMove += OnDragMove;
            MouseUp += OnDragUp;
            MouseClick += OnRightClick;

            _resizeGrip.MouseDown += OnResizeDown;
            _resizeGrip.MouseMove += OnResizeMove;
            _resizeGrip.MouseUp += OnResizeUp;

            ContextMenuStrip = BuildMenu();

            HandleCreated += (_, _) =>
            {
                ApplyExtendedStyles();
                ForceTopMost();
                StartTopmostTimer();
            };

            Resize += (_, _) =>
            {
                UpdateScale();
                PlaceGrip();
                Invalidate();
            };

            UpdateScale();
            Logger.Info("OverlayForm ready (owner-drawn, design " + DesignWidth + "x" + DesignHeight + ")");
        }

        private void UpdateScale()
        {
            // Uniform scale so content grows/shrinks without reflow chaos.
            // Top-left of form stays fixed; only width/height change on resize.
            float sx = ClientSize.Width / (float)DesignWidth;
            float sy = ClientSize.Height / (float)DesignHeight;
            _scale = Math.Clamp(Math.Min(sx, sy), 0.55f, 2.5f);
        }

        private void PlaceGrip()
        {
            _resizeGrip.Location = new Point(
                Math.Max(0, ClientSize.Width - _resizeGrip.Width - 2),
                Math.Max(0, ClientSize.Height - _resizeGrip.Height - 2));
        }

        private void StartTopmostTimer()
        {
            if (_topmostTimer != null) return;
            _topmostTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _topmostTimer.Tick += (_, _) => ForceTopMost();
            _topmostTimer.Start();
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Toggle click-through", null, (_, _) => ToggleClickThroughRequested?.Invoke());
            menu.Items.Add("Save size/position", null, (_, _) => PersistGeometry());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open config folder", null, (_, _) => OpenPath(AppPaths.ConfigDirectory));
            menu.Items.Add("Open logs", null, (_, _) => OpenPath(AppPaths.LogDirectory));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Hide to tray", null, (_, _) => Hide());
            menu.Items.Add("Exit WDDM HUD", null, (_, _) => ExitRequested?.Invoke());
            return menu;
        }

        private static void OpenPath(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Logger.Error("OpenPath", ex); }
        }

        public void ApplyMetrics(LiveMetrics m, HudConfig cfg)
        {
            if (IsDisposed || m == null) return;
            _metrics = m;
            _paintConfig = cfg ?? _config;
            try
            {
                if (IsHandleCreated)
                    Invalidate();
            }
            catch { /* ignore */ }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(KeyColor);

            float s = _scale;
            // Soft panel background (slightly above key color so it is visible but not solid black)
            using (var bg = new SolidBrush(PanelFill))
            {
                float pad = 4 * s;
                g.FillRectangle(bg, pad, pad, ClientSize.Width - pad * 2, ClientSize.Height - pad * 2);
            }

            var m = _metrics;
            var cfg = _paintConfig ?? _config;
            if (m == null)
            {
                DrawLine(g, "●  starting…", 10, 10, TextOk, 11f * s, FontStyle.Bold);
                return;
            }

            Color statusColor = TextOk;
            if (string.Equals(m.StatusText, "THERMAL", StringComparison.OrdinalIgnoreCase))
                statusColor = TextCrit;
            else if (string.Equals(m.StatusText, "WARM", StringComparison.OrdinalIgnoreCase))
                statusColor = TextWarn;
            else if (string.Equals(m.StatusText, "BATTERY", StringComparison.OrdinalIgnoreCase))
                statusColor = TextWarn;

            float x = 10 * s;
            float y = 8 * s;
            float line = 16f * s;
            float titleSize = 11.5f * s;
            float bodySize = 10.5f * s;

            // Header — always visible status glyph
            if (cfg.ShowStatusGlyph)
            {
                DrawLine(g, $"{m.StatusGlyph}  {m.StatusText}", x, y, statusColor, titleSize, FontStyle.Bold);
                y += line + 2 * s;
            }

            // Build only rows with real data — never print N/A
            var rows = new List<(string text, Color color)>();

            if (cfg.ShowFps && m.Fps.HasValue)
            {
                string src = string.IsNullOrEmpty(m.FpsSource) ? "" : $"  {m.FpsSource}";
                rows.Add(($"FPS   {m.Fps.Value,6:0.0}{src}", TextOk));
            }

            if (cfg.ShowCpuTemp)
            {
                if (m.CpuTempC.HasValue)
                {
                    Color c = TempColor(m.CpuTempC.Value, cfg.CpuWarnC, cfg.CpuCritC);
                    rows.Add(($"CPU   {m.CpuTempC.Value,5:0.0}°C   {m.CpuUsagePct,4:0}%", c));
                }
                else if (m.CpuUsagePct > 0.05)
                {
                    rows.Add(($"CPU   {m.CpuUsagePct,5:0.0}%", TextOk));
                }
            }

            if (cfg.ShowIgpuTemp)
            {
                if (m.IgpuTempC.HasValue)
                {
                    Color c = TempColor(m.IgpuTempC.Value, cfg.IgpuWarnC, cfg.IgpuCritC);
                    string u = m.IgpuUtilPct.HasValue ? $"   {m.IgpuUtilPct.Value:0}%" : "";
                    rows.Add(($"iGPU  {m.IgpuTempC.Value,5:0.0}°C{u}", c));
                }
                else if (m.IgpuUtilPct.HasValue)
                {
                    rows.Add(($"iGPU  {m.IgpuUtilPct.Value,5:0}%", TextOk));
                }
            }

            if (cfg.ShowDgpuTemp && m.DgpuTempC.HasValue)
            {
                Color c = TempColor(m.DgpuTempC.Value, cfg.DgpuWarnC, cfg.DgpuCritC);
                rows.Add(($"dGPU  {m.DgpuTempC.Value,5:0.0}°C", c));
            }

            if (cfg.ShowGpuUtil && m.DgpuUtilPct.HasValue)
            {
                string clk = m.DgpuClockMhz.HasValue ? $"  {m.DgpuClockMhz.Value:0}MHz" : "";
                string ps = string.IsNullOrEmpty(m.DgpuPState) ? "" : $"  {m.DgpuPState}";
                rows.Add(($"Util  {m.DgpuUtilPct.Value,5:0.0}%{clk}{ps}", TextOk));
            }

            if (cfg.ShowVram && m.VramUsedMiB.HasValue && m.VramTotalMiB.HasValue)
            {
                rows.Add(($"VRAM  {m.VramUsedMiB.Value:0}/{m.VramTotalMiB.Value:0} MiB", TextOk));
            }

            if (cfg.ShowPower && m.DgpuPowerW.HasValue)
            {
                string lim = m.DgpuPowerLimitW.HasValue ? $"/{m.DgpuPowerLimitW.Value:0}W" : "W";
                rows.Add(($"PWR   {m.DgpuPowerW.Value:0.0}{lim}", TextOk));
            }

            if (cfg.ShowSystemRam && m.SystemRamTotalGiB > 0.1)
            {
                rows.Add(($"RAM   {m.SystemRamUsedGiB:0.0}/{m.SystemRamTotalGiB:0.0} GiB  {m.SystemRamUsedPct:0}%", TextOk));
            }

            // Power source (compact)
            rows.Add((m.OnAcPower ? "AC" : "BATTERY", m.OnAcPower ? TextDim : TextWarn));

            foreach (var row in rows)
            {
                DrawLine(g, row.text, x, y, row.color, bodySize, FontStyle.Bold);
                y += line;
                if (y > ClientSize.Height - 18 * s)
                    break;
            }
        }

        private static Color TempColor(double t, int warn, int crit)
        {
            if (t >= crit) return TextCrit;
            if (t >= warn) return TextWarn;
            return TextOk;
        }

        private static void DrawLine(Graphics g, string text, float x, float y, Color color, float size, FontStyle style)
        {
            if (size < 7f) size = 7f;
            try
            {
                using var font = new Font("Consolas", size, style, GraphicsUnit.Pixel);
                // subtle shadow for readability over busy game frames
                using (var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                    g.DrawString(text, font, shadow, x + 1, y + 1);
                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, x, y);
            }
            catch
            {
                using var font = new Font(FontFamily.GenericMonospace, Math.Max(8f, size * 0.75f), style);
                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, x, y);
            }
        }

        // ---- drag (moves whole window; does not scramble content) ----
        private void OnDragDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _config.ClickThrough) return;
            _dragging = true;
            _dragStartCursor = Cursor.Position;
            _dragStartLoc = Location;
        }

        private void OnDragMove(object? sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            int dx = Cursor.Position.X - _dragStartCursor.X;
            int dy = Cursor.Position.Y - _dragStartCursor.Y;
            Location = new Point(_dragStartLoc.X + dx, _dragStartLoc.Y + dy);
        }

        private void OnDragUp(object? sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            PersistGeometry();
        }

        // ---- resize from bottom-right only: top-left stays fixed ----
        private void OnResizeDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _resizing = true;
            _resizeStartSize = Size;
            _resizeStartCursor = Cursor.Position;
        }

        private void OnResizeMove(object? sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            // Only change Width/Height — Location (top-left) is never modified here.
            int dx = Cursor.Position.X - _resizeStartCursor.X;
            int dy = Cursor.Position.Y - _resizeStartCursor.Y;
            int nw = Math.Clamp(_resizeStartSize.Width + dx, 200, 900);
            int nh = Math.Clamp(_resizeStartSize.Height + dy, 120, 700);
            Size = new Size(nw, nh);
        }

        private void OnResizeUp(object? sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            _resizing = false;
            PersistGeometry();
        }

        private void OnRightClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && ContextMenuStrip != null)
            {
                try { ContextMenuStrip.Show(Cursor.Position); } catch { }
            }
        }

        private void ApplyExtendedStyles()
        {
            try
            {
                if (!IsHandleCreated) return;
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                ex |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
                if (_config.ClickThrough)
                    ex |= WS_EX_TRANSPARENT;
                else
                    ex &= ~WS_EX_TRANSPARENT;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
            }
            catch (Exception ex)
            {
                Logger.Error("ApplyExtendedStyles", ex);
            }
        }

        public void SetClickThrough(bool enabled)
        {
            _config.ClickThrough = enabled;
            ApplyExtendedStyles();
            _config.Save();
        }

        public void ForceTopMost()
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                TopMost = true;
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            catch { /* ignore */ }
        }

        public void PersistGeometry()
        {
            try
            {
                if (IsDisposed) return;
                _config.WindowX = Left;
                _config.WindowY = Top;
                _config.WindowWidth = Width;
                _config.WindowHeight = Height;
                try { _config.Opacity = Opacity; } catch { }
                _config.Save();
            }
            catch (Exception ex)
            {
                Logger.Error("PersistGeometry", ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _topmostTimer?.Stop();
                    _topmostTimer?.Dispose();
                    _topmostTimer = null;
                }
                catch { }
            }
            base.Dispose(disposing);
        }
    }
}
