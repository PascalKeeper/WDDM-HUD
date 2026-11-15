// =============================================================================
// WDDM HUD by pascalkeeper — Regression lock for drag / resize
// A saved click_through=true made the overlay ignore the mouse after relaunch.
// This self-test fails the Release build and the installer if that returns.
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
// =============================================================================

using System;
using WddmHud.Core;

namespace WddmHud.UI
{
    public static class DragResizeGuard
    {
        /// <summary>
        /// 0 = pass. Non-zero is a frozen contract: do not renumber without
        /// updating installer\Install-WddmHud.ps1 comments.
        /// </summary>
        public static int SelfTest()
        {
            try
            {
                int plateAlpha = OverlayHitTest.HitPlateAlpha;
                int plateRgb = OverlayHitTest.HitPlateR + OverlayHitTest.HitPlateG + OverlayHitTest.HitPlateB;
                if (plateAlpha < OverlayHitTest.MinHittableAlpha)
                {
                    Logger.Error("Drag/resize self-test: hit plate alpha is below DWM's hittable floor.");
                    return 1;
                }

                if (plateRgb < 1)
                {
                    Logger.Error("Drag/resize self-test: hit plate RGB is zero (premultiplied alpha collapses).");
                    return 2;
                }

                const string poisoned =
                    "{ \"click_through\": true, \"auto_click_through_in_game\": true, \"window_width\": 200, \"window_height\": 150 }";
                HudConfig loaded = HudConfig.FromJson(poisoned);
                if (loaded.ClickThrough)
                {
                    Logger.Error("Drag/resize self-test: click_through=true survived FromJson.");
                    return 3;
                }

                loaded.ClickThrough = true;
                string json = HudConfig.ToJson(loaded);
                if (!loaded.ClickThrough)
                {
                    Logger.Error("Drag/resize self-test: ToJson cleared the runtime click-through toggle.");
                    return 4;
                }

                if (ContainsPersistedClickThroughTrue(json))
                {
                    Logger.Error("Drag/resize self-test: ToJson wrote click_through true.");
                    return 5;
                }

                const int w = 200;
                const int h = 150;
                if (OverlayHitTest.HitClient(100, 75, w, h, 1f, clickThrough: false) != OverlayHitTest.HtCaption)
                {
                    Logger.Error("Drag/resize self-test: plate is not HTCAPTION.");
                    return 6;
                }

                if (OverlayHitTest.HitClient(w - 4, h - 4, w, h, 1f, clickThrough: false) != OverlayHitTest.HtBottomRight)
                {
                    Logger.Error("Drag/resize self-test: corner is not HTBOTTOMRIGHT.");
                    return 7;
                }

                if (OverlayHitTest.HitClient(100, 75, w, h, 1f, clickThrough: true) != OverlayHitTest.HtTransparent)
                {
                    Logger.Error("Drag/resize self-test: click-through is not HTTRANSPARENT.");
                    return 8;
                }

                Logger.Info("Drag/resize self-test passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Drag/resize self-test crashed", ex);
                return 9;
            }
        }

        private static bool ContainsPersistedClickThroughTrue(string json)
        {
            if (string.IsNullOrEmpty(json))
                return false;
            string compact = json.Replace(" ", "", StringComparison.Ordinal)
                .Replace("\r", "", StringComparison.Ordinal)
                .Replace("\n", "", StringComparison.Ordinal);
            return compact.Contains("\"click_through\":true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
