<div align="center">

<img src="https://github.com/DruidFluids/fluidMonitor/raw/main/docs/images/icon.png" width="64">

### fluidMonitor v1.0.1
**A beautiful, lightweight system monitor widget for Windows.**

</div>

---

### Download

> **[fluidMonitor_installer_v1.0.1.exe](https://github.com/DruidFluids/fluidMonitor/releases/download/v1.0.1/fluidMonitor_installer_v1.0.1.exe)** -- Windows 10/11 (x64)
>
> Includes the widget, background sensor service, and .NET 8 runtime. CPU temperature driver is optional and downloaded on demand.

---

### What is fluidMonitor?

A desktop widget that displays real-time CPU, GPU, RAM, network, and disk stats -- always visible, never in the way. Lightweight by design: a Windows service handles hardware polling while the widget simply renders.

<div align="center">
<img src="https://github.com/DruidFluids/fluidMonitor/raw/main/docs/images/hero.png" width="220">
&nbsp;&nbsp;&nbsp;
<img src="https://github.com/DruidFluids/fluidMonitor/raw/main/docs/images/widget-horizontal.png" width="380">
</div>

---

### Highlights

**Appearance** -- 16 skins, 100+ color presets, full font control, and a dice button for random combos. Save favorites to 5 quick slots. Import/export `.fluidtheme` files.

**Game Mode** -- one hotkey snaps the widget to a corner with custom opacity and tile selection. Works in fullscreen.

**CPU Temperature** -- optional PawnIO sensor driver, installed with one click. Switch C/F any time.

**Temperature Warnings** -- flash alerts or smooth gradient coloring when thresholds are crossed.

**Remote Monitoring** -- TCP-based with handshake-key auth. Watch multiple machines from one desktop.

**Snap & Dock** -- auto-snaps to screen edges and window borders. Click-through mode with hotkey toggle.


---

### Security

| Check | Status |
|-------|--------|
| VirusTotal | **0 / 69** -- no vendors flagged as malicious |
| Telemetry | None -- zero outbound network calls |
| PawnIO driver | Downloaded on demand from [official source](https://github.com/namazso/PawnIO.Setup/releases), signature verified |
| Source code | Fully open for inspection |

**Installer SHA-256:**
```
f3abd4cb91b12d056c93159848ae981f9cf6ba488d0674aa2a69a9cf14de3dcc
```

[View VirusTotal scan](https://www.virustotal.com/gui/file/f3abd4cb91b12d056c93159848ae981f9cf6ba488d0674aa2a69a9cf14de3dcc)

---

### Requirements

- Windows 10 or 11 (x64)
- .NET 8 Desktop Runtime (bundled in installer)

---

### Getting started

1. Run the installer
2. The widget appears on your desktop
3. Click the gear gear icon to open Settings
4. Explore themes, adjust tiles, enable Game Mode

Full documentation: [README](https://github.com/DruidFluids/fluidMonitor#readme)
