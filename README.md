# WDDM HUD by pascalkeeper

Copyright (C) 2026 Joseph Rocco Peransi  
www.github.com/pascalkeeper

WDDM HUD is a Windows 10 on-screen display for a laptop's own temperatures, onboard fan speeds, and the discrete GPU. It draws only rows that have a real reading. It stays in the system tray, and it does not inject into games.

## What it was developed around

This build was made on an Alienware 17 R4 running Windows 10 22H2. That machine has an Intel i7-7700HQ, Intel HD Graphics 630, and a GeForce GTX 1070. The 1070 is the discrete GPU wired to the external display. The two onboard blowers are controlled by the embedded controller, not by an NVIDIA fan header.

The CPU package temperature comes from LibreHardwareMonitor. The blower speeds come from a read-only Dell SMM call through PawnIO. GPU temperature, load, and power come from `nvidia-smi`. The same Windows 10 x64 build can run on another PC with similar hardware. Fan RPM appears only when that PC's BIOS answers the same Dell SMM read.

Install requirements are in [docs/INSTALLER_NOTES.md](docs/INSTALLER_NOTES.md).

## One-click install

Double-click `Install-WDDM-HUD.bat` in this folder.

That file installs `dist\WddmHud.exe`, creates the Start Menu and Desktop shortcuts, and starts the HUD. It does not change the power plan, the display refresh rate, the default audio device, or Defender. Real-time protection can stay on when the exclusions in `installer\Enable-RealtimeWithHudExclusions.ps1` are present. Run that script once as administrator on a machine that needs the CPU package driver.

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

System requirements for a successful install, including Defender, the temperature driver, PawnIO fan reads, and what must not be changed, are in [docs/INSTALLER_NOTES.md](docs/INSTALLER_NOTES.md).

## Quick install (end user)

1. Double-click `Install-WDDM-HUD.bat`.
2. Launch from the **Desktop** or **Start Menu** shortcut. The shortcut opens `WddmHud.exe` only.
3. The menu installer `installer\Install-WddmHud.bat` is still there for a dry-run or a rebuild.

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

Joseph Rocco Peransi  
www.github.com/pascalkeeper
