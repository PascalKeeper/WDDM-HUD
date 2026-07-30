// =============================================================================
// WDDM HUD by pascalkeeper — Live metrics model + sampling engine
// Non-invasive (no game DLL injection). CPU/iGPU temps via LibreHardwareMonitor.
// FPS via RTSS shared memory or optional PresentMon CSV — hidden when unavailable.
// =============================================================================

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WddmHud.Core;

namespace WddmHud.Hardware
{
    public sealed class LiveMetrics
    {
        public DateTime SampledUtc { get; set; } = DateTime.UtcNow;

        public double? Fps { get; set; }
        public string FpsSource { get; set; } = "";

        public double? CpuTempC { get; set; }
        public double CpuUsagePct { get; set; }
        public string CpuName { get; set; } = "";

        public double? IgpuTempC { get; set; }
        public string IgpuName { get; set; } = "";
        public double? IgpuUtilPct { get; set; }

        public double? DgpuTempC { get; set; }
        public string DgpuName { get; set; } = "";
        public double? DgpuUtilPct { get; set; }
        public double? DgpuMemUtilPct { get; set; }
        public double? VramUsedMiB { get; set; }
        public double? VramTotalMiB { get; set; }
        public double? DgpuPowerW { get; set; }
        public double? DgpuPowerLimitW { get; set; }
        public double? DgpuClockMhz { get; set; }
        public string DgpuPState { get; set; } = "";

        public double SystemRamUsedGiB { get; set; }
        public double SystemRamTotalGiB { get; set; }
        public double SystemRamUsedPct { get; set; }

        public bool OnAcPower { get; set; } = true;
        public bool HybridDetected { get; set; }
        public string StatusGlyph { get; set; } = "●";
        public string StatusText { get; set; } = "INIT";
        public string StatusDetail { get; set; } = "";
    }

    public sealed class MetricsEngine : IDisposable
    {
        private readonly HardwareSnapshot _hw;
        private readonly PerformanceCounter? _cpuCounter;
        private readonly SensorHub _sensors = new SensorHub();
        private bool _disposed;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public MetricsEngine(HardwareSnapshot hw)
        {
            _hw = hw ?? throw new ArgumentNullException(nameof(hw));
            PerformanceCounter? counter = null;
            try
            {
                counter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                counter.NextValue(); // prime
            }
            catch (Exception ex)
            {
                Logger.Warn("CPU PerformanceCounter unavailable: " + ex.Message);
                counter = null;
            }
            _cpuCounter = counter;

            // Open LHM early so first UI frame may already have temps
            try { _sensors.EnsureOpened(); }
            catch (Exception ex) { Logger.Error("SensorHub early open", ex); }
        }

        public LiveMetrics Sample()
        {
            var m = new LiveMetrics
            {
                SampledUtc = DateTime.UtcNow,
                CpuName = _hw.CpuName ?? "",
                IgpuName = _hw.PrimaryIgpuName ?? "",
                DgpuName = _hw.PrimaryDgpuName ?? "",
                HybridDetected = _hw.HasHybridGraphics,
                OnAcPower = _hw.OnAcPower
            };

            try { SampleCpuUsage(m); } catch (Exception ex) { Logger.Error("CPU usage sample", ex); }
            try { SampleMemory(m); } catch (Exception ex) { Logger.Error("RAM sample", ex); }
            try { SampleNvidia(m); } catch (Exception ex) { Logger.Error("NVIDIA sample", ex); }
            try { SampleLhmTemps(m); } catch (Exception ex) { Logger.Error("LHM sample", ex); }
            try { SampleFps(m); } catch (Exception ex) { Logger.Debug("FPS sample: " + ex.Message); }
            try { BuildStatus(m); } catch (Exception ex) { Logger.Error("Status build", ex); }

            return m;
        }

        private void SampleCpuUsage(LiveMetrics m)
        {
            if (_cpuCounter != null)
            {
                try
                {
                    m.CpuUsagePct = Math.Clamp((double)_cpuCounter.NextValue(), 0.0, 100.0);
                    return;
                }
                catch { /* fall through */ }
            }
            m.CpuUsagePct = 0;
        }

        private void SampleMemory(LiveMetrics m)
        {
            var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref st))
            {
                m.SystemRamTotalGiB = _hw.SystemRamBytes / (1024.0 * 1024.0 * 1024.0);
                return;
            }

            m.SystemRamTotalGiB = st.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double avail = st.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            m.SystemRamUsedGiB = Math.Max(0, m.SystemRamTotalGiB - avail);
            m.SystemRamUsedPct = st.dwMemoryLoad;
        }

        private void SampleNvidia(LiveMetrics m)
        {
            var list = NvidiaSmiClient.QueryAll();
            if (list.Count == 0)
                return;

            NvidiaGpuSample primary = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                if (!double.IsNaN(list[i].MemoryTotalMiB)
                    && (double.IsNaN(primary.MemoryTotalMiB) || list[i].MemoryTotalMiB > primary.MemoryTotalMiB))
                    primary = list[i];
            }

            if (!string.IsNullOrWhiteSpace(primary.Name))
                m.DgpuName = primary.Name.Trim();
            if (!double.IsNaN(primary.TemperatureC)) m.DgpuTempC = primary.TemperatureC;
            if (!double.IsNaN(primary.UtilizationGpuPct)) m.DgpuUtilPct = primary.UtilizationGpuPct;
            if (!double.IsNaN(primary.UtilizationMemPct)) m.DgpuMemUtilPct = primary.UtilizationMemPct;
            if (!double.IsNaN(primary.MemoryUsedMiB)) m.VramUsedMiB = primary.MemoryUsedMiB;
            if (!double.IsNaN(primary.MemoryTotalMiB)) m.VramTotalMiB = primary.MemoryTotalMiB;
            if (!double.IsNaN(primary.PowerDrawW)) m.DgpuPowerW = primary.PowerDrawW;
            if (!double.IsNaN(primary.PowerLimitW)) m.DgpuPowerLimitW = primary.PowerLimitW;
            if (!double.IsNaN(primary.ClockGraphicsMhz)) m.DgpuClockMhz = primary.ClockGraphicsMhz;
            m.DgpuPState = primary.PerformanceState ?? "";
        }

        private void SampleLhmTemps(LiveMetrics m)
        {
            // 1) Prefer fresh elevated bridge file (CPU package often needs MSR/admin path)
            var bridge = SensorBridgeFile.TryReadFresh(5.0);
            if (bridge != null)
            {
                if (bridge.CpuTempC.HasValue && bridge.CpuTempC.Value > 0 && bridge.CpuTempC.Value < 125)
                    m.CpuTempC = bridge.CpuTempC;
                if (bridge.IgpuTempC.HasValue && bridge.IgpuTempC.Value > 0 && bridge.IgpuTempC.Value < 125)
                    m.IgpuTempC = bridge.IgpuTempC;
                if (bridge.IgpuUtilPct.HasValue)
                    m.IgpuUtilPct = bridge.IgpuUtilPct;
                // Prefer nvidia-smi dGPU temp if already set; else bridge
                if (!m.DgpuTempC.HasValue && bridge.DgpuTempC.HasValue)
                    m.DgpuTempC = bridge.DgpuTempC;
            }

            // 2) In-process LHM (works for NVIDIA core/hotspot without admin; CPU package often empty)
            _sensors.Sample(out double? cpu, out double? igpu, out double? igpuLoad);
            if (!m.CpuTempC.HasValue && cpu.HasValue && cpu.Value > 0 && cpu.Value < 125)
                m.CpuTempC = cpu.Value;
            if (!m.IgpuTempC.HasValue && igpu.HasValue && igpu.Value > 0 && igpu.Value < 125)
                m.IgpuTempC = igpu.Value;
            if (!m.IgpuUtilPct.HasValue && igpuLoad.HasValue && igpuLoad.Value >= 0 && igpuLoad.Value <= 100)
                m.IgpuUtilPct = igpuLoad.Value;
        }

        private static void SampleFps(LiveMetrics m)
        {
            // Only set when a real source has a value — UI hides the row otherwise.
            if (FpsReader.TryRead(out double fps, out string source))
            {
                m.Fps = fps;
                m.FpsSource = source;
            }
            else
            {
                m.Fps = null;
                m.FpsSource = "";
            }
        }

        private void BuildStatus(LiveMetrics m)
        {
            bool thermalWarn = false;
            bool thermalCrit = false;

            if (m.DgpuTempC.HasValue)
            {
                if (m.DgpuTempC.Value >= 91) thermalCrit = true;
                else if (m.DgpuTempC.Value >= 80) thermalWarn = true;
            }
            if (m.CpuTempC.HasValue)
            {
                if (m.CpuTempC.Value >= 93) thermalCrit = true;
                else if (m.CpuTempC.Value >= 78) thermalWarn = true;
            }

            if (thermalCrit)
            {
                m.StatusGlyph = "◆";
                m.StatusText = "THERMAL";
                m.StatusDetail = "Critical temperature";
            }
            else if (thermalWarn)
            {
                m.StatusGlyph = "▲";
                m.StatusText = "WARM";
                m.StatusDetail = "Elevated temperature";
            }
            else if (!m.OnAcPower)
            {
                m.StatusGlyph = "◐";
                m.StatusText = "BATTERY";
                m.StatusDetail = "On battery";
            }
            else if (m.HybridDetected)
            {
                m.StatusGlyph = "●";
                m.StatusText = "HYBRID OK";
                m.StatusDetail = "";
            }
            else
            {
                m.StatusGlyph = "●";
                m.StatusText = "ACTIVE";
                m.StatusDetail = "";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cpuCounter?.Dispose(); } catch { }
            try { _sensors.Dispose(); } catch { }
        }
    }
}
