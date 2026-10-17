# AutoDsrToggle

<p align="center">
  <b>A robust, portable, and self-healing .NET 10 headless utility for seamless NVIDIA DSR resolution toggling.</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=.net" alt=".NET 10">
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-0078D6?style=flat-square&logo=windows" alt="Windows x64">
  <img src="https://img.shields.io/badge/License-GPL%20v3-blue.svg?style=flat-square" alt="License: GPL v3">
</p>

---

## 🚀 Overview

**AutoDsrToggle** acts as a reliable toggle switch between your native display resolution and your configured **NVIDIA DSR** (Dynamic Super Resolution) target. Designed as a lightweight, headless background executable, it allows power users and enthusiasts to instantly switch resolutions via hotkeys, desktop shortcuts, or custom scripts without interacting with the NVIDIA App.

---

## ⚙️ How It Works (Toggle Behavior)

- If your display is currently at **NATIVE** resolution $\rightarrow$ switches to **DSR**.
- If your display is currently at **DSR** resolution $\rightarrow$ switches back to **NATIVE**.
- If `DsrMultiplier` is set to `1.0` for a specific display $\rightarrow$ stays at native resolution (ignores toggling).

---

## 🛠️ Getting Started & Configuration

1. **First Run:** Launch `AutoDsrToggle.exe` once. It will automatically initialize, map your active displays via NVAPI, prompt a setup message box, and generate the configuration files in `%LOCALAPPDATA%\AutoDsrToggle`.
2. **Edit Config:** Open `adt_config.ini` located in that folder.
3. **Set Multiplier:** Locate your display section (e.g., `[Display1]`) and change `DsrMultiplier=1.0` to your desired scaling factor (e.g., `1.25`, `2.0`, or `4.0`).
4. **Save & Use:** Save the file and execute the utility whenever you want to toggle your resolutions.

### Example Configuration (`adt_config.ini`)
```ini
[Display1]
; Monitor Model: Example Display
UniqueId=DISPLAY\ABC1234\{4d36e96e-e325-11ce-bfc1-08002be10318}\0001
PrimaryDisplay=Yes
NativeWidth=2560
NativeHeight=1440
DsrMultiplier=2.25
RefreshRate=165

🛡️ Safety & Reliability Architecture
AutoDsrToggle includes safeguards to prevent misbehavior, corruption, or race conditions:

🔒 Single-Instance Lock: A system-wide Mutex prevents overlapping executions and race conditions if the tool is triggered rapidly via macros or scripts.

👁️ WMI Hardware Safeguards: Unique hardware instance IDs track your specific monitors even if display cables, ports, or power states are swapped.

🔄 Self-Healing Engine: If a display order shift, hardware change, invalid resolution, or structural config error is detected, the utility automatically purges the corrupted state, triggers a warning/reset prompt, and regenerates fresh defaults.

📜 License
This program is free software released under the GNU General Public License v3.0 (GPL v3).
You can find the full license text in the gpl-3.0.txt file included in this repository or online at https://www.gnu.org/licenses/.