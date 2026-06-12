<div align="center">

<img src="docs/images/icon.png" alt="fluidMonitor" width="96" height="96">

# fluidMonitor

**A beautiful, lightweight system monitor widget for Windows.**

Real-time CPU, GPU, RAM, network, and disk stats — always on your desktop, never in your way.

[![Release](https://img.shields.io/badge/release-v1.0.0-blue)](../../releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078d4)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](#requirements)

<br>

<img src="docs/images/hero.png" alt="fluidMonitor widget" width="260">

</div>

---

## Why fluidMonitor?

Most system monitors are either heavyweight dashboards or cryptic taskbar numbers. fluidMonitor sits in between — a clean, themeable widget that shows exactly what you care about at a glance, with virtually zero overhead.

- **Lightweight** — a Windows service polls the hardware; the widget just renders. No Electron, no browser engine.
- **Beautiful by default, yours in two clicks** — 16 skins, 100+ color themes, full font control, or roll the dice and let it surprise you.
- **Built for gamers** — Game Mode snaps the widget to a corner with one hotkey, even in fullscreen.
- **Remote monitoring** — watch your other PCs' stats from one desktop over your LAN.

---

## Features

### Live hardware tiles

CPU, GPU, RAM, Network, Disk, and Clock tiles — each individually toggleable and drag-to-reorder. CPU and GPU tiles show temperature, load, and clock speed. RAM shows usage and capacity. Network shows live up/down traffic with animated indicators. Disk shows real-time read/write speeds.

Vertical or horizontal layout — switch any time.

<div align="center">
<img src="docs/images/widget-horizontal.png" alt="Horizontal layout">
</div>

### Themes, skins, and colors

The appearance engine has three independent layers:

| Layer | What it controls | Count |
|-------|-----------------|-------|
| **Skins** | Shape, borders, tile style, corner radius | 16 built-in |
| **Colors** | 5-color palette (background, tile, accent, text, muted) | 100+ presets |
| **Preset Themes** | One-click skin + color combos | Curated library |

Hit the dice for a random look, undo if you don't like it, and save your favorites to 5 quick slots. Import and export themes as `.fluidtheme` files to share with others.

<div align="center">
<img src="docs/images/settings-appearance.png" alt="Appearance settings" width="420">
</div>

### CPU temperature

A one-time sensor driver setup ([PawnIO](https://pawnio.eu/)) unlocks CPU temperature directly on the widget. The driver is downloaded on demand from the official source — fluidMonitor never bundles or redistributes it. Switch between °C and °F with a rocker, and remove the driver any time from the same menu.

### Game Mode

Press a hotkey and the widget snaps to a corner of your screen with custom opacity, layout, and tile selection — designed to stay readable but unobtrusive over a game. Press again to send it back. Works in fullscreen.

<div align="center">
<img src="docs/images/game-mode.png" alt="Game Mode settings" width="420">
</div>

### Temperature warnings

Set a threshold and the widget flashes a warning color when your CPU or GPU runs hot. Or use gradient mode, where the unit text shifts smoothly from cool blue to hot red as temperature climbs.

<div align="center">
<img src="docs/images/warnings.png" alt="Warnings settings" width="420">
</div>

### Utilities

Quick launchers for popular Windows optimization tools and a window-snap blocklist with a live window picker.

<div align="center">
<img src="docs/images/utilities.png" alt="Utilities window" width="420">
</div>

### Remote monitoring

Run fluidMonitor on multiple machines and watch them all from one desktop. TCP-based with mutual handshake-key authentication. Each remote device gets its own popout widget with independent layout and theming.

<div align="center">
<img src="docs/images/remote-monitoring.png" alt="Remote monitoring" width="420">
</div>

### Quality of life

- **Snap to edges and windows** — the widget docks flush to screen edges and other windows' borders, with a configurable blocklist
- **Click-through mode** — make the widget invisible to the mouse; toggle back with a hotkey
- **Slider default markers** — every settings slider shows a tick at its factory default that glows as you approach it
- **Built-in help** — the **?** button opens a categorized guide to every feature
- **Dark and light mode** — full palette swap with one click
- **Run at startup** — per-user, no admin needed
- **Crash-hardened** — automatic render recovery and crash logging

---

## Security

fluidMonitor is built with security-conscious defaults:

- **0/69 on VirusTotal** — the installer is clean across all major antivirus engines. [Verify the latest scan yourself](https://www.virustotal.com) by uploading the installer or checking the SHA-256 hash listed in each release.
- **No telemetry** — the app makes zero network calls. The only outbound connections are the optional PawnIO driver download (user-initiated) and LAN-only remote monitoring.
- **PawnIO is never bundled** — the CPU temperature driver is downloaded on demand from its [official GitHub release](https://github.com/namazso/PawnIO.Setup/releases), and the digital signature is verified before execution.
- **Open source** — every line of code is in this repo for inspection. The service requires LocalSystem privileges solely for hardware sensor access via LibreHardwareMonitor.
- **Settings are local** — all configuration lives in `%APPDATA%\fluidMonitor\settings.json`. Nothing is sent anywhere.

## Installation

1. Download the latest installer from [**Releases**](../../releases)
2. Run the installer
3. The widget appears on your desktop — click the gear icon to open Settings

The installer sets up both the widget and the background sensor service. CPU temperature requires an optional one-time driver install, offered on first run.

### Requirements

- Windows 10 / 11 (x64)
- .NET 8 Desktop Runtime (bundled in the installer)

---

## Architecture

```
┌─────────────────────┐     named pipes      ┌──────────────────┐
│  Fluid.Service      │ ◄──────────────────► │  Fluid.App       │
│  LibreHardwareMonitor│   sensors + commands │  (WPF widget)    │
│  polls hardware      │                      │  renders tiles   │
└─────────────────────┘                      └──────────────────┘
         │                                            │
         └──── TCP (optional) ────────────────────────┘
                  remote monitoring between machines
```

- **Fluid.Service** — Windows service running as LocalSystem. Polls hardware via [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) at a configurable interval (250ms–5s) and pushes snapshots over a named pipe.
- **Fluid.App** — the WPF widget. Stateless renderer with all user settings persisted to `%APPDATA%\fluidMonitor\settings.json`.
- **Fluid.Shared** — pipe and TCP protocol definitions shared by both projects.

---

## Building from source

```powershell
git clone https://github.com/DruidFluids/fluidMonitor.git
cd fluidMonitor
dotnet build -c Release
```

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) from `installer/fluid.iss`.

---

## License

All rights reserved. © DruidFluids

---

<div align="center">
<sub>Built with WPF, .NET 8, and an unreasonable number of color palettes.</sub>
</div>
