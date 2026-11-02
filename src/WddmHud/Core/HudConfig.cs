// =============================================================================
// WDDM HUD by pascalkeeper — Persistent user configuration (JSON, no deps)
// =============================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WddmHud.Core
{
    /// <summary>
    /// All user-tunable HUD settings. Defaults are longevity-aware for AW17R4 + GTX 1070.
    /// </summary>
    public sealed class HudConfig
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("window_x")]
        public int WindowX { get; set; } = 40;

        [JsonPropertyName("window_y")]
        public int WindowY { get; set; } = 40;

        [JsonPropertyName("window_width")]
        public int WindowWidth { get; set; } = 360;

        [JsonPropertyName("window_height")]
        public int WindowHeight { get; set; } = 280;

        [JsonPropertyName("user_sized_frame")]
        public bool UserSizedFrame { get; set; }

        [JsonPropertyName("natural_width")]
        public int NaturalWidth { get; set; }

        [JsonPropertyName("natural_height")]
        public int NaturalHeight { get; set; }

        [JsonPropertyName("opacity")]
        public double Opacity { get; set; } = 0.92;

        [JsonPropertyName("poll_interval_ms")]
        public int PollIntervalMs { get; set; } = 1000;

        [JsonPropertyName("click_through")]
        public bool ClickThrough { get; set; } = false;

        /// <summary>Re-assert WS_EX_TOPMOST while a game is in the foreground.</summary>
        [JsonPropertyName("stay_in_front")]
        public bool StayInFront { get; set; } = true;

        /// <summary>Force click-through automatically while Fortnite/RDR2/etc. is running.</summary>
        [JsonPropertyName("auto_click_through_in_game")]
        public bool AutoClickThroughInGame { get; set; } = true;

        /// <summary>Paint a solid panel behind text. Off = fully transparent chrome (text + outline only).</summary>
        [JsonPropertyName("show_panel")]
        public bool ShowPanel { get; set; } = false;

        [JsonPropertyName("start_minimized_to_tray")]
        public bool StartMinimizedToTray { get; set; } = false;

        [JsonPropertyName("show_fps")]
        public bool ShowFps { get; set; } = true;

        [JsonPropertyName("show_cpu_temp")]
        public bool ShowCpuTemp { get; set; } = true;

        [JsonPropertyName("show_igpu_temp")]
        public bool ShowIgpuTemp { get; set; } = true;

        [JsonPropertyName("show_dgpu_temp")]
        public bool ShowDgpuTemp { get; set; } = true;

        [JsonPropertyName("show_vram")]
        public bool ShowVram { get; set; } = true;

        [JsonPropertyName("show_system_ram")]
        public bool ShowSystemRam { get; set; } = true;

        // RAM temperature removed — AW17R4 has no exposed sensor (always clutter).
        [JsonPropertyName("show_ram_temp")]
        public bool ShowRamTemp { get; set; } = false;

        [JsonPropertyName("show_power")]
        public bool ShowPower { get; set; } = true;

        [JsonPropertyName("show_gpu_util")]
        public bool ShowGpuUtil { get; set; } = true;

        [JsonPropertyName("show_status_glyph")]
        public bool ShowStatusGlyph { get; set; } = true;

        // Thermal thresholds (°C) — longevity gates for Alienware 17 R4 / Pascal
        [JsonPropertyName("cpu_warn_c")]
        public int CpuWarnC { get; set; } = 78;

        [JsonPropertyName("cpu_crit_c")]
        public int CpuCritC { get; set; } = 93;

        [JsonPropertyName("igpu_warn_c")]
        public int IgpuWarnC { get; set; } = 73;

        [JsonPropertyName("igpu_crit_c")]
        public int IgpuCritC { get; set; } = 88;

        [JsonPropertyName("dgpu_warn_c")]
        public int DgpuWarnC { get; set; } = 80;

        [JsonPropertyName("dgpu_crit_c")]
        public int DgpuCritC { get; set; } = 91;

        [JsonPropertyName("font_size")]
        public float FontSize { get; set; } = 11f;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static HudConfig Load()
        {
            string path = AppPaths.ConfigFilePath;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<HudConfig>(json, JsonOptions);
                    if (cfg != null)
                    {
                        cfg.Sanitize();
                        Logger.Info("Config loaded from " + path);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Config load failed; using defaults", ex);
            }

            var fresh = new HudConfig();
            fresh.Save();
            return fresh;
        }

        public void Save()
        {
            try
            {
                Sanitize();
                string path = AppPaths.ConfigFilePath;
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(path, json);
                Logger.Info("Config saved to " + path);
            }
            catch (Exception ex)
            {
                Logger.Error("Config save failed", ex);
            }
        }

        /// <summary>
        /// Clamp values into safe operational ranges.
        /// </summary>
        public void Sanitize()
        {
            // Force-disable RAM temp (no sensor on target hardware)
            ShowRamTemp = false;

            if (WindowWidth < 64) WindowWidth = 64;
            if (WindowHeight < 32) WindowHeight = 32;
            if (WindowWidth > 900) WindowWidth = 900;
            if (WindowHeight > 700) WindowHeight = 700;
            // Form.Opacity fades the TEXT as well as chrome. Keep it at 1 so glyphs stay
            // readable; transparency is the color-key background, not window alpha.
            if (Opacity < 0.85) Opacity = 1.0;
            if (Opacity > 1.0) Opacity = 1.0;
            if (PollIntervalMs < 250) PollIntervalMs = 250;
            if (PollIntervalMs > 5000) PollIntervalMs = 5000;
            if (FontSize < 8f) FontSize = 8f;
            if (FontSize > 22f) FontSize = 22f;
            if (CpuWarnC >= CpuCritC) CpuCritC = CpuWarnC + 5;
            if (IgpuWarnC >= IgpuCritC) IgpuCritC = IgpuWarnC + 5;
            if (DgpuWarnC >= DgpuCritC) DgpuCritC = DgpuWarnC + 5;
        }
    }
}
