<p align="center">
  <img src="assets/logo.svg" width="96" alt="dHud logo" />
</p>

<h1 align="center">dHud</h1>

<p align="center">A lightweight, transparent, always-on-top hardware overlay for Windows.</p>

<p align="center"><b>English</b> · <a href="README.tr.md">Türkçe</a></p>

---

## Features

- CPU / GPU temperature, usage, clock, power, fan speed (RPM), VRAM and RAM
- Network download / upload speed and ping
- Vertical or single-row horizontal layout, compact mode, mini temperature graph
- Temperature-based colors and critical temperature notifications
- Click-through, position lock, edge snapping, hotkeys
- Dark-themed system tray menu and run at Windows startup
- English and Turkish UI (English by default, switch in Settings → Appearance)
- Automatic update checks via GitHub Releases (on startup and every 1–24 hours while running; you get a notification and update with one click)
- Game FPS counter (fullscreen DirectX games): small overlay on the PC and live FPS on the mobile app. Off by default, needs administrator rights
- Android companion app (APK in Releases): live stats, temperature graph and a full-screen tablet dashboard over your local network. Pair by scanning the QR code in Settings → Behavior → Mobile app

## Requirements

- Windows 10 / 11 (no .NET install needed, the release exe bundles .NET)
- Administrator rights and the [PawnIO](https://pawnio.eu) driver for CPU temperature

## Installation

Download from the [latest release](https://github.com/d0ukan/dHud-xverlay/releases/latest):

- **`dHud-Setup-<version>.exe`**: installer. Installs per-user to `%LocalAppData%\Programs\dHud` (no admin rights needed), adds a Start menu entry and an uninstaller.
- **`dHud-<version>.exe`**: portable single file, just run it anywhere.

## Usage

| | |
| --- | --- |
| Move | Left-click and drag |
| Settings | ⚙ icon, right-click, or double-click the tray icon |
| `Ctrl+Shift+F9` | Hide / show the overlay |
| `Ctrl+Shift+F10` | Toggle click-through |

> The overlay is not visible in exclusive fullscreen games; use borderless windowed mode.

Sensors are read with [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).

## License

[MIT](LICENSE) © 2026 d0ukan. Sensor reading uses LibreHardwareMonitorLib (MPL-2.0).
