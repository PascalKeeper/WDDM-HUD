# DEV NOTES — WDDM HUD by pascalkeeper

## Architecture

```
WddmHud.exe (self-contained net8.0-windows win-x64)
├── Program.cs                 UI instance OR --sensor-bridge mode
├── Core/                      Paths, logger, JSON config
├── Hardware/
│   ├── HardwareDetector       WMI hybrid inventory
│   ├── NvidiaSmiClient        dGPU temp/util/VRAM/power
│   ├── SensorHub              In-process LibreHardwareMonitor
│   ├── SensorBridgeHost       Elevated LHM → live_sensors.json
│   ├── SensorBridgeFile       Fresh-file reader (≤5s)
│   ├── FpsReader              RTSS SM / PresentMon CSV (no injection)
│   └── MetricsEngine          Aggregates samples for UI
└── UI/
    ├── OverlayForm            Owner-drawn, scale-on-resize, no N/A rows
    └── TrayController         Tray + "Enable CPU sensors (admin once)"
```

## Metrics honesty

| Metric | Source | Notes |
|--------|--------|-------|
| dGPU temp / util / VRAM / power | `nvidia-smi` | Works asInvoker |
| dGPU hotspot (optional) | LHM / bridge | Bonus |
| CPU usage | PerformanceCounter | Works asInvoker |
| CPU package temp | LHM MSR path | Often **requires** elevated `--sensor-bridge` once |
| iGPU temp | — | **Not exposed** on Intel HD 630 via LHM/WMI on AW17R4 |
| iGPU load | LHM D3D counters | Shown when available |
| System RAM | GlobalMemoryStatusEx | Always |
| RAM temp | — | **Removed** (no sensor) |
| FPS | RTSS SM or tools\presentmon_live.csv | Row hidden when no source |

## UI rules

- Never paint `N/A` rows.
- Resize from bottom-right only; **top-left stays fixed**; content uses uniform scale from design 340×220.
- RAM temperature permanently disabled in `HudConfig.Sanitize()`.

## Sensor bridge

```bat
dist\WddmHud.exe --sensor-bridge
```

Or tray → **Enable CPU sensors (admin once)…** (UAC).  
Writes `config\live_sensors.json` every 1s. HUD reads if file age &lt; 5s.

## Build

```bat
cd /d F:\imonlinegaming\WDDM_HUD_by_pascalkeeper
dotnet publish src\WddmHud\WddmHud.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

## Version

1.0.1 — Owner-drawn UI, LHM + optional elevated bridge, no N/A clutter, scale resize.
