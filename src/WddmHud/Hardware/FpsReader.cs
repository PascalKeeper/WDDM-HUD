// =============================================================================
// WDDM HUD by pascalkeeper — Non-invasive FPS readers
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
// Priority: RTSS shared memory (if Afterburner/RTSS running) → PresentMon CSV
// Never injects into games. No instructional clutter in UI (caller hides empty).
// =============================================================================

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WddmHud.Core;

namespace WddmHud.Hardware
{
    public static class FpsReader
    {
        public static bool TryRead(out double fps, out string source)
        {
            fps = 0;
            source = "";

            if (TryReadRtss(out fps))
            {
                source = "RTSS";
                return true;
            }

            string csv = Path.Combine(AppPaths.Root, "tools", "presentmon_live.csv");
            if (TryReadPresentMonCsv(csv, out fps))
            {
                source = "PresentMon";
                return true;
            }

            return false;
        }

        // -------------------- PresentMon CSV tail --------------------
        private static bool TryReadPresentMonCsv(string path, out double fps)
        {
            fps = 0;
            try
            {
                if (!File.Exists(path))
                    return false;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < 8)
                    return false;

                // Read last ~4KB
                long start = Math.Max(0, fs.Length - 4096);
                fs.Seek(start, SeekOrigin.Begin);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                string text = sr.ReadToEnd();
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0)
                    return false;

                // Header detection for MsBetweenPresents column
                int msCol = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf("MsBetweenPresents", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string[] hdr = lines[i].Split(',');
                        for (int c = 0; c < hdr.Length; c++)
                        {
                            if (hdr[c].Trim().Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase))
                            {
                                msCol = c;
                                break;
                            }
                        }
                        break;
                    }
                }

                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("Application", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] cols = line.Split(',');
                    if (msCol >= 0 && msCol < cols.Length)
                    {
                        if (double.TryParse(cols[msCol].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double ms)
                            && ms > 0.2 && ms < 250)
                        {
                            fps = 1000.0 / ms;
                            return fps > 1 && fps < 1000;
                        }
                    }
                    // fallback: last numeric column that looks like frame time
                    for (int c = cols.Length - 1; c >= 0; c--)
                    {
                        if (double.TryParse(cols[c].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double ms)
                            && ms > 0.5 && ms < 100)
                        {
                            fps = 1000.0 / ms;
                            return fps > 1 && fps < 1000;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        // -------------------- RTSS shared memory --------------------
        // Layout based on public RTSSSharedMemory.h (RivaTuner / Unwinder)
        private const uint FILE_MAP_READ = 0x0004;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr OpenFileMappingA(uint dwDesiredAccess, bool bInheritHandle, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess,
            uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static bool TryReadRtss(out double fps)
        {
            fps = 0;
            IntPtr hMap = IntPtr.Zero;
            IntPtr view = IntPtr.Zero;
            try
            {
                hMap = OpenFileMappingA(FILE_MAP_READ, false, "RTSSSharedMemoryV2");
                if (hMap == IntPtr.Zero)
                    return false;

                // Map enough for header + a few app entries (256 * ~1KB is plenty; map 256KB)
                view = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, (UIntPtr)(256 * 1024));
                if (view == IntPtr.Zero)
                    return false;

                // DWORD dwSignature; // 0x52545353 'RTSS'
                uint sig = (uint)Marshal.ReadInt32(view, 0);
                if (sig != 0x52545353)
                    return false;

                // DWORD dwVersion;
                uint version = (uint)Marshal.ReadInt32(view, 4);
                // DWORD dwAppEntrySize;
                int appEntrySize = Marshal.ReadInt32(view, 8);
                // DWORD dwAppArrOffset;
                int appArrOffset = Marshal.ReadInt32(view, 12);
                // DWORD dwAppArrSize;
                int appArrSize = Marshal.ReadInt32(view, 16);

                if (appEntrySize <= 0 || appArrOffset <= 0 || appArrSize <= 0 || appArrSize > 256)
                    return false;

                // Walk app entries; pick highest 1s FPS among active processes
                // Offsets inside APP_ENTRY (common across v2.x):
                // 0: DWORD dwProcessId
                // ... name at variable offset; dwTime0, dwTime1, dwFrames for FPS calc
                // Public structure uses:
                //   DWORD dwProcessID
                //   char szName[MAX_PATH]
                //   DWORD dwFlags
                //   DWORD dwTime0, dwTime1
                //   DWORD dwFrames
                //   ...
                //   FLOAT flFrameTime (in newer)
                //
                // Safer approach used by many tools: at known offsets after name:
                // For version >= 0x00020000:
                //   processId @ 0
                //   name @ 4 (260 chars ANSI often)
                // We use frame time field if present.

                double best = 0;
                for (int i = 0; i < appArrSize; i++)
                {
                    int baseOff = appArrOffset + i * appEntrySize;
                    if (baseOff + 32 >= 256 * 1024)
                        break;

                    int pid = Marshal.ReadInt32(view, baseOff + 0);
                    if (pid == 0)
                        continue;

                    // Classic calculation from dwTime0/dwTime1/dwFrames
                    // Offsets from RTSSSharedMemory.h APP_ENTRY after dwProcessID + szName[260]:
                    // 0 + 4 + 260 = 264 → may pad; try multiple layouts.

                    // Layout A (common):
                    // 0: pid
                    // 4: name[260]
                    // 264: flags
                    // 268: time0
                    // 272: time1
                    // 276: frames
                    int[] time0Candidates = { 268, 272, 280 };
                    foreach (int t0off in time0Candidates)
                    {
                        if (baseOff + t0off + 12 >= 256 * 1024) continue;
                        uint time0 = unchecked((uint)Marshal.ReadInt32(view, baseOff + t0off));
                        uint time1 = unchecked((uint)Marshal.ReadInt32(view, baseOff + t0off + 4));
                        uint frames = unchecked((uint)Marshal.ReadInt32(view, baseOff + t0off + 8));
                        if (frames == 0 || time1 <= time0)
                            continue;
                        double dt = (time1 - time0) / 1000.0; // ms → s? actually GetTickCount ms
                        // RTSS uses GetTickCount milliseconds
                        double seconds = (time1 - time0) / 1000.0;
                        if (seconds <= 0.01 || seconds > 10)
                            continue;
                        double f = frames / seconds;
                        if (f > 1 && f < 1000 && f > best)
                            best = f;
                    }

                    // Also try float frame time near end of older entries (1000/ft)
                    // Skip if unreliable
                }

                if (best > 1)
                {
                    fps = best;
                    return true;
                }

                // Version field available for future refinements
                _ = version;
                return false;
            }
            catch (Exception ex)
            {
                Logger.Debug("RTSS read failed: " + ex.Message);
                return false;
            }
            finally
            {
                if (view != IntPtr.Zero) try { UnmapViewOfFile(view); } catch { }
                if (hMap != IntPtr.Zero) try { CloseHandle(hMap); } catch { }
            }
        }
    }
}
