// =============================================================================
// Elevated sensor bridge host — run: WddmHud.exe --sensor-bridge
// Polls LibreHardwareMonitor and writes config\live_sensors.json for the HUD.
// Prefer one elevated instance; main overlay stays asInvoker (no UAC spam).
// =============================================================================

using System;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using WddmHud.Core;

namespace WddmHud.Hardware
{
    public static class SensorBridgeHost
    {
        public static int Run()
        {
            Logger.Info("Sensor bridge starting (elevated path for CPU package temps when possible).");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            Computer? computer = null;
            try
            {
                computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = true,
                    IsMemoryEnabled = false,
                    IsControllerEnabled = false,
                    IsNetworkEnabled = false,
                    IsStorageEnabled = false
                };
                computer.Open();
                Logger.Info("Sensor bridge: LHM opened.");
            }
            catch (Exception ex)
            {
                Logger.Error("Sensor bridge LHM open failed", ex);
                return 2;
            }

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    double? cpu = null, igpuT = null, igpuL = null, dgpu = null, hotspot = null;
                    try
                    {
                        foreach (IHardware hw in computer.Hardware)
                        {
                            UpdateAll(hw);
                            if (hw.HardwareType == HardwareType.Cpu)
                            {
                                foreach (var s in EnumTemps(hw))
                                {
                                    string n = s.Name ?? "";
                                    if (n.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 && s.Value.HasValue)
                                        cpu = s.Value;
                                    else if (n.IndexOf("Core Max", StringComparison.OrdinalIgnoreCase) >= 0 && s.Value.HasValue)
                                        cpu ??= s.Value;
                                    else if (n.IndexOf("Core Average", StringComparison.OrdinalIgnoreCase) >= 0 && s.Value.HasValue)
                                        cpu ??= s.Value;
                                    else if (n.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    {
                                        if (!cpu.HasValue || s.Value > cpu) cpu = s.Value;
                                    }
                                }
                            }
                            else if (hw.HardwareType == HardwareType.GpuIntel)
                            {
                                foreach (ISensor s in AllSensors(hw))
                                {
                                    if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                                        igpuT = s.Value;
                                    if (s.SensorType == SensorType.Load && s.Value.HasValue)
                                    {
                                        string n = s.Name ?? "";
                                        if (n.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0)
                                            igpuL = s.Value;
                                    }
                                }
                            }
                            else if (hw.HardwareType == HardwareType.GpuNvidia)
                            {
                                foreach (ISensor s in AllSensors(hw))
                                {
                                    if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                                    string n = s.Name ?? "";
                                    if (n.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0
                                        || n.IndexOf("Hotspot", StringComparison.OrdinalIgnoreCase) >= 0)
                                        hotspot = s.Value;
                                    else if (n.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                                        dgpu = s.Value;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Sensor bridge sample error: " + ex.Message);
                    }

                    string note = "";
                    if (!cpu.HasValue)
                        note = "CPU package temp unavailable (kernel/MSR path). iGPU temp often unexposed on HD 630.";
                    if (!igpuT.HasValue)
                        note = (note + " No iGPU temperature sensor exposed.").Trim();

                    SensorBridgeFile.Write(new LiveSensorFile
                    {
                        Utc = DateTime.UtcNow,
                        CpuTempC = cpu,
                        IgpuTempC = igpuT,
                        IgpuUtilPct = igpuL,
                        DgpuTempC = dgpu,
                        DgpuHotspotC = hotspot,
                        Source = "sensor-bridge",
                        Note = note
                    });

                    Thread.Sleep(1000);
                }
            }
            finally
            {
                try { computer.Close(); } catch { }
            }

            Logger.Info("Sensor bridge exiting.");
            return 0;
        }

        private static void UpdateAll(IHardware hw)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware)
                UpdateAll(sub);
        }

        private static System.Collections.Generic.IEnumerable<ISensor> EnumTemps(IHardware hw)
        {
            foreach (var s in AllSensors(hw))
                if (s.SensorType == SensorType.Temperature)
                    yield return s;
        }

        private static System.Collections.Generic.IEnumerable<ISensor> AllSensors(IHardware hw)
        {
            foreach (var s in hw.Sensors) yield return s;
            foreach (var sub in hw.SubHardware)
                foreach (var s in AllSensors(sub))
                    yield return s;
        }
    }
}
