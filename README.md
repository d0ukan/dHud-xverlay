<p align="center">
  <img src="assets/logo.svg" width="96" alt="dHud logo" />
</p>

<h1 align="center">dHud</h1>

<p align="center">A lightweight, transparent, always-on-top hardware overlay for Windows,<br />with fan control, a game FPS counter and an Android companion app.</p>

<p align="center"><b>English</b> · <a href="README.tr.md">Türkçe</a></p>

---

## What is dHud?

dHud shows your PC's vital signs in a small overlay that stays above your windows: CPU and GPU temperature, usage, clocks, power, fan speed, memory, network and ping. It is built to be unobtrusive (low CPU and memory use) and to stay out of your way (click-through, hotkeys, edge snapping).

On top of the overlay, dHud can:

- **control your fans** with per-fan curves you draw yourself,
- **count FPS** in fullscreen games,
- **stream everything to your phone or tablet** (Android app) over your home network, with temperature alerts and a full-screen dashboard mode.

## Quick start

1. Download the installer from the [latest release](https://github.com/d0ukan/dHud-xverlay/releases/latest) and run it.
2. Start dHud. The overlay appears; drag it where you like.
3. Click the ⚙ icon on the overlay (or double-click the tray icon) to open **Settings**.

> For CPU temperature dHud needs **administrator rights** and the free [PawnIO](https://pawnio.eu) driver. The Settings window tells you if either is missing.

## What you can see

**The overlay** has one row per device. Each row can show temperature (coloured green / yellow / red by your thresholds), usage, clock speed, power draw, fan speed and a tiny temperature graph.

| Row | Shows |
| --- | --- |
| **CPU** | temperature, usage, clock, power, fan |
| **GPU** | temperature, usage, clock, power, fan, VRAM |
| **RAM** | usage and used / total memory |
| **NET** | download, upload and ping (ping can be shown on its own) |

**Layouts:** vertical (rows stacked, columns aligned) or horizontal (one single row, great for the top of the screen). **Compact mode** shrinks text and spacing. Everything is optional: right-click the overlay → **Metrics** to switch items on or off instantly.

## Controls and shortcuts

| Action | How |
| --- | --- |
| Move the overlay | Left-click and drag (it can't be dragged off-screen; it snaps to screen edges) |
| Open Settings | ⚙ on the overlay, right-click → Settings, or double-click the tray icon |
| Hide / show overlay | `Ctrl` + `Shift` + `F9` |
| Click-through on / off | `Ctrl` + `Shift` + `F10` (mouse clicks pass through to the window below) |
| Quick menu | Right-click the overlay: layout, compact, lock position, metrics, reset position |
| Tray menu | Right-click the tray icon: hide / show, click-through, lock, check for updates, exit |

> The overlay cannot appear over *exclusive* fullscreen games. Use borderless / windowed-fullscreen mode.

## Settings at a glance

| Tab | What is in it |
| --- | --- |
| **Appearance** | layout, compact mode, always on top, background opacity, size, language (English / Türkçe) |
| **Metrics** | which items to show (CPU, GPU, usage, graph, clock, power, fan, VRAM, RAM, network, ping), refresh rate |
| **Behavior** | start with Windows, click-through, lock position, hotkeys, update checks, plus **Fan control**, **Game FPS** and **Mobile app** |
| **Alerts** | warning / critical temperature thresholds (separate for CPU and GPU), temperature colours, notification when a temperature stays critical |

## Fan control

*Settings → Behavior → Fan control → Open fan control…* (needs administrator rights).

dHud lists every fan it can control (CPU fan, pump, case fans, GPU fans). For each one choose:

- **Auto**: dHud does not touch it (BIOS / driver stays in charge).
- **Manual**: a fixed speed.
- **Curve**: speed follows a temperature curve. Open the curve editor, **drag points** to shape the curve, click an empty spot to add a point, right-click a point to remove it. A green marker shows the current temperature. Pick the temperature source (CPU or GPU) and a minimum speed.

**Presets:** *Quiet*, *Optimal* and *Max Cold* give you a starting curve you can fine-tune. The buttons at the top of the fan window apply a preset to **all fans at once** (GPU fans follow the GPU, the CPU fan follows the CPU, the rest follow the higher of CPU and GPU). Pumps are skipped unless you tick *Include pump*, because a slow pump can overheat your CPU.

**Your own curves and profiles:** save any curve under a name (*My curves*) and reuse it on any fan. Save the settings of **all fans** as a *profile* ("Gaming", "Night"…) and load it with one click.

**Fan test and fan alert:** the **Test** button runs a fan at 100% for a few seconds and tells you whether its speed really increased (useful to spot a loose cable). If a fan that dHud controls stops spinning, you get a notification (also on your phone).

**Temperature source:** each curve can follow the CPU, the GPU, or the higher of the two.

**Safety:**

- Fan control is **off by default** and asks for confirmation the first time.
- If CPU or GPU reaches the **critical temperature** (Settings → Alerts), every fan dHud controls jumps to **100%**.
- If a curve's temperature sensor can't be read for a few seconds, the fan goes to 100%.
- Fans slow down gradually (never abruptly) to avoid constant speed hunting. A curve can only go up: a point can't be lower than the one on its left.
- Every risky action (enabling control, applying a preset to all fans, loading a profile, testing a fan) asks for confirmation in your language.
- When dHud exits or crashes, fans are handed back to the BIOS / driver. *If dHud is killed abruptly (for example from Task Manager), fans keep their last speed until you start dHud again or restart the PC.*

Not every motherboard allows software fan control; dHud shows "no controllable fans found" in that case.

## Game FPS counter

*Settings → Behavior → Game FPS.* Off by default, needs administrator rights.

- Measures the FPS of the **fullscreen / borderless** game in front, without injecting anything into the game (it only listens to Windows' own frame events).
- Shows a small counter in the top-left corner of the game's monitor, only while a game is running. Colour changes with the frame rate.
- The same value appears on the Android app.
- Works with DirectX 10 / 11 / 12 games. DirectX 9 is experimental; Vulkan and OpenGL games are not measured.

## Android companion app

An Android app (phone or tablet) that mirrors dHud over your **local network** (same Wi-Fi). It has the same dark theme.

**What it does:** live CPU / GPU / RAM / network values, a 5-minute temperature graph, high-temperature alerts (vibration and notification), the game FPS, and a **full-screen dashboard mode** for tablets (the screen stays on while charging).

**Setup (once):**

1. Download `dHud-<version>.apk` from the [latest release](https://github.com/d0ukan/dHud-xverlay/releases/latest) onto the phone / tablet and install it (Android asks you to allow installs from this source).
2. On the PC: *Settings → Behavior → Mobile app* → tick **Allow the dHud mobile app to connect**. A QR code appears. If Windows Firewall asks, allow access on **private networks**.
3. In the Android app tap **Scan QR code** and scan it. Done: the dot at the top turns green.

**Fans from the phone:** the app has a separate **Fans** tab. To use it, tick **Let the mobile app change fan settings** under the QR code (off by default, so the phone only watches) and turn on fan control on the PC. The phone can only pick ready-made options: for each fan *Auto*, a built-in curve (*Quiet / Optimal / Max Cold*) or one of your saved curves, or a saved **profile** that sets all fans at once. Custom speeds cannot be set from the phone. The PC's safety rules (emergency 100%, minimum speed, gradual slow-down) still apply, and the PC shows a notification for every change.

If the PC's IP address changes, the app finds the PC again by itself. The app also checks for new versions and offers a one-tap update. **Reset pairing** (below the QR code) disconnects every device.

Only devices that know the pairing key can connect; nothing is sent over the internet. dHud uses TCP port `47821` and UDP port `47822` on your local network.

## Installation

Download from the [latest release](https://github.com/d0ukan/dHud-xverlay/releases/latest):

- **`dHud-Setup-<version>.exe`**: installer. Installs per user to `%LocalAppData%\Programs\dHud` (no admin rights needed), adds a Start menu entry and an uninstaller.
- **`dHud-<version>.exe`**: portable single file, run it anywhere.
- **`dHud-<version>.apk`**: Android app.

dHud checks GitHub for new versions when it starts and every few hours while it runs (configurable in Settings → Behavior), then notifies you and updates with one click.

**Requirements:** Windows 10 / 11 (no .NET install needed), Android 7.0+ for the app.

> The app is not code-signed yet, so Windows SmartScreen may warn you the first time. Click **More info → Run anyway**.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| CPU temperature shows `--` | Run dHud as administrator and install the [PawnIO](https://pawnio.eu) driver (Settings shows the status). |
| Overlay missing in a game | Exclusive fullscreen hides overlays. Switch the game to borderless / windowed-fullscreen. |
| Fan list is empty | Run as administrator. Some motherboards don't allow software fan control. |
| FPS shows nothing | Turn on *Game FPS*, run dHud as administrator, and use a fullscreen / borderless DirectX game. |
| Phone can't connect | Same Wi-Fi as the PC? *Mobile app* ticked? Allow dHud in Windows Firewall (private network). Try *Reset pairing* and scan again. |
| Overlay off-screen after changing monitors | Overlay menu → **Reset position**. |

## Privacy

dHud has no telemetry. The only internet request is the update check to GitHub. The mobile connection stays on your local network and requires the pairing key.

## Credits and license

[MIT](LICENSE) © 2026 d0ukan. Sensors are read with [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0); FPS is measured with Windows event tracing ([TraceEvent](https://github.com/microsoft/perfview), MIT).
