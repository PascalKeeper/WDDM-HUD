// =============================================================================
// WDDM HUD by pascalkeeper — Drag / resize hit-test math
// Shared by OverlayForm and DragResizeGuard so a regression fails the self-test.
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
// =============================================================================

using System;
using System.Drawing;

namespace WddmHud.UI
{
    /// <summary>
    /// Layered-window hit rules. DWM ignores alpha 0 and often 1, so the plate
    /// must stay above <see cref="MinHittableAlpha"/> with non-zero RGB.
    /// </summary>
    public static class OverlayHitTest
    {
        public const int HitPlateAlpha = 20;
        public const int HitPlateR = 4;
        public const int HitPlateG = 4;
        public const int HitPlateB = 4;
        public static readonly int MinHittableAlpha = 8;

        public const int HtTransparent = -1;
        public const int HtCaption = 2;
        public const int HtBottomRight = 17;

        public static Rectangle GripRect(int width, int height, float scale)
        {
            int grip = Math.Max(18, (int)(22 * Math.Max(0.1f, scale)));
            return new Rectangle(
                Math.Max(0, width - grip),
                Math.Max(0, height - grip),
                grip,
                grip);
        }

        public static int HitClient(int x, int y, int width, int height, float scale, bool clickThrough)
        {
            if (clickThrough)
                return HtTransparent;
            if (GripRect(width, height, scale).Contains(x, y))
                return HtBottomRight;
            return HtCaption;
        }
    }
}
