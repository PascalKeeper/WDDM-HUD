# Installer notes — WDDM HUD by pascalkeeper

Copyright (C) 2026 Joseph Rocco Peransi / pascalkeeper

These are the conditions a machine must meet before an install is successful. The installer does not change Defender, the vulnerable-driver blocklist, the power plan, the panel refresh rate, or the default audio device. It records the checks and warns. A person confirms the Defender and driver steps in Windows Security before the first elevated sensor bridge.

The ship target is the **Alienware 17 R4**. The same build is meant to run on another PC with similar hardware and the same OS. Rows appear only when a real reading exists. A missing fan or a missing temperature is left off the overlay. The HUD never prints N/A for those rows, and it never invents RPM.

## Proven machine

| Item | Value |
|---|---|
| Chassis | Alienware 17 R4, SKU 0778 |
| OS | Windows 10 Home 22H2, build 19045, 64-bit |
| CPU | Intel Core i7-7700HQ |
| iGPU | Intel HD Graphics 630, no temperature sensor exposed |
| dGPU | NVIDIA GeForce GTX 1070 8 GB, HDMI wired to the 1070 |
| BIOS | Alienware 1.18.0 |
| Fans | Two onboard blowers, EC-controlled, shared heatsink |
| BIOS fan setting | Fan Performance Mode = Performance (factory default is Balanced) |

## Operating system

- Windows 10 x64. The proven build is 22H2 (19045).
- Windows 11 was not part of this ship test. Do not treat it as covered.
- A 32-bit OS cannot run the self-contained win-x64 binary.
- .NET 8 SDK is required only to build. The published `dist\WddmHud.exe` is self-contained. End users do not install a runtime.
- Preferred tree on this PC: `F:\imonlinegaming\WDDM_HUD_by_pascalkeeper`. Another disk is fine on another PC. The installer warns if `F:\` is missing and still uses that path on this machine.

## What the installer is allowed to do

- Build or copy `dist\WddmHud.exe`.
- Create Desktop and Start Menu shortcuts that launch the exe only.
- Register the uninstall key.
- Stop old HUD processes before replacing the exe, then sweep dead tray icons (`WddmHud.exe --kill-all`).
- Optionally register GpuPreference for Fortnite, RDR2, and WRC9 when that flag is passed.
- Optionally delete duplicate Ultimate Performance / Rocco Infinity power-plan remnants. It never changes the active power plan.

## What the installer must not do

- Switch the active power plan.
- Change panel refresh, EDID, or any 120 Hz override. On this HDMI link, forcing 120 Hz black-screens. The panel stays 1920x1080@60.
- Change the default playback device. On this PC the Sharp HDMI display stays the default. Do not seize the Sonos Roam.
- Enable the Sonos Roam auto-connect task.
- Install or start Alienware Command Center or `AWCCDriver.sys`.
- Install Dell Instrumentation.
- Stop the Compal embedded-controller service `EMSC`.
- Send a fan-speed write. Commands that must never be sent by the installer or the HUD: Dell SMM set-fan `0x01A3`, get-fan-type `0x03A3`, disable BIOS fan control `0x30A3` / `0x34A3`, enable `0x31A3` / `0x35A3`, EC write `0x81`, and a raw OUT to port `0xB2`.
- Call WMI fan setters (`UserSetCPUFan`, `ECSetCPUFan`, `Thermal_Control`) or `EsifExecutePrimitive`.
- Turn Tamper Protection or real-time protection off or on by itself.
- Reboot the PC.

## Temperature driver (CPU package)

CPU package temperature uses LibreHardwareMonitor 0.9.4. That library drops a small driver beside the exe. On this version the file is `dist\WddmHud.sys`, not `WinRing0x64.sys`. The elevated sensor bridge loads it. The normal HUD window stays non-elevated.

Defender has quarantined this class of driver before. A successful install needs all of the following:

1. **Memory integrity (HVCI) is off.** On the proven machine it was already 0. The temperature driver does not load while memory integrity is on.
2. **Microsoft vulnerable-driver blocklist is off.** Registry value `HKLM\SYSTEM\CurrentControlSet\Control\CI\Config\VulnerableDriverBlocklistEnable` is DWORD `0`. On this PC the driver loaded in the same session after that value was set, with no reboot. On a PC where the blocklist has already refused the driver, one reboot may be required before the first successful load. The installer does not set this value.
3. **Real-time protection stays off, or the driver files are excluded.** Exclusions used on this PC:
   - `dist\WinRing0.sys`
   - `dist\WinRing0x64.sys`
   - `dist\WddmHud.sys`
   If real-time protection is on and those files are not excluded, Defender can quarantine the driver. Fan RPM does not use this driver. CPU package temperature does. If the driver is missing, the 17 R4 can fall back to EC field CTMP. That fallback is labeled `EC`, not package.
4. **Tamper Protection may be on.** Turning it on does not ask for a reboot. It does not unload a driver that is already loaded. It does not turn the vulnerable-driver blocklist back on. Leave real-time protection off when you flip Tamper Protection, unless the exclusions above are confirmed. Tamper Protection blocks later scripted changes to Defender settings. The Windows Security app can still change them.
5. **Cleanup must not delete the driver.** InstanceGuard and `scripts\Stop-WddmHudGhosts.ps1` skip `WinRing0*` and `WddmHud.sys`. Any other `*.sys` next to the exe is still removed.

The sensor bridge is started once with UAC (`WddmHud.exe --sensor-bridge`). After `config\live_sensors.json` is fresh, later HUD launches do not ask again.

## Fan RPM (Dell SMM, not the temperature driver)

Onboard blower RPM does not come from NVIDIA and does not come from WinRing0. NVIDIA reports GPU temperature. `nvmlDeviceGetFanSpeed` is not supported on this laptop. The blowers are on the embedded controller.

The read path:

- Official signed **PawnIO 2.2.0** (`PawnIO_setup.exe -install -silent`). Do not pass `-unrestricted`. On this PC the installer returned exit 0 and did not require a reboot.
- Signed **DellSMM.bin 0.2.11** from PawnIO.Modules, embedded in the HUD. LGPL-2.1. The bytes are not modified. The module runs the call on CPU 0 and refuses to load unless the BIOS answers the Dell signature (`DIAG` / `DELL`).
- Command `0x02A3` only. Fan index goes in EBX. The HUD probes indexes 0 through 3 once and keeps the indexes that answer.

On this Alienware 17 R4 the BIOS answers index 0 and index 2. Indexes 1 and 3 are rejected. The overlay labels are `Fan` and `Fan 2`.

Measured nominal speeds from `0x04A3` on this BIOS:

| Step | Fan (index 0) | Fan 2 (index 2) |
|---|---:|---:|
| 1 | 1800 | 2000 |
| 2 | 4200 | 4400 |
| 3 | 4600 | 4500 |

Step 3 is the fastest step, and Fan Performance Mode already selects it at idle. A rapid read of the live register moves in steps of about 100 RPM (4500 or 4600), not 1 RPM. The HUD reads both fans about four times a second and repaints when the integer changes. It does not invent digits.

Setting step 2 with `0x01A3` on this BIOS slowed the fans and both blowers cut out during the change. They were restored by setting step 3 again. Do not send that command from an install.

Another Dell or Alienware laptop on Windows 10 x64 can show fans when its BIOS answers the same signature. The answered indexes may differ. If none answer, the overlay shows no fan row. That is a successful install with no blower reading, not a failed paint.

PawnIO must be installed before the bridge starts. The HUD does not download it.

## GPU, FPS, and the overlay

- Discrete GPU temperature, load, clocks, and power come from `nvidia-smi`. The HUD already calls it on the slow poll. Do not start a second `nvidia-smi` loop while a game is running. A visible console flash is the failure mode.
- Intel HD 630 on this chassis has no temperature reading. The iGPU row stays hidden.
- FPS is shown only from RTSS shared memory or a PresentMon CSV. It is hidden when neither is present.
- The overlay is per-pixel alpha, draggable, and resizable from the bottom-right corner. The plate is alpha 1 so it does not show on a black desktop. The first layout hugs the text. After a corner drag, the size is saved and the text scales.
- Tray menu, top to bottom of the actions that matter: **Minimize to tray**, **Exit**. A killed process can leave a dead tray icon. `--kill-all` and each UI start sweep those icons.
- Click-through can turn on while a known game is in front. Right-click the plate or the tray icon to turn it off.

## Similar hardware

Use this build on another machine when all of these are true:

- Windows 10 x64, same generation as 22H2.
- An Intel CPU LibreHardwareMonitor can read after the driver exception above, or a Dell EC that exposes CTMP the way this 17 R4 does.
- Optional: a BIOS that answers Dell SMM through PawnIO, for fan RPM.
- Optional: an NVIDIA GPU with a working `nvidia-smi`, for the dGPU rows.

Do not copy another laptop’s EC byte offsets into this installer. This 17 R4 does not keep RPM in the named EC map. HWiNFO’s finer Compal curve is unpublished. The installer does not scan EC RAM and does not touch the EC SMBus mailbox at `0x61–0x64`.

## After install, these must pass

1. `dist\WddmHud.exe` starts a window and one tray icon.
2. The tray menu contains **Minimize to tray** and **Exit**.
3. `config\live_sensors.json` is being rewritten. On this 17 R4 it shows a CPU temperature and two fan RPM values between 1 and 20000. `source` is `lhm` when the package driver is open.
4. The log contains `Dell SMM open` with the indexes that answered, then `OverlayForm ready`, and no unhandled exception.
5. `WddmHud.exe --kill-all` stops the UI and the bridge and leaves no dead tray icon.
6. The active power plan is the same plan as before install.

On the proven machine a good sample looks like: CPU package in the 30s or 40s at idle, Fan around 4600, Fan 2 around 4500, with the BIOS in Performance fan mode. Those speeds are the top step, not a sign that the installer set a fan.
