// =============================================================================
// WDDM HUD by pascalkeeper — Auto-detect CPU / iGPU / dGPU / RAM / power
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
// Uses WMI + nvidia-smi path discovery. No third-party runtime deps.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using WddmHud.Core;

namespace WddmHud.Hardware
{
    public static class HardwareDetector
    {
        private static HardwareSnapshot? _cached;
        private static readonly object Sync = new object();

        public static HardwareSnapshot Detect(bool forceRefresh = false)
        {
            lock (Sync)
            {
                if (!forceRefresh && _cached != null)
                    return _cached;

                var snap = new HardwareSnapshot
                {
                    CapturedUtc = DateTime.UtcNow,
                    MachineName = Environment.MachineName,
                    OsVersion = Environment.OSVersion.VersionString
                };

                try { DetectCpu(snap); }
                catch (Exception ex) { Logger.Error("CPU detect failed", ex); }

                try { DetectMemory(snap); }
                catch (Exception ex) { Logger.Error("Memory detect failed", ex); }

                try { DetectGpus(snap); }
                catch (Exception ex) { Logger.Error("GPU detect failed", ex); }

                try { DetectPower(snap); }
                catch (Exception ex) { Logger.Error("Power detect failed", ex); }

                try
                {
                    snap.NvidiaSmiPath = NvidiaSmiClient.ResolveNvidiaSmiPath() ?? "";
                    snap.NvidiaSmiAvailable = !string.IsNullOrEmpty(snap.NvidiaSmiPath);
                }
                catch (Exception ex)
                {
                    Logger.Error("nvidia-smi path resolve failed", ex);
                }

                BuildSummary(snap);
                PersistSnapshot(snap);
                _cached = snap;
                Logger.Info("Hardware snapshot: " + snap.Summary);
                return snap;
            }
        }

        private static void DetectCpu(HardwareSnapshot snap)
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                snap.CpuName = (obj["Name"]?.ToString() ?? "Unknown").Trim();
                snap.CpuCores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                snap.CpuLogical = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
                snap.CpuMaxMhz = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0);
                break;
            }
        }

        private static void DetectMemory(HardwareSnapshot snap)
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                // TotalVisibleMemorySize is in KB
                ulong kb = Convert.ToUInt64(obj["TotalVisibleMemorySize"] ?? 0UL);
                snap.SystemRamBytes = (long)(kb * 1024UL);
                break;
            }
        }

        private static void DetectGpus(HardwareSnapshot snap)
        {
            var list = new List<GpuDeviceInfo>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, DriverVersion, AdapterRAM, Status FROM Win32_VideoController");

            foreach (ManagementObject obj in searcher.Get())
            {
                string name = (obj["Name"]?.ToString() ?? "Unknown").Trim();
                string pnp = obj["PNPDeviceID"]?.ToString() ?? "";
                string driver = obj["DriverVersion"]?.ToString() ?? "";
                string status = obj["Status"]?.ToString() ?? "";
                long ram = 0;
                try { ram = Convert.ToInt64(obj["AdapterRAM"] ?? 0L); } catch { ram = 0; }

                // Skip Microsoft Basic Display Adapter noise when real GPUs exist
                bool isBasic = name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0
                               || name.IndexOf("Microsoft Remote", StringComparison.OrdinalIgnoreCase) >= 0;

                var gpu = new GpuDeviceInfo
                {
                    Name = name,
                    PnpDeviceId = pnp,
                    DriverVersion = driver,
                    AdapterRamBytes = ram < 0 ? 0 : ram, // WMI can overflow signed int for large adapters
                    Status = status,
                    IsNvidia = name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0
                               || pnp.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0,
                    IsIntel = name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0
                              || pnp.IndexOf("VEN_8086", StringComparison.OrdinalIgnoreCase) >= 0,
                    IsAmd = name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0
                            || pnp.IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) >= 0
                };

                // Role heuristic for hybrid laptops (AW17R4 class)
                if (gpu.IsIntel && !gpu.IsNvidia)
                    gpu.Role = "iGPU";
                else if (gpu.IsNvidia)
                    gpu.Role = "dGPU";
                else if (gpu.IsAmd && name.IndexOf("Graphics", StringComparison.OrdinalIgnoreCase) >= 0
                         && name.IndexOf("Radeon RX", StringComparison.OrdinalIgnoreCase) < 0)
                    gpu.Role = "iGPU";
                else if (gpu.IsAmd)
                    gpu.Role = "dGPU";
                else
                    gpu.Role = isBasic ? "Basic" : "Unknown";

                // Deduplicate identical PNP paths (ghost Intel entries)
                if (!string.IsNullOrEmpty(pnp) && list.Any(g => string.Equals(g.PnpDeviceId, pnp, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Prefer OK status over Unknown when names collide
                var sameName = list.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                if (sameName != null)
                {
                    if (string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(sameName.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Remove(sameName);
                    }
                    else
                    {
                        continue;
                    }
                }

                if (isBasic)
                    continue;

                list.Add(gpu);
            }

            // Assign nvidia-smi indices in detection order among NVIDIA adapters
            int nIdx = 0;
            foreach (var g in list.Where(x => x.IsNvidia))
            {
                g.NvidiaSmiIndex = nIdx++;
            }

            snap.Gpus = list;
            snap.PrimaryDgpuName = list.FirstOrDefault(g => g.Role == "dGPU")?.Name ?? "N/A";
            snap.PrimaryIgpuName = list.FirstOrDefault(g => g.Role == "iGPU")?.Name ?? "N/A";
            snap.HasHybridGraphics = list.Any(g => g.Role == "iGPU") && list.Any(g => g.Role == "dGPU");
        }

        private static void DetectPower(HardwareSnapshot snap)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus, EstimatedChargeRemaining FROM Win32_Battery");
                var results = searcher.Get().Cast<ManagementObject>().ToList();
                if (results.Count == 0)
                {
                    snap.BatteryPresent = false;
                    snap.OnAcPower = true;
                    return;
                }

                snap.BatteryPresent = true;
                // BatteryStatus: 1=Discharging, 2=AC, 3=Fully Charged, etc.
                int status = Convert.ToInt32(results[0]["BatteryStatus"] ?? 0);
                snap.OnAcPower = status == 2 || status == 3 || status == 6 || status == 7 || status == 8 || status == 9;
            }
            catch
            {
                snap.BatteryPresent = false;
                snap.OnAcPower = true;
            }
        }

        private static void BuildSummary(HardwareSnapshot snap)
        {
            snap.Summary = string.Format(
                "CPU={0} ({1}C/{2}T) | iGPU={3} | dGPU={4} | RAM={5:0.0}GB | Hybrid={6} | SMI={7} | AC={8}",
                snap.CpuName,
                snap.CpuCores,
                snap.CpuLogical,
                snap.PrimaryIgpuName,
                snap.PrimaryDgpuName,
                snap.SystemRamBytes / (1024.0 * 1024.0 * 1024.0),
                snap.HasHybridGraphics,
                snap.NvidiaSmiAvailable,
                snap.OnAcPower);
        }

        private static void PersistSnapshot(HardwareSnapshot snap)
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(AppPaths.HardwareSnapshotPath, JsonSerializer.Serialize(snap, opts));
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not persist hardware snapshot: " + ex.Message);
            }
        }
    }
}
