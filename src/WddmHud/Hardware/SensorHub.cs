// =============================================================================
// WDDM HUD by pascalkeeper — LibreHardwareMonitor sensor hub
// Provides CPU package / core temps and Intel iGPU temps offline.
// Fail-soft: if LHM cannot open (permissions/driver), returns nulls.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using WddmHud.Core;

namespace WddmHud.Hardware
{
    public sealed class SensorHub : IDisposable
    {
        private readonly object _sync = new object();
        private Computer? _computer;
        private bool _opened;
        private bool _disposed;
        private DateTime _lastLogUtc = DateTime.MinValue;

        public bool IsAvailable => _opened && _computer != null;

        public void EnsureOpened()
        {
            if (_opened || _disposed)
                return;

            lock (_sync)
            {
                if (_opened || _disposed)
                    return;

                try
                {
                    var c = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = false,   // no RAM temp sensor on this chassis
                        IsMotherboardEnabled = true,
                        IsControllerEnabled = false,
                        IsNetworkEnabled = false,
                        IsStorageEnabled = false,
                        IsBatteryEnabled = false,
                        IsPsuEnabled = false
                    };
                    c.Open();
                    _computer = c;
                    _opened = true;
                    Logger.Info("LibreHardwareMonitor opened (CPU + GPU + MB sensors).");
                    // One-shot sensor inventory for diagnostics
                    LogSensorInventoryOnce();
                }
                catch (Exception ex)
                {
                    Logger.Error("LibreHardwareMonitor open failed — CPU/iGPU temps may be unavailable", ex);
                    _opened = false;
                    _computer = null;
                }
            }
        }

        public void Sample(out double? cpuTempC, out double? igpuTempC, out double? igpuLoadPct)
        {
            cpuTempC = null;
            igpuTempC = null;
            igpuLoadPct = null;

            EnsureOpened();
            if (!_opened || _computer == null)
                return;

            lock (_sync)
            {
                try
                {
                    double? cpuPackage = null;
                    double? cpuCoreMax = null;
                    double? intelGpuTemp = null;
                    double? intelGpuLoad = null;

                    foreach (IHardware hw in _computer.Hardware)
                    {
                        UpdateRecursive(hw);

                        if (hw.HardwareType == HardwareType.Cpu)
                        {
                            foreach (ISensor s in EnumerateSensors(hw))
                            {
                                if (s.SensorType != SensorType.Temperature || !s.Value.HasValue)
                                    continue;
                                string name = s.Name ?? "";
                                if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0
                                    || name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0
                                    || name.IndexOf("CPU Package", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    cpuPackage = s.Value.Value;
                                }
                                else if (name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (!cpuCoreMax.HasValue || s.Value.Value > cpuCoreMax.Value)
                                        cpuCoreMax = s.Value.Value;
                                }
                                else if (!cpuPackage.HasValue && !cpuCoreMax.HasValue)
                                {
                                    // any CPU temp as last resort
                                    cpuCoreMax = s.Value.Value;
                                }
                            }
                        }
                        else if (hw.HardwareType == HardwareType.GpuIntel)
                        {
                            foreach (ISensor s in EnumerateSensors(hw))
                            {
                                if (!s.Value.HasValue) continue;
                                if (s.SensorType == SensorType.Temperature)
                                {
                                    string n = s.Name ?? "";
                                    if (n.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0
                                        || n.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0
                                        || intelGpuTemp == null)
                                        intelGpuTemp = s.Value.Value;
                                }
                                else if (s.SensorType == SensorType.Load)
                                {
                                    string n = s.Name ?? "";
                                    if (n.IndexOf("D3D", StringComparison.OrdinalIgnoreCase) >= 0
                                        || n.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0
                                        || n.IndexOf("GPU Core", StringComparison.OrdinalIgnoreCase) >= 0
                                        || intelGpuLoad == null)
                                        intelGpuLoad = s.Value.Value;
                                }
                            }
                        }
                        else if (hw.HardwareType == HardwareType.Motherboard)
                        {
                            // Some laptops expose CPU temp only via SuperIO on MB
                            if (!cpuPackage.HasValue && !cpuCoreMax.HasValue)
                            {
                                foreach (ISensor s in EnumerateSensors(hw))
                                {
                                    if (s.SensorType != SensorType.Temperature || !s.Value.HasValue)
                                        continue;
                                    string n = s.Name ?? "";
                                    if (n.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0)
                                        cpuPackage = s.Value.Value;
                                }
                            }
                        }
                    }

                    cpuTempC = cpuPackage ?? cpuCoreMax;
                    igpuTempC = intelGpuTemp;
                    igpuLoadPct = intelGpuLoad;
                }
                catch (Exception ex)
                {
                    // throttle log spam
                    if ((DateTime.UtcNow - _lastLogUtc).TotalSeconds > 30)
                    {
                        Logger.Error("SensorHub.Sample failed", ex);
                        _lastLogUtc = DateTime.UtcNow;
                    }
                }
            }
        }

        private void LogSensorInventoryOnce()
        {
            try
            {
                if (_computer == null) return;
                var lines = new List<string>();
                foreach (IHardware hw in _computer.Hardware)
                {
                    UpdateRecursive(hw);
                    foreach (ISensor s in EnumerateSensors(hw))
                    {
                        if (s.SensorType == SensorType.Temperature || s.SensorType == SensorType.Load)
                        {
                            lines.Add($"{hw.HardwareType}/{hw.Name} :: {s.SensorType} {s.Name}={s.Value}");
                        }
                    }
                }
                Logger.Info("Sensor inventory (" + lines.Count + "): " + string.Join(" | ", lines.Take(40)));
            }
            catch (Exception ex)
            {
                Logger.Warn("Sensor inventory failed: " + ex.Message);
            }
        }

        private static void UpdateRecursive(IHardware hw)
        {
            hw.Update();
            foreach (IHardware sub in hw.SubHardware)
                UpdateRecursive(sub);
        }

        private static IEnumerable<ISensor> EnumerateSensors(IHardware hw)
        {
            foreach (ISensor s in hw.Sensors)
                yield return s;
            foreach (IHardware sub in hw.SubHardware)
            {
                foreach (ISensor s in EnumerateSensors(sub))
                    yield return s;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_sync)
            {
                try { _computer?.Close(); } catch { /* ignore */ }
                _computer = null;
                _opened = false;
            }
        }
    }
}
