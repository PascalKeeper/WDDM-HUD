// =============================================================================
// WDDM HUD by pascalkeeper — LibreHardwareMonitor sensor hub
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
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
                    if (!IsAdministrator())
                    {
                        // The UI process stays non-elevated. Only --sensor-bridge loads the driver.
                        _computer = null;
                        _opened = true;
                        return;
                    }

                    _computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsMotherboardEnabled = true,
                        IsControllerEnabled = true,
                        IsGpuEnabled = false,
                        IsMemoryEnabled = false,
                        IsNetworkEnabled = false,
                        IsStorageEnabled = false
                    };
                    _computer.Open();
                    _opened = true;
                    Logger.Info("SensorHub: LibreHardwareMonitor opened. WinRing0 load was granted.");
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

        public List<(string Name, int Rpm)> ReadFans()
        {
            var found = new List<(string Name, int Rpm)>();
            EnsureOpened();
            if (!_opened || _computer == null)
                return found;

            lock (_sync)
            {
                try
                {
                    foreach (IHardware hw in _computer.Hardware)
                    {
                        UpdateRecursive(hw);
                        foreach (ISensor s in EnumerateSensors(hw))
                        {
                            if (s.SensorType != SensorType.Fan || !s.Value.HasValue)
                                continue;
                            int rpm = (int)Math.Round(s.Value.Value);
                            if (rpm <= 0 || rpm >= 20000)
                                continue;
                            string name = string.IsNullOrWhiteSpace(s.Name) ? "Fan" : s.Name.Trim();
                            found.Add((name, rpm));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("SensorHub.ReadFans failed: " + ex.Message);
                }
            }

            return found;
        }

        private static bool IsAdministrator()
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(id);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
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
                                    // Rare on HD 630 — keep best-effort if ever exposed
                                    intelGpuTemp = s.Value.Value;
                                }
                                else if (s.SensorType == SensorType.Load)
                                {
                                    string n = s.Name ?? "";
                                    // Prefer primary 3D engine load over decode/copy noise
                                    if (n.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase)
                                        || n.IndexOf("GPU Core", StringComparison.OrdinalIgnoreCase) >= 0)
                                        intelGpuLoad = s.Value.Value;
                                    else if (intelGpuLoad == null && n.IndexOf("D3D", StringComparison.OrdinalIgnoreCase) >= 0)
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
