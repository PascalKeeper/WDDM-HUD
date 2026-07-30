# EXTRA RULES — Project Coordinator / Human Tester Directives

**Product:** WDDM HUD by pascalkeeper  
**Machine class:** Alienware 17 R4 · Hybrid Intel HD 630 + NVIDIA GTX 1070 (Pascal) · Windows 10  
**Owner:** Joseph Rocco Peransi / pascalkeeper / imonlinegaming  

These rules are **non-negotiable** for anyone developing, debugging, packaging, or supporting this project.

---

## 1. Never trigger “How do you want to open this file?”

| Rule | Detail |
|------|--------|
| Daily launch path is **only** `WddmHud.exe` | Desktop / Start Menu / Task Scheduler must target the `.exe` |
| Installer entry is **only** `Install-WddmHud.bat` | The `.bat` hard-calls `powershell.exe -File …` |
| Uninstall entry is **only** `Uninstall-WddmHud.bat` | Same binding rule |
| Do **not** double-click `.ps1`, `.cs`, `.json`, `.md` as product entry points | Those are developer artifacts |
| Shortcuts use `WScript.Shell` → `.lnk` → absolute path to `.exe` | Working directory = `dist\` |
| No custom URI protocols or file associations required for HUD to run | Zero “Open with” registration for source files |

**Why:** Broken associations on this machine and user preference. If a change causes an Open-With dialog, it is a **release-blocking bug**.

---

## 2. Drive placement & game installs

| Rule | Detail |
|------|--------|
| Prefer **F:\** for tools, HUD, installers, source | `F:\imonlinegaming\WDDM_HUD_by_pascalkeeper` |
| **Do not move games to F:\** unless F: is faster | **Measured 2026-07-30:** F: = USB HDD (My Passport), C: = NVMe SSD. Moving Fortnite/RDR2 to F: would **regress** load times and streaming stutter. |
| WRC9 is on E: (USB HDD) | Moving to F: is not a win; moving to C:/D: NVMe would help but is **out of scope** unless user requests |

### Drive benchmark snapshot (256 MB sequential)

| Drive | Media | Write MB/s | Notes |
|-------|--------|------------|--------|
| C: | NVMe SSD | ~1033 | Fortnite + RDR2 |
| D: | NVMe SSD | ~1258 | Fastest write |
| E: | USB HDD | ~40 write | WRC9 |
| F: | USB HDD Passport | ~94 write | Tools only |

---

## 3. Power plans

| Rule | Detail |
|------|--------|
| **Do not change the active power plan** | User reports no perceived difference High Performance vs Ultimate |
| **Do not activate Rocco Infinity Mode** | Untested / no lasting positive impression; no sandbox FPS lab required unless user asks later |
| **Do delete duplicate plan remnants** | Ultimate Performance / Rocco Infinity **clones only**; keep one of each name; never delete active or stock Balanced/High Performance |
| ASPM | May already be Off; HUD does not force ASPM changes |

---

## 4. Thermals, longevity, OC

| Rule | Detail |
|------|--------|
| No undervolt | Forbidden |
| No Afterburner OC curves | Forbidden |
| No raising dGPU power limit above stock TGP | GTX 1070 mobile already at 115 W cap |
| HUD must show thermal status glyph | WARN / CRIT thresholds for CPU & dGPU |
| Prefer reversible, monitorable changes | Snapshots before install |

---

## 5. Anti-cheat & FPS philosophy

| Rule | Detail |
|------|--------|
| **No DLL injection into games** | Fortnite EAC / other AC = ban risk |
| “RTSS replacement” means **sensor + overlay class**, not inject-and-hook | Safe coexistence with RTSS if present |
| Honest FPS | Show N/A unless PresentMon CSV or future RTSS SM reader works; never fake game FPS from overlay paint rate |
| Optional PresentMon | Drop into `tools\` — never required for install |

---

## 6. Mother Earth / existing PascalKeeper stack

| Rule | Detail |
|------|--------|
| Align with Mother Earth philosophy | GpuPreference, iGPU for shell, dry-run, restore |
| Do **not** reimplement a second power/ASPM engine inside the HUD | HUD is visibility + game pref scripts |
| DualGPU-Forcer explorer→dGPU is obsolete | Do not revive |
| Corrupt registry keys `C:\Program` / `E:\Program` | Always scrub |

---

## 7. Windows 10 installer surface

| Rule | Detail |
|------|--------|
| Self-contained single-file **win-x64** | No “install .NET runtime” prompt on target |
| Works on Windows 10 builds with x64 | Manifest lists Win10 GUIDs |
| Offline | No telemetry, no network calls at runtime |
| Dry-run, uninstall, emergency restore | Required for every release |

---

## 8. Development & debugging discipline

Before writing new features, re-read:

1. This file (`docs/EXTRA_RULES.md`)
2. `docs/DEV_NOTES.md`
3. Mother Earth README (safety, ASPM thermal cost, dry-run)
4. Local code-stack materials on the machine (minerforge OpenCL PDFs, motherearth source) for defensive patterns

**Codersguild.net alignment (practice, not copy):**

- Clear goals before coding  
- Active notes / comments where complexity warrants  
- Practice along production paths (build → install → run .exe)  
- Prefer curated, trustworthy patterns over junk one-liners  
- Production-ready error handling over lean minimalism  

---

## 9. Things to avoid (quick blacklist)

- Placeholders, mock metrics, simulated FPS  
- `UseShellExecute=true` on `.ps1` as the product launch path  
- Associating source extensions with the HUD  
- Elevating the HUD itself for normal use (installer may elevate for powercfg delete only)  
- HAGS experiments on this hybrid laptop without explicit approval  
- Deleting the active power scheme  
- Network beacons, analytics, auto-updaters  

---

## 10. Approval log (this delivery)

| Request | Decision |
|---------|----------|
| Move games to F: | **No** — F: slower than C: NVMe |
| Power plan switch to Ultimate | **No** |
| Rocco Infinity | **Skip** (no sandbox compare required this pass) |
| Undervolt / OC | **No** |
| Delete dupe power plans | **Yes** |
| WDDM HUD + UI requests | **Yes** |
| Full RTSS replacement | **Anti-cheat-safe overlay class** (yes, beneficial) |
| Installer all Win10 | **Yes** (self-contained x64) |
| Auto hardware detect | **Yes** |
| No Open-With dialogs | **Hard rule** |
