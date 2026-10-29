<p align="center">
  <img src="assets/logo.svg" width="96" alt="dHud logo" />
</p>

<h1 align="center">dHud</h1>

<p align="center">A lightweight, transparent, always-on-top hardware overlay for Windows.</p>

<p align="center"><b>English</b> · <a href="README.tr.md">Türkçe</a></p>

---

## Features

- CPU / GPU temperature, usage, clock, power, VRAM and RAM
- Network download / upload speed and ping
- Vertical or single-row horizontal layout, compact mode, mini temperature graph
- Temperature-based colors and critical temperature notifications
- Click-through, position lock, edge snapping, hotkeys
- Dark-themed system tray menu and run at Windows startup
- English and Turkish UI (English by default, switch in Settings → Appearance)
- Automatic updates via GitHub Releases

## Requirements

- Windows 10 / 11 (no .NET install needed, the release exe bundles .NET)
- Administrator rights and the [PawnIO](https://pawnio.eu) driver for CPU temperature

## Installation

Download from the [latest release](https://github.com/d0ukan/dHud-xverlay/releases/latest):

- **`dHud-Setup-<version>.exe`**: installer. Installs per-user to `%LocalAppData%\Programs\dHud` (no admin rights needed), adds a Start menu entry and an uninstaller.
- **`dHud-<version>.exe`**: portable single file, just run it anywhere.

## Building

```bash
dotnet run                 # development
dotnet publish -c Release  # bin\Release\publish\CpuGpuOverlay.exe (single file, .NET included)
```

## Releasing and updates

The version number is the `Version` value in `CpuGpuOverlay.csproj` (first release: `1.0.0`). To publish:

```bash
git tag v1.0.0
git push origin v1.0.0
```

Pushing the tag triggers GitHub Actions (`.github/workflows/release.yml`), which builds the single-file
`dHud-<version>.exe` and the `dHud-Setup-<version>.exe` installer (Inno Setup, `installer/dHud.iss`) and publishes them as a Release. On startup (can be disabled in settings) and via the tray menu
item **Check for updates**, the app looks at the latest Release; if a newer version exists it asks for
confirmation, downloads it, replaces itself and restarts.

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
