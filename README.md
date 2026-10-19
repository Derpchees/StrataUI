<div align="center">

<img src="docs/logo.png" width="104" alt="Strata UI logo" />

# Strata UI

### A new layer on top of Windows 11

Your own taskbar, system tray, Start menu, flyouts and notification center,<br />
plus a Hub to customize and optimize Windows. All in one app.

<br />

[![Latest release](https://img.shields.io/github/v/release/Derpchees/StrataUI?label=download&style=for-the-badge&color=8b5cf6)](https://github.com/Derpchees/StrataUI/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Derpchees/StrataUI/total?style=for-the-badge&color=ec4899)](https://github.com/Derpchees/StrataUI/releases)

![Windows 11](https://img.shields.io/badge/Windows-11-0078d4?style=flat-square&logo=windows11&logoColor=white)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet&logoColor=white)
![Languages](https://img.shields.io/badge/languages-EN%20%C2%B7%20ES%20%C2%B7%20PT%20%C2%B7%20FR%20%C2%B7%20DE-334155?style=flat-square)

**[Download](#download)** &nbsp;·&nbsp; **[Features](#features)** &nbsp;·&nbsp; **[FAQ](#faq)** &nbsp;·&nbsp; **[Build from source](#building-from-source)** &nbsp;·&nbsp; **[Changelog](CHANGELOG.md)**

</div>

<br />

Strata UI runs **on top of File Explorer**. Your files, desktop and apps keep working exactly as
before, while Strata UI hides the native taskbar, Start menu and flyouts and draws its own. Close it
and everything goes back to normal.

> [!NOTE]
> Formerly known as **WINX** (up to v1.3). Updating from WINX keeps your settings, pins and
> startup options.

## Download

1. Grab `StrataUI-Setup-X.Y.Z.exe` from the [latest release](https://github.com/Derpchees/StrataUI/releases/latest).
2. Run it and follow the wizard. Install it just for you (no admin rights needed) or for every user.
3. Done. Everything it needs is bundled, so there is nothing else to install, and the Hub keeps it
   up to date for you.

<br />

## Features

### At a glance

| | |
|---|---|
| **Taskbar** | Up to four bars per monitor, any size and style, live previews, groups and pins |
| **Edit mode** | Lay out every monitor on screen, with a live preview, before applying anything |
| **Start menu** | Seven styles, folders, sections, free placement and every game launcher |
| **Notifications** | Its own center and banners, with pictures, color emojis and clickable links |
| **Flyouts** | Sound, network, Bluetooth, brightness for every monitor, battery |
| **Performance** | CPU, GPU and RAM usage, temperatures, an FPS counter and RAM cleanup |
| **Widgets** | Clock, music visualizer and big performance readouts on the desktop |
| **Themes** | Four styles, a dozen color schemes, wallpaper accent and six animation styles |
| **Optimization** | Cleanup, performance, privacy and preinstalled apps, always undoable |
| **Modules** | Themes, animations, widgets and Start menu looks you can import and share |

### Taskbar

- Up to **four bars per monitor**, one on each edge (top and bottom bars keep the corners), at any size.
- Each bar has its own style: the theme's, **fully transparent** (text adapts to the wallpaper behind
  it) or a color of your own.
- Pinned apps, live window previews, **group by app** with stacks that show in any theme, click an
  open app to minimize it, and end task from its menu.
- Web apps such as YouTube or WhatsApp Web get their own icon and group.
- **Drag to reorder** windows, pins, bar items and tray icons: what you drag follows the pointer and
  everything else slides out of the way.

### Bar items

- Choose what each bar shows, in which order, and where: the **start, center or end** of the bar.
  Start and open apps are items too, so alignment is simply where you put them.
- Items: tray, network, Bluetooth, brightness, volume, battery, clock, notifications, media,
  performance meters and internet speed (with a built-in speed test).
- Each monitor gets its own Start menu, tray and status items.
- Rearrange them in edit mode or right on the bar; tray icons move between the bar and the hidden
  ones the same way.

### Edit mode

- An overlay on every monitor shows your wallpaper with the bars, widgets and a sample notification
  exactly as they will look.
- Drag items between bars, edges and monitors, add new ones from the library, and move widgets and
  the notification banner anywhere.
- Right-click a bar for its size and style (with a color picker), or an item for its options: clock
  seconds and date, battery percent, temperatures, FPS monitor and more.
- Nothing changes until you apply it, and Esc cancels. Open it from the taskbar's right-click menu
  (*Edit layout...*) or with `StrataUI.exe --edit`.

### Start menu

- **Seven styles**: Windows 11, Profile, List, Full screen, Compact, Search only and Icons. All resizable.
- Pinned apps with **folders** (drop one app on another), sections, free placement and icons of any size.
- Pin files, shortcuts and folders, also from the Windows 11 right-click menu, and set a custom icon
  for any app.
- Finds Store apps, installed programs and **Steam, Epic and GOG games**. Drag any app onto the
  taskbar to pin it.
- Uninstall from the right-click menu: Store apps go away directly, and parts of Windows that can't
  be removed don't offer it.
- Update and shut down, or update and restart, when updates are pending. Opens on the monitor your
  mouse is on.

### Notifications

- Its own **notification center** behind a bell on the taskbar (Win+N), with Do not disturb and a
  calendar on the clock.
- Banners on the monitor and corner you choose, with icons and pictures for every notification
  (web apps included), **color emojis**, clickable links, stickers and photos.
- Clicking a website's notification opens its installed web app or the tab that sent it, the way
  Windows does.
- Its own notification sound (or one you import), with Windows' per-app sounds muted so you only
  hear one.

### Flyouts, media and performance

- **Sound**: output, input, per-app volume and device options. **Network and Wi-Fi** with a
  show-password button, **Bluetooth**, **battery**.
- **Brightness for every monitor** over DDC/CI, with Windows Night light.
- **Media**: album art, title and artist on the bar for any app that reports media (Spotify,
  browsers, Media Player...), with optional controls.
- **Performance**: CPU, GPU and RAM usage, temperatures and an FPS counter that understands frame
  generation (DLSS FG, FSR FG, Lossless Scaling...), plus one-click RAM cleanup.

### Widgets

A digital clock (with or without the date), a music visualizer and big CPU, GPU, FPS and internet
speed readouts on your desktop. Add, move and resize them in edit mode on any monitor; they stay
visible with Show desktop.

### Themes and colors

- **Four styles**: Mica, Translucent, Material and Fully transparent.
- **Color schemes** that stay when you switch style: Follow Windows, Light, Dark, Midnight OLED,
  Nord, Dracula, Catppuccin, Tokyo Night, Rosé Pine and Gruvbox.
- Accent from Windows, your own, or taken from the wallpaper (Wallpaper Engine included); custom
  panel colors with opacity.
- **Six animation styles** and three speeds for flyouts, the Start menu, notifications, menus, the
  tray, previews and taskbar buttons. Windows' own window animations follow your choice.

### Optimization

An optional module, installed from the Hub with one click. Every change is backed up, so everything
can be undone.

- **Cleanup**: temp files, crash dumps, update caches and more. Never the Recycle Bin.
- **Performance and memory**: fewer svchost processes, SysMain, Widgets and other background services.
- **Preinstalled apps**: remove what you don't use, plus dedicated cards for **Microsoft Edge** and
  the **Xbox Game Bar**. Where Windows doesn't allow uninstalling Edge (outside the EEA), it is
  turned off instead (no shortcuts, no background processes) and can be restored with one click.
  Removing the Game Bar also stops Windows asking for an app to open its links when a game starts.
- **Privacy, AI features, Windows Update, Explorer** and more.
- Installed for all users, it applies changes without UAC prompts.

### Modules and languages

- Import **themes, animations, desktop widgets and Start menu looks** (small declarative JSON files
  that can't run code) and notification sounds from one place in the Hub. See the
  [module format](docs/modules/README.md).
- Export and import all your Strata UI settings as a `.zip`.
- **English, Spanish, Portuguese, French and German**, following Windows or chosen in the Hub. Drop
  an `xx.json` into `%LOCALAPPDATA%\StrataUI\Languages` to add your own.

<br />

## FAQ

<details>
<summary><b>How do I get the Windows taskbar back?</b></summary>
<br />

Right-click an empty spot on the bar and choose **Exit Strata UI**, or use Strata UI's own tray icon.
If Strata UI ever closes unexpectedly, run `StrataUI.exe --restore-taskbar`.
</details>

<details>
<summary><b>Does it change Windows?</b></summary>
<br />

No. Strata UI draws on top of File Explorer and puts everything back when it closes. The only
changes to Windows are the ones you choose in Optimization, and each of them can be undone from the
Hub.
</details>

<details>
<summary><b>Where are my settings?</b></summary>
<br />

In `%LOCALAPPDATA%\StrataUI\`: `settings.json` (preferences), `tweaks-backup.json` (the original
values before each optimization) and `logs\strataui.log`. Hub › General exports them all as a `.zip`.
</details>

<details>
<summary><b>Why does it ask for administrator rights sometimes?</b></summary>
<br />

Strata UI runs without privileges. A few tasks (some optimizations, advanced sensors, device
properties) run in a short-lived elevated helper that does only that task. Installed for all users,
those tasks no longer ask each time.
</details>

<br />

## Building from source

Requirements: Windows 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download)
(`winget install Microsoft.DotNet.SDK.10`). The installer needs [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
.\scripts\run.ps1                          # closes Strata UI if running, builds and relaunches it
dotnet test tests\Strata.Tests -c Release   # tests (xUnit)
.\scripts\build-installer.ps1              # artifacts\StrataUI-Setup-X.Y.Z.exe
```

### Command line

| Argument | What it does |
|---|---|
| *(none)* | Starts Strata UI. If it's already running, opens the Hub. |
| `--autostart` | Used when Windows starts: doesn't open the Hub (unless *Open the Hub when Windows starts* is on). |
| `--hub <page>` | Opens the Hub on a page: `Dashboard`, `Taskbar`, `Tray`, `Flyouts`, `Start`, `Notifications`, `Appearance`, `Import`, `General`, `Updates`, `Privacy`, `Backup`... |
| `--edit` | Opens edit mode (in the running instance, or right after starting). |
| `--exit` | Cleanly closes the running instance and waits for it. |
| `--restore-taskbar` | Emergency: shows the Windows taskbar again if Strata UI didn't close properly. |

Other arguments (`--apply-tweak`, `--sensors-task`, `--pick`...) are internal: Strata UI launches
itself with them for elevated tasks and helper processes.

### Project layout

```
src/Strata.Shell/            The app (StrataUI.exe)
  App.xaml(.cs)              Startup, single instance, shell lifetime
  Taskbar.*.cs               The bars: items, pins, drag and drop, transparency, groups
  EditMode/                  On-screen layout editor
  StartMenu/                 Start menu styles, pins with folders and sections
  Flyouts/                   Sound, network, Bluetooth, brightness, battery, notification center
  Notifications/             Banners and on-screen indicators (OSD)
  Widgets/                   Desktop widgets
  Hub/                       Settings window and its pages
  Controls/                  Task button, tray icon, window previews
  Tweaks/                    Optimization engine, catalog, backup, apps, elevation
  Services/                  Settings, audio, network, sensors, notifications, themes, modules...
  Localization/              UI translations (en, pt, fr, de; Spanish lives in the code)
src/Strata.Optimization/     Optional module with the Optimization pages
src/Strata.ContextMenu/      "Pin to Start" in the Windows 11 right-click menu
tests/Strata.Tests/          xUnit tests
installer/                   Inno Setup script and the context menu package
scripts/                     run.ps1, build-installer.ps1, build-context-menu.ps1
docs/modules/                Module format and examples
```

Versions follow [semantic versioning](https://semver.org/). Every release is listed in the
[changelog](CHANGELOG.md) (in Spanish) and tagged `vX.Y.Z`.

## Credits

- [ManagedShell](https://github.com/cairoshell/ManagedShell): taskbar, tray and AppBar plumbing.
- [WPF-UI](https://github.com/lepoco/wpfui): Fluent controls for the Hub and flyouts.
- [NAudio](https://github.com/naudio/NAudio): Core Audio access.
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor): temperatures.
- [Vortice](https://github.com/amerkoleci/Vortice.Windows): DirectWrite for color emojis.
- Optimization options inspired by [Win11Debloat](https://github.com/Raphire/Win11Debloat) (MIT).
