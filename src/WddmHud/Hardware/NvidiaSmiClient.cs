// =============================================================================
// WDDM HUD by pascalkeeper — nvidia-smi CSV query client
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
// Offline, subprocess-based, CREATE_NO_WINDOW. Elite error handling.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using WddmHud.Core;

namespace WddmHud.Hardware
{
    public sealed class NvidiaGpuSample
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public double TemperatureC { get; set; } = double.NaN;
        public double UtilizationGpuPct { get; set; } = double.NaN;
        public double UtilizationMemPct { get; set; } = double.NaN;
        public double MemoryUsedMiB { get; set; } = double.NaN;
        public double MemoryTotalMiB { get; set; } = double.NaN;
        public double PowerDrawW { get; set; } = double.NaN;
        public double PowerLimitW { get; set; } = double.NaN;
        public double ClockGraphicsMhz { get; set; } = double.NaN;
        public double ClockMemoryMhz { get; set; } = double.NaN;
        public string PerformanceState { get; set; } = "";
        public bool Valid { get; set; }
    }

    public static class NvidiaSmiClient
    {
        private static string? _resolvedPath;
        private static DateTime _lastFailUtc = DateTime.MinValue;
        private static readonly TimeSpan FailBackoff = TimeSpan.FromSeconds(15);

        public static string? ResolveNvidiaSmiPath()
        {
            if (!string.IsNullOrEmpty(_resolvedPath) && File.Exists(_resolvedPath))
                return _resolvedPath;

            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
                @"C:\Windows\System32\nvidia-smi.exe",
                @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
                @"C:\Program Files (x86)\NVIDIA Corporation\NVSMI\nvidia-smi.exe"
            };

            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(c))
                    {
                        _resolvedPath = c;
                        Logger.Info("nvidia-smi resolved: " + c);
                        return _resolvedPath;
                    }
                }
                catch { /* continue */ }
            }

            // PATH lookup
            try
            {
                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    foreach (string dir in pathEnv.Split(Path.PathSeparator))
                    {
                        try
                        {
                            string p = Path.Combine(dir.Trim(), "nvidia-smi.exe");
                            if (File.Exists(p))
                            {
                                _resolvedPath = p;
                                return _resolvedPath;
                            }
                        }
                        catch { /* continue */ }
                    }
                }
            }
            catch { /* ignore */ }

            Logger.Warn("nvidia-smi.exe not found on system.");
            return null;
        }

        /// <summary>
        /// Query all NVIDIA GPUs for telemetry. Returns empty list on failure.
        /// </summary>
        public static List<NvidiaGpuSample> QueryAll()
        {
            var results = new List<NvidiaGpuSample>();

            if (DateTime.UtcNow - _lastFailUtc < FailBackoff && string.IsNullOrEmpty(_resolvedPath))
                return results;

            string? smi = ResolveNvidiaSmiPath();
            if (string.IsNullOrEmpty(smi))
            {
                _lastFailUtc = DateTime.UtcNow;
                return results;
            }

            // Comprehensive but single-call CSV for low overhead
            const string query =
                "index,name,temperature.gpu,utilization.gpu,utilization.memory," +
                "memory.used,memory.total,power.draw,power.limit," +
                "clocks.current.graphics,clocks.current.memory,pstate";

            string? output = RunSmi(smi, $"--query-gpu={query} --format=csv,noheader,nounits");
            if (string.IsNullOrWhiteSpace(output))
            {
                _lastFailUtc = DateTime.UtcNow;
                return results;
            }

            foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                string[] parts = SplitCsv(line);
                if (parts.Length < 12)
                {
                    Logger.Debug("nvidia-smi short line: " + line);
                    continue;
                }

                var sample = new NvidiaGpuSample
                {
                    Index = ParseInt(parts[0], -1),
                    Name = parts[1].Trim(),
                    TemperatureC = ParseDouble(parts[2]),
                    UtilizationGpuPct = ParseDouble(parts[3]),
                    UtilizationMemPct = ParseDouble(parts[4]),
                    MemoryUsedMiB = ParseDouble(parts[5]),
                    MemoryTotalMiB = ParseDouble(parts[6]),
                    PowerDrawW = ParseDouble(parts[7]),
                    PowerLimitW = ParseDouble(parts[8]),
                    ClockGraphicsMhz = ParseDouble(parts[9]),
                    ClockMemoryMhz = ParseDouble(parts[10]),
                    PerformanceState = parts[11].Trim(),
                    Valid = true
                };

                if (sample.Index < 0)
                    sample.Valid = false;

                results.Add(sample);
            }

            return results;
        }

        private static string? RunSmi(string smiPath, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = smiPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return null;

                // Hard timeout — never hang the HUD poll loop
                if (!proc.WaitForExit(4000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    Logger.Warn("nvidia-smi timed out.");
                    return null;
                }

                string stdout = proc.StandardOutput.ReadToEnd();
                if (proc.ExitCode != 0)
                {
                    string err = proc.StandardError.ReadToEnd();
                    Logger.Warn($"nvidia-smi exit {proc.ExitCode}: {err}");
                    // still return stdout if any data present
                }

                return stdout;
            }
            catch (Exception ex)
            {
                Logger.Error("nvidia-smi invoke failed", ex);
                return null;
            }
        }

        private static string[] SplitCsv(string line)
        {
            // nvidia-smi CSV is simple comma-separated with possible spaces after commas
            return line.Split(',');
        }

        private static double ParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return double.NaN;
            s = s.Trim();
            if (s.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                || s.Equals("[N/A]", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Not Supported", StringComparison.OrdinalIgnoreCase))
                return double.NaN;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            return double.NaN;
        }

        private static int ParseInt(string s, int fallback)
        {
            if (int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            return fallback;
        }
    }
}
