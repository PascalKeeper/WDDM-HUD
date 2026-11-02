// =============================================================================
// WDDM HUD by pascalkeeper — coin-sized color wheel
// Copyright (C) 2026 Joseph Rocco Peransi / pascalkeeper
//
// A US half-dollar is 30.61 mm. A 60-inch 16:9 screen is about 1328 mm wide.
// On a 1920-wide desktop that ratio is 44 pixels. The popup is that size.
// =============================================================================

using System.Drawing.Drawing2D;
using WddmHud.Core;

namespace WddmHud.UI
{
    public static class ColorWheelMath
    {
        public const double HalfDollarMm = 30.61;
        public const double SixtyInchTvWidthMm = 1328.0;

        public static int PopupSize(int screenWidth)
        {
            if (screenWidth < 640)
                screenWidth = 1920;
            int px = (int)Math.Round(screenWidth * (HalfDollarMm / SixtyInchTvWidthMm));
            return Math.Clamp(px, 40, 52);
        }

        public static bool TryColorAt(int x, int y, int size, out Color color)
        {
            color = Color.Empty;
            int center = size / 2;
            int radius = Math.Max(1, center - 1);
            double dx = x - center;
            double dy = y - center;
            double dist = Math.Sqrt((dx * dx) + (dy * dy));
            if (dist > radius)
                return false;

            double hue = Math.Atan2(dy, dx) * (180.0 / Math.PI);
            if (hue < 0)
                hue += 360.0;
            color = FromHsv(hue, dist / radius, 1.0);
            return true;
        }

        public static Color FromHsv(double hueDegrees, double saturation, double value)
        {
            double h = ((hueDegrees % 360.0) + 360.0) % 360.0;
            double s = Math.Clamp(saturation, 0, 1);
            double v = Math.Clamp(value, 0, 1);
            double c = v * s;
            double x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromArgb(
                255,
                (int)Math.Round((r + m) * 255.0),
                (int)Math.Round((g + m) * 255.0),
                (int)Math.Round((b + m) * 255.0));
        }

        /// <summary>
        /// Center is white. The right edge is red. Size on a 1920-wide screen is 44.
        /// </summary>
        public static int SelfTest()
        {
            if (PopupSize(1920) != 44)
                return 1;

            int size = 44;
            int center = size / 2;
            int radius = center - 1;
            if (!TryColorAt(center, center, size, out Color middle))
                return 2;
            if (middle.R < 240 || middle.G < 240 || middle.B < 240)
                return 3;

            if (!TryColorAt(center + radius - 1, center, size, out Color red))
                return 4;
            if (red.R < 200 || red.G > 40 || red.B > 40)
                return 5;

            if (TryColorAt(-1, -1, size, out _))
                return 6;

            return 0;
        }
    }

    public sealed class ColorWheelPopup : Form
    {
        private readonly OverlayForm _overlay;
        private readonly int _wheelSize;
        private readonly DateTime _openedUtc = DateTime.UtcNow;
        private bool _dirty;

        private ColorWheelPopup(OverlayForm overlay, int wheelSize)
        {
            _overlay = overlay;
            _wheelSize = wheelSize;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            ClientSize = new Size(wheelSize, wheelSize);
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
        }

        public static void ShowDroppedUp(OverlayForm overlay, Point cursor)
        {
            Rectangle screen = Screen.FromPoint(cursor).WorkingArea;
            int size = ColorWheelMath.PopupSize(screen.Width);
            var popup = new ColorWheelPopup(overlay, size);
            int x = cursor.X - (size / 2);
            int y = cursor.Y - size - 6;
            if (y < screen.Top)
                y = Math.Min(cursor.Y + 6, screen.Bottom - size);
            int maxX = Math.Max(screen.Left, screen.Right - size);
            int maxY = Math.Max(screen.Top, screen.Bottom - size);
            popup.Location = new Point(Math.Clamp(x, screen.Left, maxX), Math.Clamp(y, screen.Top, maxY));
            popup.Show();
            popup.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.Clear(Color.Black);
            for (int y = 0; y < _wheelSize; y++)
            {
                for (int x = 0; x < _wheelSize; x++)
                {
                    if (!ColorWheelMath.TryColorAt(x, y, _wheelSize, out Color color))
                        continue;
                    using var brush = new SolidBrush(color);
                    e.Graphics.FillRectangle(brush, x, y, 1, 1);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dirty = true;
                ApplyAt(e.Location, commit: false);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dirty && (e.Button & MouseButtons.Left) == MouseButtons.Left)
                ApplyAt(e.Location, commit: false);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dirty && e.Button == MouseButtons.Left)
            {
                ApplyAt(e.Location, commit: false);
                Commit();
                Close();
            }
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Commit();
                Close();
            }
            base.OnKeyDown(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            // The tray menu hands focus over as this opens. Ignore that first beat.
            if ((DateTime.UtcNow - _openedUtc).TotalMilliseconds < 400)
            {
                base.OnDeactivate(e);
                return;
            }
            Commit();
            Close();
            base.OnDeactivate(e);
        }

        private void ApplyAt(Point point, bool commit)
        {
            if (!ColorWheelMath.TryColorAt(point.X, point.Y, _wheelSize, out Color color))
                return;
            _overlay.PreviewAccent(color);
            _dirty = true;
            if (commit)
                Commit();
        }

        private void Commit()
        {
            if (!_dirty)
                return;
            _dirty = false;
            _overlay.CommitAccent();
        }
    }
}
