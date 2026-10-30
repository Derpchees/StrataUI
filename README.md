<div align="center">

<img src="docs/logo.png" width="120" alt="Strata UI logo" />

# Strata UI

**A new layer on top of Windows 11**

Your own taskbar, system tray, Start menu, flyouts and notification center,<br />
plus a Hub to customize and optimize Windows. All in one app.

<br />

[![Latest release](https://img.shields.io/github/v/release/Derpchees/StrataUI?label=download&style=for-the-badge&logo=github&color=7c6cf0)](https://github.com/Derpchees/StrataUI/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Derpchees/StrataUI/total?style=for-the-badge&logo=windows11&logoColor=white&color=3f6fdc)](https://github.com/Derpchees/StrataUI/releases)

[![Windows 11](https://img.shields.io/badge/Windows-11-0078d4?style=flat-square&logo=windows11&logoColor=white)](#requirements)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20Fluent-5c2d91?style=flat-square&logo=dotnet&logoColor=white)](https://github.com/lepoco/wpfui)
[![Languages](https://img.shields.io/badge/languages-EN%20%C2%B7%20ES%20%C2%B7%20PT%20%C2%B7%20FR%20%C2%B7%20DE-334155?style=flat-square&logo=googletranslate&logoColor=white)](#modules-and-languages)
[![Last commit](https://img.shields.io/github/last-commit/Derpchees/StrataUI?style=flat-square&logo=git&logoColor=white&color=334155)](https://github.com/Derpchees/StrataUI/commits/main)

<br />

[**Download**](#download) &nbsp;&middot;&nbsp;
[**Features**](#features) &nbsp;&middot;&nbsp;
[**FAQ**](#faq) &nbsp;&middot;&nbsp;
[**Build from source**](#building-from-source) &nbsp;&middot;&nbsp;
[**Changelog**](CHANGELOG.md)

</div>

<br />

Strata UI runs **on top of File Explorer**. Your files, desktop and apps keep working exactly as
before, while Strata UI hides the native taskbar, Start menu and flyouts and draws its own. Close it
and everything goes back to normal.

> [!NOTE]
> Formerly known as **WINX** (up to v1.3). Updating from WINX keeps your settings, pins and
> startup options.

<br />

## Highlights

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/icons/taskbar.svg" width="40" align="left" alt="" />
      <b><a href="#taskbar">Taskbar</a></b><br />
      Up to four bars per monitor, any size and style, live previews, groups and pins.
    </td>
    <td width="50%" valign="top">
      <img src="docs/icons/edit.svg" width="40" align="left" alt="" />
      <b><a href="#edit-mode">Edit mode</a></b><br />
      Lay out every monitor on screen, with a live preview, before applying anything.
    </td>
  </tr>
  <tr>
    <td valign="top">
      <img src="docs/icons/start.svg" width="40" align="left" alt="" />
      <b><a href="#start-menu">Start menu</a></b><br />
      Seven styles, folders, sections, free placement and every game launcher.
    </td>
    <td valign="top">
      <img src="docs/icons/notifications.svg" width="40" align="left" alt="" />
      <b><a href="#notifications">Notifications</a></b><br />
      Its own center, grouped by app like on your phone, with pictures, color emojis and links.
    </td>
  </tr>
  <tr>
    <td valign="top">
      <img src="docs/icons/flyouts.svg" width="40" align="left" alt="" />
      <b><a href="#flyouts-and-media">Flyouts</a></b><br />
      Sound, network, Bluetooth, brightness for every monitor, battery and media.
    </td>
    <td valign="top">
      <img src="docs/icons/performance.svg" width="40" align="left" alt="" />
      <b><a href="#performance">Performance</a></b><br />
      CPU, GPU and RAM usage, temperatures, an FPS counter and RAM cleanup.
    </td>
  </tr>
  <tr>
    <td valign="top">
      <img src="docs/icons/widgets.svg" width="40" align="left" alt="" />
      <b><a href="#widgets">Widgets</a></b><br />
      Clock, calendar, media player, drives, battery and performance readouts on the desktop.
    </td>
    <td valign="top">
      <img src="docs/icons/themes.svg" width="40" align="left" alt="" />
      <b><a href="#themes-and-colors">Themes</a></b><br />
      Four styles, a dozen color schemes, wallpaper accent and six animation styles.
    </td>
  </tr>
  <tr>
    <td valign="top">
      <img src="docs/icons/optimization.svg" width="40" align="left" alt="" />
      <b><a href="#optimization">Optimization</a></b><br />
      Cleanup, performance, privacy and preinstalled apps, always undoable.
    </td>
    <td valign="top">
      <img src="docs/icons/modules.svg" width="40" align="left" alt="" />
      <b><a href="#modules-and-languages">Modules</a></b><br />
      Themes, animations, widgets and Start menu looks you can import and share.
    </td>
  </tr>
  <tr>
    <td colspan="2" valign="top">
      <img src="docs/icons/files.svg" width="40" align="left" alt="" />
      <b><a href="#files">Files</a></b><br />
      A clean file explorer in your theme: folders, drives, recents, thumbnails, preview pane, copy progress and built-in archives (zip, 7z, rar…) without extra apps.
    </td>
  </tr>
</table>

<br />

<a id="download"></a>

## <img src="docs/icons/download.svg" width="32" align="center" alt="" /> Download

1. Grab `StrataUI-Setup-X.Y.Z.exe` from the [latest release](https://github.com/Derpchees/StrataUI/releases/latest).
2. Run it and follow the wizard. Install it just for you (no admin rights needed) or for every user.
3. Done. Everything it needs is bundled, so there is nothing else to install, and the Hub keeps it
   up to date for you.

### Requirements

| | |
|---|---|
| **System** | Windows 11, 64-bit |
| **Rights** | None to install for yourself; admin only to install for every user |
| **Extras** | Nothing: the .NET runtime and every library ship with the installer |

<br />

## Features

<a id="taskbar"></a>

### <img src="docs/icons/taskbar.svg" width="28" align="center" alt="" /> Taskbar

- Up to **four bars per monitor**, one on each edge (top and bottom bars keep the corners), at any size.
- Each bar has its own style: the theme's, **fully transparent** (text adapts to the wallpaper behind
  it) or a color of your own.
- Pinned apps, live window previews, **group by app** with stacks that show in any theme, click an
  open app to minimize it, and end task from its menu.
- Web apps such as YouTube or WhatsApp Web get their own icon and group.
- **Drag to reorder** windows, pins, bar items and tray icons: what you drag follows the pointer and
  everything else slides out of the way.

**Bar items**

- Choose what each bar shows, in which order, and where: the **start, center or end** of the bar.
  Start and open apps are items too, so alignment is simply where you put them.
- Items: tray, network, Bluetooth, brightness, volume, battery, clock, notifications, media, a
  screenshot button, a power menu (lock, sleep, sign out, restart, shut down),
  performance meters and internet speed (with a built-in speed test).
- Each monitor gets its own Start menu, tray and status items.
- Rearrange them in edit mode or right on the bar; tray icons move between the bar and the hidden
  ones the same way.

<a id="edit-mode"></a>

### <img src="docs/icons/edit.svg" width="28" align="center" alt="" /> Edit mode

- An overlay on every monitor shows your wallpaper with the bars, widgets and a sample notification
  exactly as they will look.
- Drag items between bars, edges and monitors, add new ones from the library, and move widgets and
  the notification banner anywhere.
- Right-click a bar for its size and style (with a color picker), or an item for its options: clock
  seconds and date, battery percent, temperatures, FPS monitor and more.
- Nothing changes until you apply it, and Esc cancels. Open it from the taskbar's right-click menu
  (*Edit layout...*) or with `StrataUI.exe --edit`.

<a id="start-menu"></a>

### <img src="docs/icons/start.svg" width="28" align="center" alt="" /> Start menu

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

<a id="notifications"></a>

### <img src="docs/icons/notifications.svg" width="28" align="center" alt="" /> Notifications

- Its own **notification center** behind a bell on the taskbar (Win+N), with Do not disturb and a
  calendar on the clock.
- **Grouped by app** like on your phone: each app, website or web app is one card with the newest
  notification and a count; expand it to see the rest in the same card, or clear them all at once.
- Banners on the monitor and corner you choose, with icons and pictures for every notification
  (web apps included), **color emojis**, clickable links, stickers and photos.
- Clicking a notification takes you where Windows would: a website's opens its installed web app
  or the tab that sent it, and apps with their own activator (WhatsApp, Spotify, Thunderbird...)
  open the chat, song or email. Other apps come to the front if they're open, or start.
- Its own notification sound (or one you import), with Windows' per-app sounds muted so you only
  hear one.

<a id="flyouts-and-media"></a>

### <img src="docs/icons/flyouts.svg" width="28" align="center" alt="" /> Flyouts and media

- **Sound**: output, input, per-app volume and device options, and **Also play on** to hear
  everything on a second output too (both monitors' speakers, for example), and paired Bluetooth
  headphones that are not connected yet (tap to connect and switch to them). **Network and Wi-Fi** with a
  show-password button, **Bluetooth** (live connection status, battery level and pairing nearby
  devices right from the flyout), **battery**.
- **Brightness for every monitor** over DDC/CI, with Windows Night light.
- **Media**: album art, title and artist on the bar for any app that reports media (Spotify,
  browsers, Media Player...), with optional controls.

<a id="files"></a>

### <img src="docs/icons/files.svg" width="28" align="center" alt="" /> Files

- A **file explorer** that matches the rest of Strata UI: a home page with your folders, drives (with
  free space) and recent files, and every folder as a list or a grid with previews of pictures,
  videos, music, PDFs and documents.
- **Tabs** in the title bar (Ctrl+T, middle-click a folder to open it in a new tab) and a **dual
  pane** view with two folders side by side (Ctrl+Shift+D) to drag files between them.
- Select with a box by dragging over empty space, drop files on any folder in the sidebar (or on
  the Recycle Bin), and drag folders there to pin them.
- Pin your own folders to the sidebar (and give them your own names there), the Recycle Bin is one click away, file extensions can be
  shown or hidden, and scrolling is smooth.
- The **desktop** is Strata UI's too: icons in the same style (previews, drag to arrange, rename,
  drop files) and a right-click menu with view and sort options, widgets, Files and everything
  Windows puts there (New, Display settings, Personalize, your graphics card panel). Widgets always
  stay above the icons, and turning it off in the Hub brings the Windows desktop back as it was.
- Breadcrumb path you can also type into (Ctrl+L), search that also looks inside subfolders (each
  result says where it is), sorting, hidden files, back/forward with the mouse side buttons and the
  usual keyboard shortcuts (Shift+Delete deletes permanently).
- A **preview pane** (Alt+P) for the selected file: pictures with their real size, the first page
  of a PDF, the start of text and code files, covers of video and music, and a folder's item count.
- Copy and move show their **progress inside Files** (speed, time left, cancel) instead of a
  Windows window, and replacing a file never loses the old one if you cancel. Name conflicts,
  permanent deletes and emptying the Recycle Bin ask in Strata UI's own themed dialogs, on the
  monitor you're working on. Anything that needs administrator rights (copying into Program Files,
  deleting, renaming, new folders) asks in a Strata UI dialog with the shield and runs elevated with
  the same progress card, instead of Windows' prompts. Clipboard and drag and drop work with File Explorer and
  any other app. The right-click menu also includes everything Windows and your apps add (NanaZip,
  7-Zip, Open with, Send to, Properties, New...), read in a separate process so a broken shell
  extension can't take Strata UI down. The Recycle Bin
  (restore, delete, empty) lives inside Files, and anything can be pinned to Start.
- **Archives without extra apps** (7-Zip is built in): zip, 7z, rar, tar.gz and about 50 more formats
  open like folders. Browse them, open or copy files from them, and extract with progress ("Extract
  here" doesn't nest a folder inside another). Password-protected ones ask in a Strata UI dialog.
  **Compress** to 7z (the smallest, one click) or pick zip or tar, the level and an AES-256 password.
- **Format drives** from their right-click menu (NTFS, exFAT or FAT32, label, quick format). The
  Windows drive, and the ones holding Strata UI or your profile, can never be formatted.
- A built-in **viewer** for pictures (animated GIFs, SVG, camera RAW, HEIF/AVIF too), video and
  music with the modern Windows player and your Store codecs (webm, AV1, HEVC, ogg, opus...): zoom
  where the mouse is, drag to pan, real size with a double click, phone photos the right way up,
  playback controls that hide on their own, full screen, and the arrow keys to go through the
  folder. PDFs and Office documents open in your own apps.
- **Cloud and network**: OneDrive (personal and work), Google Drive, Dropbox and iCloud Drive in the
  sidebar with each file's status (online-only, on this PC, always keep) and *Free up space*;
  thumbnails never download online-only files. Shared folders (`\\server\share`, or just
  `\\server` to list them), your saved network locations, mapped drives (also disconnected ones)
  and *Map network drive*; an unreachable server says so instead of hanging.
- An optional module: install it in Hub > Optional modules. Within Strata UI it replaces File
  Explorer: Win+E, the Explorer button and the Start menu and Win+X folders open Files (switch it off
  in Hub > General). Everything else in Windows keeps using
  File Explorer. You can also open it with `StrataUI.exe --files [folder]`.

<a id="performance"></a>

### <img src="docs/icons/performance.svg" width="28" align="center" alt="" /> Performance

- CPU, GPU and RAM usage on the bar or as big desktop readouts.
- Temperatures and an **FPS counter** that understands frame generation (DLSS FG, FSR FG,
  Lossless Scaling...), with the *Advanced readings* optional module (they start on their own as
  soon as it is installed).
- One-click **RAM cleanup** and internet speed with a built-in speed test.

<a id="widgets"></a>

### <img src="docs/icons/widgets.svg" width="28" align="center" alt="" /> Widgets

A digital clock (with or without the date), a month calendar, a media player with cover art and
controls, a music visualizer, free space on your drives and big CPU, GPU, RAM, FPS, battery and
internet speed readouts on your desktop. Add, move and resize them in edit mode on any monitor; they
stay visible with Show desktop.

<a id="themes-and-colors"></a>

### <img src="docs/icons/themes.svg" width="28" align="center" alt="" /> Themes and colors

- **Four styles**: Mica, Translucent, Material and Fully transparent.
- **Color schemes** that stay when you switch style: Follow Windows, Light, Dark, Midnight OLED,
  Nord, Dracula, Catppuccin, Tokyo Night, Rosé Pine and Gruvbox.
- Accent from Windows, your own, or taken from the wallpaper (Wallpaper Engine included); custom
  panel colors with opacity.
- **Six animation styles** and three speeds for flyouts, the Start menu, notifications, menus, the
  tray, previews and taskbar buttons. Windows' own window animations follow your choice.

<a id="optimization"></a>

### <img src="docs/icons/optimization.svg" width="28" align="center" alt="" /> Optimization

An optional module, installed from Hub > Optional modules with one click. Once installed it gets its own Hub
entry, **Optimization**, with a page for each section (and how many of its options are on). Every change is backed up, so everything
can be undone. Its pages are grouped in tabs:

| Section | Tabs |
|---|---|
| **Performance** | General responsiveness, memory (fewer svchost processes, SysMain and other background services), games (Game Mode, Game DVR, fullscreen optimizations) and hardware vendor services. |
| **Cleanup and apps** | Temp files, crash dumps and update caches (never the Recycle Bin); preinstalled apps, with dedicated cards for **Microsoft Edge** and the **Xbox Game Bar**; browser extras for Edge, Chrome, Firefox and Brave. |
| **Privacy** | Telemetry, ads, feedback requests, error reporting, AI features and Windows Update (including keeping your own drivers). |
| **Customize Windows** | System behavior, appearance, File Explorer and multitasking. |
| **Backup** | Every original value, to undo any change. |

- Where Windows doesn't allow uninstalling Edge (outside the EEA), it is turned off instead (no
  shortcuts, no background processes) and can be restored with one click.
- Removing the Game Bar also stops Windows asking for an app to open its links when a game starts.
- Installed for all users, it applies changes without UAC prompts.

<a id="modules-and-languages"></a>

### <img src="docs/icons/modules.svg" width="28" align="center" alt="" /> Modules and languages

- Import **themes, animations, desktop widgets and Start menu looks** (small declarative JSON files
  that can't run code) and notification sounds from one place in the Hub. See the
  [module format](docs/modules/README.md).
- Export and import all your Strata UI settings as a `.zip`.
- **English, Spanish, Portuguese, French and German**, following Windows or chosen in the Hub. Drop
  an `xx.json` into `%LOCALAPPDATA%\StrataUI\Languages` to add your own.

<br />

<a id="faq"></a>

## <img src="docs/icons/faq.svg" width="32" align="center" alt="" /> FAQ

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

<a id="building-from-source"></a>

## <img src="docs/icons/build.svg" width="32" align="center" alt="" /> Building from source

[![.NET 10 SDK](https://img.shields.io/badge/.NET%20SDK-10-512bd4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download)
[![Inno Setup 6](https://img.shields.io/badge/Inno%20Setup-6-264b99?style=flat-square)](https://jrsoftware.org/isinfo.php)
[![xUnit](https://img.shields.io/badge/tests-xUnit-5e2a84?style=flat-square)](https://xunit.net/)

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
| `--files [folder]` | Opens a Files window, on that folder or on the home page. |
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
src/Strata.Files/            Optional module with the Files windows
src/Strata.Sensors/          Optional module with the temperature and FPS readers
src/Strata.ContextMenu/      "Pin to Start" in the Windows 11 right-click menu
tests/Strata.Tests/          xUnit tests
installer/                   Inno Setup script and the context menu package
scripts/                     run.ps1, build-installer.ps1, build-context-menu.ps1, build-logo.ps1
docs/                        Logo, README icons, module format and examples
```

Versions follow [semantic versioning](https://semver.org/). Every release is listed in the
[changelog](CHANGELOG.md) (in Spanish) and tagged `vX.Y.Z`.

<br />

<a id="credits"></a>

## <img src="docs/icons/credits.svg" width="32" align="center" alt="" /> Credits

| Project | Used for |
|---|---|
| [ManagedShell](https://github.com/cairoshell/ManagedShell) | Taskbar, tray and AppBar plumbing |
| [WPF-UI](https://github.com/lepoco/wpfui) | Fluent controls for the Hub and flyouts |
| [NAudio](https://github.com/naudio/NAudio) | Core Audio access |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | Temperatures |
| [Vortice](https://github.com/amerkoleci/Vortice.Windows) | DirectWrite for color emojis |
| [7-Zip](https://www.7-zip.org/) (LGPL, `7z.dll`) | Browsing, extracting and creating archives |
| [Win11Debloat](https://github.com/Raphire/Win11Debloat) (MIT) | Inspiration for the optimization options |

<br />

<div align="center">
  <img src="docs/logo.png" width="40" alt="" />
  <br />
  <sub><b>Strata UI</b> &middot; A new layer on top of Windows 11 &middot; <a href="CHANGELOG.md">Changelog</a> &middot; <a href="https://github.com/Derpchees/StrataUI/releases/latest">Latest release</a></sub>
</div>
