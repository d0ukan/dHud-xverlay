<p align="center">
  <img src="assets/logo.svg" width="96" alt="dHud logo" />
</p>

<h1 align="center">dHud</h1>

<p align="center">A light, transparent hardware overlay for Windows,<br />with desktop widgets, fan control, a game FPS counter and an Android app.</p>

<p align="center"><b>English</b> · <a href="README.tr.md">Türkçe</a></p>

<p align="center">
  <img src="docs/images/overview.png" alt="dHud: the top bar and some desktop widgets" />
</p>

## What is dHud?

- **A thin bar** at the top of your screen with CPU / GPU temperature, usage, clocks, fans, RAM, network and ping.
- **Desktop widgets** (all optional): clock, sticky notes, to-do list, timer, weather, calendar, system summary and more.
- **Extras when you want them:** fan control, a game FPS counter and an Android app for your phone or tablet.

It is easy on your PC (low CPU and memory use), has no telemetry and is free ([MIT](LICENSE)).

## Quick start

1. Download the installer from the [latest release](https://github.com/d0ukan/dHud-xverlay/releases/latest) and run it.
2. Start dHud. The bar appears: drag it where you like.
3. Click the ⚙ on the bar (or double-click the tray icon) to open **Settings**. The search box at the top finds any setting.

> CPU temperature needs **administrator rights** and the free [PawnIO](https://pawnio.eu) driver. Settings tells you if either is missing.

## What can it do?

| | |
| --- | --- |
| **The bar** | Live values in a slim bar. Vertical or horizontal, compact mode, click-through, hotkeys. You can also turn it off. |
| **Widgets** | Small dark cards you drag onto a grid. 12 kinds, each with its own options. |
| **Profiles** | Save all your settings under a name, switch in one click, move everything to a new PC. |
| **Fan control** | Draw your own fan curves, use presets, test fans. Safe by design. |
| **Game FPS** | FPS counter and an end-of-game summary. |
| **Android app** | See everything on your phone or tablet, get alerts, even change fan presets. |

The details are below. Open what you need.

<details>
<summary><b>The bar and shortcuts</b></summary>

<br />

Each row can show temperature (green / yellow / red by your thresholds), usage, clock, power, fan speed and a tiny graph. Rows: **CPU**, **GPU**, **RAM**, **NET** (download, upload, ping), plus optional **DISK** and **CORE** (busiest CPU core). Right-click the bar → **Metrics** to switch items on or off.

| Action | How |
| --- | --- |
| Move the bar | Drag it (it stays on screen and snaps to edges) |
| Open Settings | ⚙ on the bar, right-click → Settings, or double-click the tray icon |
| Hide / show the bar | `Ctrl` + `Shift` + `F9`, or right-click → **Turn off the bar** |
| Click-through on / off | `Ctrl` + `Shift` + `F10` |
| Quick menu | Right-click the bar |

The bar is optional: if you prefer the **System** widget (same values), turn the bar off in Settings → Appearance and bring it back any time from the tray icon.

<p align="center"><img src="docs/images/settings-appearance.png" width="720" alt="Settings: Appearance" /></p>

> The bar and widgets cannot appear over *exclusive* fullscreen games. Use borderless / windowed-fullscreen mode.

</details>

<details>
<summary><b>Widgets</b></summary>

<br />

*Settings → Widgets.* Every widget is a small card with an on/off switch; **Options** opens its visibility, always-on-top, lock and "move to corner" buttons. Widgets snap to a fine grid like macOS desktop widgets: while you drag, the spot it will take is highlighted. Drag any **edge or corner** to resize a widget: it snaps to whole grid cells, so cards line up like puzzle pieces, and the content grows and shrinks with the card (lists get wider text and show more rows, clocks and gauges scale proportionally). Double-click an edge to go back to the automatic size. They never overlap each other or the bar and can't be dragged off-screen. All are off by default and use almost no CPU.

<p align="center"><img src="docs/images/widgets.png" alt="More widgets" /></p>

| Widget | What it does |
| --- | --- |
| **Clock** | Time and date, optional seconds and a second time zone. |
| **Sticky notes** | Up to 30 notes stacked like **folder tabs**: click a tab to bring it forward, drag a tab to move the stack. Saved as you type. |
| **To-do list** | Add a task with Enter, add sub-tasks with ＋, tick it off, clear the finished ones. Right-click a task to duplicate it with its sub-tasks or copy it as text. |
| **Timer and reminders** | Quick timers, reminders at a clock time and a pomodoro. Notification and sound (optionally on your phone). |
| **Stopwatch** | Start, pause, laps. |
| **Day counter** | "12 days left" for any date. |
| **Calendar** | The next 7 days from an `.ics` file or link (repeating events work). No account sign-in. |
| **Weather** | Current weather and a 3-day forecast for your city. The only widget that uses the internet; off until you pick a city. |
| **System summary** | CPU, GPU, VRAM, RAM, disk and network on bars. |
| **Quick launcher** | Your favourite apps and files as icons (opened without administrator rights). |
| **Clipboard history** | The last 10 copied texts. Kept in memory only; password managers' private copies are skipped. |
| **Sound and microphone** | Volume, mute and microphone mute. |

Notes and lists are stored only on your PC in `%AppData%\dHud\widgets.json` (saved safely, with a `.bak` copy).

<p align="center"><img src="docs/images/settings-widgets.png" width="720" alt="Settings: Widgets" /></p>

</details>

<details>
<summary><b>Profiles and moving to a new PC</b></summary>

<br />

*Settings → Profiles.*

- **Profiles:** save all your settings (look, metrics, widgets, alerts, fan curves …) under a name and switch with one click, also from the tray icon.
- **Back up / move:** *Export everything…* writes one `.dhudprofile` file. On the new PC install dHud, choose *Import…* and carry on where you left off. You can include your notes, lists and launcher items, and optionally the phone pairing key.
- **Safe:** importing never turns fan control on by itself, and the phone key is only taken if you tick it. Launcher paths and fan sensors belong to the old PC, so check them after moving.

<p align="center"><img src="docs/images/settings-profiles.png" width="720" alt="Settings: Profiles" /></p>

</details>

<details>
<summary><b>Fan control</b></summary>

<br />

*Settings → Behavior → Fan control* (needs administrator rights). dHud lists every fan it can control. For each one choose **Auto** (dHud does not touch it), **Manual** (fixed speed) or **Curve** (speed follows temperature: drag points, click to add, right-click to remove).

- **Presets:** *Quiet*, *Optimal*, *Max Cold*. Apply one to all fans at once (pumps are skipped unless you ask).
- **Your own curves and profiles:** save a curve under a name, or the setup of all fans as a profile.
- **Test:** runs a fan at 100% for a few seconds to see if it really responds.
- **Alerts:** if a controlled fan stops, you get one calm notification (also on your phone). It can be turned off.

**Safety:** fan control is off by default and asks for confirmation. If CPU or GPU gets critically hot, or a temperature sensor is lost, every controlled fan goes to 100%. Fans slow down gradually. When dHud exits or crashes, fans are handed back to the BIOS. If dHud is killed abruptly, a tiny windowless helper sets those fans to a safe 70% and dHud tells you at the next start. Not every motherboard allows software fan control.

</details>

<details>
<summary><b>Game FPS counter</b></summary>

<br />

*Settings → Behavior → Game FPS* (off by default, needs administrator rights). Measures the FPS of the fullscreen / borderless game in front without injecting anything into it, shows a small counter in the corner and on your Android app. When a session of 2+ minutes ends, a summary card shows average FPS, 1% low, lowest / highest FPS and peak temperatures. Works with DirectX 10 / 11 / 12 (DirectX 9 is experimental; Vulkan and OpenGL are not measured).

</details>

<details>
<summary><b>Android app</b></summary>

<br />

Mirrors dHud on your **local network** (same Wi-Fi), in the same dark theme.

1. Download `dHud-<version>.apk` from the [latest release](https://github.com/d0ukan/dHud-xverlay/releases/latest) onto the phone / tablet and install it.
2. On the PC: *Settings → Behavior → Mobile app* → tick **Allow the dHud mobile app to connect**. A QR code appears (allow Windows Firewall on private networks).
3. In the app tap **Scan QR code**. The dot at the top turns green.

It shows live values and a temperature graph, vibrates and notifies on alerts, shows your reminders and the game summary, and has a **full-screen dashboard** for tablets (it slowly shifts and dims to protect the screen) and a **home-screen widget**. It finds your PC again if its IP address changes and updates itself with one tap.

**Fans from the phone:** a separate *Fans* tab. Off by default: tick **Let the mobile app change fan settings** on the PC. The phone can only pick ready-made options (Auto, Quiet / Optimal / Max Cold, your saved curves, a saved profile) and switch fan control on or off. Each fan has an **Allow control from phone** checkbox on the PC, and all safety rules still apply.

Only paired devices can connect; nothing leaves your network. Ports: TCP `47821`, UDP `47822`.

</details>

## Installation

Get these from the [latest release](https://github.com/d0ukan/dHud-xverlay/releases/latest):

- **`dHud-Setup-<version>.exe`**: installer (per user, no admin rights needed, adds a Start menu entry).
- **`dHud-<version>.exe`**: portable single file.
- **`dHud-<version>.apk`**: Android app.

dHud checks GitHub for new versions and updates with one click. **Requirements:** Windows 10 / 11 (no .NET install needed), Android 7.0+.

> The app is not code-signed yet, so Windows SmartScreen may warn you the first time: **More info → Run anyway**.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| CPU temperature shows `--` | Run as administrator and install the [PawnIO](https://pawnio.eu) driver. |
| Bar missing in a game | Switch the game to borderless / windowed-fullscreen. |
| Fan list is empty | Run as administrator. Some motherboards don't allow software fan control. |
| FPS shows nothing | Turn on *Game FPS*, run as administrator, use a fullscreen / borderless DirectX game. |
| Phone can't connect | Same Wi-Fi? *Mobile app* ticked? Allow dHud in Windows Firewall (private network). Try *Reset pairing*. |
| Widgets or bar off-screen | Right-click → Reset position, or use *Move to* in Settings → Widgets. |
| Something else | Settings → Behavior → **Help → Copy diagnostic info** and paste it into your report. |

## Privacy

No telemetry. The only internet requests are the update check to GitHub and, only if you turn on those widgets, the weather lookup at open-meteo.com (the city you search for and its coordinates; no account, no key) or a calendar link you entered. The phone connection stays on your local network and needs the pairing key.

## Credits and license

[MIT](LICENSE) © 2026 d0ukan. Sensors are read with [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0); FPS with Windows event tracing ([TraceEvent](https://github.com/microsoft/perfview), MIT); audio with [NAudio](https://github.com/naudio/NAudio) (MIT).
