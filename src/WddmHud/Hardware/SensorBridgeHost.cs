// =============================================================================
// Elevated sensor bridge host — run: WddmHud.exe --sensor-bridge
// Copyright (C) 2026 Joseph Rocco Peransi
// www.github.com/pascalkeeper
// WinRing0 load was granted for this app. The bridge opens LibreHardwareMonitor
// for package temperature and any fan tachometer it can actually read.
// EC CTMP remains the fallback when that driver does not open.
// This host does not send a fan-speed write.
// =============================================================================

using WddmHud.Core;

namespace WddmHud.Hardware
{
    public static class SensorBridgeHost
    {
        public static int Run()
        {
            Logger.Info("Sensor bridge starting. WinRing0 granted. No fan-speed write.");
            using var hub = new SensorHub();
            hub.EnsureOpened();

            using var smm = new DellSmmReader();
            if (!smm.TryOpen(out string smmError))
                Logger.Warn("Dell SMM fan read unavailable: " + smmError);

            using var ec = new EcReader();
            bool ecOpen = false;
            if (!hub.IsAvailable)
            {
                ecOpen = ec.TryOpen(out string error);
                if (!ecOpen)
                    Logger.Warn("EC open failed (" + error + "). CPU temperature stays unavailable.");
            }

            bool announced = false;
            DateTime nextHeavyUtc = DateTime.MinValue;
            double? cachedCpu = null;
            double? cachedIgpu = null;
            double? cachedIgpuLoad = null;
            string cachedSource = "";
            int? shownFan1 = null;
            int? shownFan2 = null;
            while (true)
            {
                try
                {
                    var file = new LiveSensorFile { Utc = DateTime.UtcNow, Source = "" };
                    bool heavy = DateTime.UtcNow >= nextHeavyUtc;
                    if (heavy)
                    {
                        if (hub.IsAvailable)
                        {
                            hub.Sample(out double? cpu, out double? igpu, out double? igpuLoad);
                            cachedIgpu = igpu;
                            cachedIgpuLoad = igpuLoad;
                            if (cpu.HasValue && cpu.Value > 0 && cpu.Value < 125)
                            {
                                cachedCpu = cpu.Value;
                                cachedSource = "lhm";
                            }
                        }
                        else if (ecOpen)
                        {
                            int? ctmp = ec.ReadCpuTempC();
                            if (ctmp.HasValue)
                            {
                                cachedCpu = ctmp.Value;
                                cachedSource = "ec-ctmp";
                                file.Note = "DSDT ECMM CTMP. Not fan RPM.";
                            }
                        }
                        nextHeavyUtc = DateTime.UtcNow.AddSeconds(1);
                    }

                    if (cachedCpu.HasValue)
                    {
                        file.CpuTempC = cachedCpu;
                        file.Source = cachedSource;
                    }
                    file.IgpuTempC = cachedIgpu;
                    file.IgpuUtilPct = cachedIgpuLoad;

                    if (smm.IsOpen)
                    {
                        smm.Read(out int? smm0, out int? smm1);
                        file.Fan1Name = "Fan";
                        file.Fan1Rpm = smm0 is > 0 and < 20000 ? smm0 : shownFan1;
                        file.Fan2Name = "Fan 2";
                        file.Fan2Rpm = smm1 is > 0 and < 20000 ? smm1 : shownFan2;
                    }

                    bool fanChanged = file.Fan1Rpm != shownFan1 || file.Fan2Rpm != shownFan2;
                    if ((heavy || fanChanged) && (file.CpuTempC.HasValue || file.Fan1Rpm.HasValue))
                    {
                        SensorBridgeFile.Write(file);
                        shownFan1 = file.Fan1Rpm;
                        shownFan2 = file.Fan2Rpm;
                        if (!announced)
                        {
                            Logger.Info("Bridge sample source=" + file.Source + " cpu=" + file.CpuTempC + " fan1=" + file.Fan1Rpm + " fan2=" + file.Fan2Rpm);
                            announced = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Bridge sample failed: " + ex.Message);
                }

                // Fan reads only. Temperature and nvidia stay on the slow poll.
                Thread.Sleep(250);
            }
        }
    }
}
