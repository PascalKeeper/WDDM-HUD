// =============================================================================
// WDDM HUD by pascalkeeper — Static hardware auto-detection model
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WddmHud.Hardware
{
    public sealed class GpuDeviceInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Unknown";

        [JsonPropertyName("pnp_device_id")]
        public string PnpDeviceId { get; set; } = "";

        [JsonPropertyName("driver_version")]
        public string DriverVersion { get; set; } = "";

        [JsonPropertyName("adapter_ram_bytes")]
        public long AdapterRamBytes { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("is_nvidia")]
        public bool IsNvidia { get; set; }

        [JsonPropertyName("is_intel")]
        public bool IsIntel { get; set; }

        [JsonPropertyName("is_amd")]
        public bool IsAmd { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; } = "Unknown"; // iGPU | dGPU | Unknown

        [JsonPropertyName("nvidia_smi_index")]
        public int? NvidiaSmiIndex { get; set; }
    }

    public sealed class HardwareSnapshot
    {
        [JsonPropertyName("captured_utc")]
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("machine_name")]
        public string MachineName { get; set; } = Environment.MachineName;

        [JsonPropertyName("os_version")]
        public string OsVersion { get; set; } = "";

        [JsonPropertyName("cpu_name")]
        public string CpuName { get; set; } = "Unknown";

        [JsonPropertyName("cpu_cores")]
        public int CpuCores { get; set; }

        [JsonPropertyName("cpu_logical")]
        public int CpuLogical { get; set; }

        [JsonPropertyName("cpu_max_mhz")]
        public int CpuMaxMhz { get; set; }

        [JsonPropertyName("system_ram_bytes")]
        public long SystemRamBytes { get; set; }

        [JsonPropertyName("gpus")]
        public List<GpuDeviceInfo> Gpus { get; set; } = new List<GpuDeviceInfo>();

        [JsonPropertyName("has_hybrid_graphics")]
        public bool HasHybridGraphics { get; set; }

        [JsonPropertyName("primary_dgpu_name")]
        public string PrimaryDgpuName { get; set; } = "N/A";

        [JsonPropertyName("primary_igpu_name")]
        public string PrimaryIgpuName { get; set; } = "N/A";

        [JsonPropertyName("nvidia_smi_available")]
        public bool NvidiaSmiAvailable { get; set; }

        [JsonPropertyName("nvidia_smi_path")]
        public string NvidiaSmiPath { get; set; } = "";

        [JsonPropertyName("battery_present")]
        public bool BatteryPresent { get; set; }

        [JsonPropertyName("on_ac_power")]
        public bool OnAcPower { get; set; } = true;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";
    }
}
