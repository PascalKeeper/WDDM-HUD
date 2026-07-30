# WDDM HUD by pascalkeeper

**Lightweight hybrid-graphics performance overlay for Windows 10**

Copyright (C) 2026 Joseph Rocco Peransi / pascalkeeper  

Offline · Zero telemetry · Self-contained `.exe` · Auto hardware detect · System tray  

Designed for **Alienware 17 R4** class hybrids (**Intel iGPU + NVIDIA Pascal dGPU**) and aligned with the **Mother Earth / WDDM Hybrid Graphics Orchestrator** ecosystem.

---

## What it does

- Always-visible **status glyph** (hybrid OK / warm / thermal / battery)
- Live metrics (only rows with **real values** are painted — no N/A clutter):
  - FPS when RTSS is running or PresentMon CSV is present (never faked)
  - CPU utilization; CPU package temp via LibreHardwareMonitor (tray: **Enable CPU sensors** if empty)
  - iGPU load when available (**HD 630 does not expose iGPU temperature** on this chassis)
  - dGPU temperature, utilization, clocks, P-state, power draw (`nvidia-smi`)
  - VRAM used / total
  - System RAM used / total
  - RAM temperature **removed** (no sensor on AW17R4)
- Fully **draggable** and **resizable**
- **System tray** dock (minimize / restore / exit)
- Click-through toggle
- Auto-detects CPU, iGPU, dGPU, RAM, AC/battery, `nvidia-smi`

## What it deliberately does not do

- No DLL injection into games (Fortnite EAC-safe design)
- No power plan switching (coordinator decision)
- No undervolt / Afterburner OC
- No telemetry / network
- No “Open with” source-file launch path

---

## Quick install (end user)

1. Prefer tree at:  
   `F:\imonlinegaming\WDDM_HUD_by_pascalkeeper`
2. Double-click:  
   `installer\Install-WddmHud.bat`
3. Choose option **[1] Full install**
4. Launch from **Desktop** or **Start Menu** shortcut (**WddmHud.exe only**)

### Installer features

- Works on **Windows 10 x64** (self-contained single-file — no .NET runtime install)
- **Dry-run** mode (option 2)
- Pre-install snapshot under `backups\`
- Optional: force dGPU for Fortnite / RDR2 / WRC9
- Optional: delete **duplicate** Ultimate Performance / Rocco Infinity plan remnants  
  (**never** changes the active power plan)
- `EmergencyRestore.bat` + clean uninstall

### Uninstall

`installer\Uninstall-WddmHud.bat`  
or Add/Remove Programs → **WDDM HUD by pascalkeeper**

---

## Game install move policy

**Games were NOT moved to F:\.**

| Drive | Type | Role |
|-------|------|------|
| C: | NVMe SSD | Fortnite + RDR2 (keep) |
| E: | USB HDD | WRC9 |
| F: | USB HDD Passport | Tools / HUD only |

F: is **slower** for game streaming than C: NVMe. Moving titles to F: would hurt performance.

---

## Build from source

Requirements: .NET 8 SDK (x64), Windows 10.

```bat
cd /d F:\imonlinegaming\WDDM_HUD_by_pascalkeeper
dotnet publish src\WddmHud\WddmHud.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

Run:

```bat
dist\WddmHud.exe
```

---

## Layout

```
WDDM_HUD_by_pascalkeeper\
  dist\WddmHud.exe          Offline binary (after build/install)
  installer\                One-click bat/ps1, uninstall, emergency restore
  scripts\                  Game GPU prefs + dupe power plan cleaner
  src\WddmHud\              Full C# source
  config\                   Runtime config + hardware snapshot
  logs\                     Install + runtime logs
  docs\EXTRA_RULES.md       Coordinator hard rules
  docs\DEV_NOTES.md         Architecture & debug notes
```

---

## Relation to Mother Earth

| Component | Responsibility |
|-----------|----------------|
| **Mother Earth** | Power/ASPM orchestration, SHIM, context menu, restore |
| **WDDM HUD** | Always-on visibility, hardware detect, game GpuPreference helper scripts |

Use them together. Do not fork power logic into the HUD.

---

## License

GPL-3.0-or-later (see `LICENSE`).  
Use at your own risk on hybrid laptops; monitor thermals.

---

## Author

Joseph Rocco Peransi · github.com/pascalkeeper · x.com/xAIModerator
