// =============================================================================
// Shared live_sensors.json schema between main HUD (asInvoker) and optional
// elevated sensor bridge (--sensor-bridge). Lets CPU package temps work when
// the user has approved a one-time elevation for LibreHardwareMonitor/MSR.
// =============================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WddmHud.Core;

namespace WddmHud.Hardware
{
    public sealed class LiveSensorFile
    {
        [JsonPropertyName("utc")]
        public DateTime Utc { get; set; }

        [JsonPropertyName("cpu_temp_c")]
        public double? CpuTempC { get; set; }

        [JsonPropertyName("igpu_temp_c")]
        public double? IgpuTempC { get; set; }

        [JsonPropertyName("igpu_util_pct")]
        public double? IgpuUtilPct { get; set; }

        [JsonPropertyName("dgpu_temp_c")]
        public double? DgpuTempC { get; set; }

        [JsonPropertyName("dgpu_hotspot_c")]
        public double? DgpuHotspotC { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; } = "lhm";

        [JsonPropertyName("note")]
        public string Note { get; set; } = "";
    }

    public static class SensorBridgeFile
    {
        public static string Path => System.IO.Path.Combine(AppPaths.ConfigDirectory, "live_sensors.json");

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        public static void Write(LiveSensorFile data)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ConfigDirectory);
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, Opts));
                File.Copy(tmp, Path, overwrite: true);
                try { File.Delete(tmp); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warn("SensorBridgeFile.Write failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Read if file is fresh (&lt; 5 seconds old). Stale files are ignored.
        /// </summary>
        public static LiveSensorFile? TryReadFresh(double maxAgeSeconds = 5.0)
        {
            try
            {
                if (!File.Exists(Path))
                    return null;
                var fi = new FileInfo(Path);
                if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalSeconds > maxAgeSeconds)
                    return null;
                string json = File.ReadAllText(Path);
                return JsonSerializer.Deserialize<LiveSensorFile>(json, Opts);
            }
            catch
            {
                return null;
            }
        }
    }
}
