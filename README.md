<div align="center">

<img src="docs/logo.png" width="96" alt="Strata UI logo" />

# Strata UI

**A new layer on top of Windows 11.**
Your own taskbar, system tray, Start menu, flyouts and notification center, plus a Hub to
customize and optimize Windows, all in one app.

[![Latest release](https://img.shields.io/github/v/release/Derpchees/StrataUI?label=download&color=8b5cf6)](https://github.com/Derpchees/StrataUI/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Derpchees/StrataUI/total?color=ec4899)](https://github.com/Derpchees/StrataUI/releases)
![Windows 11](https://img.shields.io/badge/Windows-11-0078d4?logo=windows11&logoColor=white)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet&logoColor=white)

</div>

---

Strata UI runs **on top of File Explorer**: your files, desktop and apps keep working exactly as
before, while Strata UI hides the native taskbar, Start menu and flyouts and draws its own. Close it
and everything goes back to normal.

> Formerly known as **WINX** (up to v1.3). Updating from WINX keeps your settings, pins and
> startup options.

## Download

1. Grab `StrataUI-Setup-X.Y.Z.exe` from the [latest release](https://github.com/Derpchees/StrataUI/releases/latest).
2. Run it and follow the wizard. You can pick the folder and install it just for you (no admin
   rights) or for every user.
3. That's it. Everything it needs is bundled, so there's no need to install .NET. The Hub
   checks for updates and installs them for you.

## Features

| | |
|---|---|
| 🧭 **Taskbar** | One or two bars per monitor, on any edge, with any size and alignment. Live window previews, drag to reorder, group by app, web apps (YouTube, WhatsApp Web…) with their own icon, and a fully transparent mode whose text adapts to the wallpaper. |
| 🧩 **Bar items** | Choose what each bar shows and in which order (Start, tray, network, Bluetooth, brightness, volume, battery, clock, media, performance meters) by dragging on the bar itself or in the Hub, even from one bar to another. |
| 🚀 **Start menu** | 7 styles (Windows 11, Profile, List, Full screen, Compact, Search only, Icons), resizable. Pinned apps with folders, sections and free placement; pin files, shortcuts and folders; custom icons for any app. Opens on the monitor your mouse is on. |
| 🔔 **Notifications** | Its own notification center next to the clock (Win+N), banners on the monitor and corner you choose, Do not disturb, calendar, and icons and pictures for every notification (web apps included). |
| 🔊 **Flyouts** | Sound (output, input, per-app volume, device options), network and Wi-Fi, Bluetooth, brightness for every monitor (DDC/CI) and battery. |
| 🎵 **Media** | Album art, title and artist on the bar for any app that reports media (Spotify, browsers, Media Player…), with optional controls. |
| 📊 **Performance** | CPU, GPU and RAM usage, temperatures and an FPS counter (frame generation aware: DLSS FG, FSR FG, Lossless Scaling…), plus one-click RAM cleanup. |
| 🎨 **Themes** | System, Light, Dark, Transparency, Liquid Glass and Material, with the Windows accent, your own or one taken from the wallpaper (Wallpaper Engine too). Six animation styles and three speeds. |
| ⚙️ **Optimization** | Performance, preinstalled apps, privacy, AI features, Windows Update, Explorer and more, always with a backup so you can undo everything. |
| 🌐 **Languages** | English and Spanish, following Windows or chosen in the Hub. |

Right-click an empty spot on the bar for **Strata UI settings** (the Hub) or **Exit Strata UI**
(brings back the Windows taskbar).

## Building from source

Requirements: Windows 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download)
(`winget install Microsoft.DotNet.SDK.10`).

```powershell
.\scripts\run.ps1                         # closes Strata UI if it's running, builds and relaunches it
dotnet test tests\Strata.Tests -c Release  # tests (xUnit)
.\scripts\build-installer.ps1             # artifacts\StrataUI-Setup-X.Y.Z.exe (needs Inno Setup 6)
```

### Command line

| Argument | What it does |
|---|---|
| *(none)* | Starts Strata UI; if it's already running, opens the Hub. |
| `--hub <page>` | Opens the Hub on a page: `Home`, `Taskbar`, `Start`, `Tray`, `Notifications`, `Appearance`, `Updates`, `Privacy`, `Backup`… |
| `--exit` | Cleanly closes the running instance (and waits for it). |
| `--restore-taskbar` | Emergency: shows the Windows taskbar again if Strata UI didn't close properly. |

Other arguments (`--apply-tweak`, `--sensors-task`, `--pick`…) are internal: Strata UI launches
itself with them for elevated tasks and helper processes.

### Where data lives

Everything is in `%LOCALAPPDATA%\StrataUI\`: `settings.json` (preferences), `tweaks-backup.json`
(original values before each optimization) and `logs\strataui.log`.

### Project layout

```
src/Strata.Shell/
  App.xaml(.cs)        Startup, single instance, shell lifetime
  Taskbar.xaml(.cs)    The taskbar (one or two per monitor)
  Controls/            Task button, tray icon, window previews
  Flyouts/             Sound, network, Bluetooth, brightness, battery, notification center
  Notifications/       Notification banners and on-screen indicators (OSD)
  StartMenu/           Start menu styles, pins with folders and sections
  Hub/                 Settings window and its pages
  Tweaks/              Optimization engine, catalog, backup, Appx apps, elevation
  Services/            Settings, audio, network, Bluetooth, brightness, notifications, sensors,
                       updates, themes, localization
  Localization/        UI translations (en.json; Spanish lives in the code)
tests/Strata.Tests/    xUnit tests
installer/             Inno Setup script
scripts/               run.ps1, build-installer.ps1
```

Versions follow [semantic versioning](https://semver.org/); every release is listed in the
[changelog](CHANGELOG.md) (in Spanish) and tagged `vX.Y.Z`.

## Credits

- [ManagedShell](https://github.com/cairoshell/ManagedShell): taskbar, tray and AppBar plumbing.
- [WPF-UI](https://github.com/lepoco/wpfui): Fluent controls for the Hub and flyouts.
- [NAudio](https://github.com/naudio/NAudio): Core Audio access.
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor): temperatures.
- Optimization options inspired by [Win11Debloat](https://github.com/Raphire/Win11Debloat) (MIT).
