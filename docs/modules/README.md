# Strata UI modules

Themes, animations, desktop widgets and Start menu looks can be shared as small **JSON files**.
Import them from the Hub with the **Import…** button of each section:

| Module | `"type"` | Where to import it | Where it shows up |
| --- | --- | --- | --- |
| Theme | `theme` | Hub › Theme | Theme › *Imported* (pick it per monitor; right-click to remove) |
| Animation | `animation` | Hub › Theme › Animations | Animations › *Style* |
| Widget | `widget` | Hub › Widgets | Widgets › *Import* list › **Add** |
| Start menu look | `startmenu` | Hub › Start menu | Start menu › *Import* list › **Apply** |

Modules are **declarative**: they describe colors, sizes and text, never code, XAML, scripts,
file paths or URLs, so importing one can't run anything on your PC. Imported files are copied to
`%LOCALAPPDATA%\StrataUI\Modules\<Themes|Animations|Widgets|StartMenus>`; you can also drop files
there by hand (they're picked up when the section opens). Importing a module with the same name
replaces the old one.

General rules:

- Comments (`// …`) and trailing commas are allowed. Property names and values are case-insensitive.
- Every module needs `"type"` and `"name"` (up to 60 characters). `"description"` and `"author"`
  are optional and shown in the Hub.
- Colors are `"#RRGGBB"` or `"#AARRGGBB"` (alpha first, like WPF).
- Numbers out of range are clamped instead of rejected; anything you leave out keeps its default.

Ready-made examples live in [`examples/`](examples/).

## Theme

```jsonc
{
  "type": "theme",
  "name": "Solarized Dark",
  "light": false,             // true / false; leave it out to follow Windows light/dark mode
  "backdrop": "solid",        // solid, mica, acrylic or blur
  "glass": false,             // glass look: clean blur with edges of light
  "tonal": false,             // Material-style surfaces tinted with the accent
  "floating": false,          // capsule taskbar detached from the screen edges
  "radius": 8,                // taskbar and button corners (0–24)
  "panelRadius": 12,          // flyout and Start menu corners (0–32)
  "colors": {                 // optional: without it the theme uses the usual light/dark colors
    "bar": "#002B36",         // taskbar
    "panel": "#073642",       // flyouts, Start menu, Hub
    "surface": "#0B3E4B",     // cards and controls on the panels
    "text": "#FDF6E3",
    "subText": "#93A1A1",
    "border": "#124D5B",
    "accent": "#268BD2"       // optional; used when Hub › Colors is set to "Same as Windows"
  }
}
```

With `colors` all six of `bar`, `panel`, `surface`, `text`, `subText` and `border` are required.
If `light` is missing, it's deduced from the panel color. Your own colors from Hub › Colors still
go on top.

## Animation

How flyouts, the Start menu, notifications, menus and Hub pages appear (`enter`) and go away
(`exit`). The speed chosen in the Hub still applies.

```jsonc
{
  "type": "animation",
  "name": "Swing",
  "enter": {
    "offset": 48,       // distance in pixels, coming from the taskbar side (-200–200)
    "scale": 0.9,       // starting size (0.3–1.5); 1 = no zoom
    "opacity": 0,       // starting opacity (0–1)
    "duration": 1.6,    // multiplies the Hub speed (0.2–4)
    "easing": "back",   // cubic, linear, sine, quint, back, elastic or bounce
    "amplitude": 0.6    // overshoot for back, springiness for elastic/bounce (0–3)
  },
  "exit": { "offset": 24, "scale": 0.95, "duration": 0.8, "easing": "cubic" }
}
```

`exit` is optional: without it the exit mirrors the entrance, a bit shorter. In the exit, `scale`
and `opacity` are the values it ends at.

## Widget

A desktop widget made of text lines. Like the built-in ones, it can be dragged around, resized
(small, medium, large) and locked from its right-click menu.

```jsonc
{
  "type": "widget",
  "name": "Big clock",
  "background": "#66000000",  // optional card behind the text
  "cornerRadius": 12,
  "padding": 12,
  "shadow": true,             // soft shadow so white text reads on any wallpaper
  "lines": [                  // 1 to 12 lines
    {
      "text": "{time:HH:mm}",
      "size": 96,             // font size at the "medium" widget size (6–200)
      "weight": "thin",       // thin, light, normal, semibold, bold or black
      "color": "accent",      // "#RRGGBB", "#AARRGGBB" or "accent"; white if left out
      "font": "Segoe UI Variable Display",
      "align": "center",      // left, center or right
      "opacity": 1,
      "spacing": 0            // space above the line; negative pulls it closer
    }
  ]
}
```

Placeholders you can use in `text`:

| Placeholder | Shows |
| --- | --- |
| `{time}` / `{time:format}` | Current time, e.g. `{time:HH:mm:ss}` ([.NET date format](https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings)) |
| `{date}` / `{date:format}` | Today's date, e.g. `{date:dddd d MMMM}` |
| `{song}`, `{artist}`, `{album}` | What's playing in any app (Spotify, browsers, Media Player…) |
| `{battery}` | Battery percentage (empty on desktops) |
| `{user}` | Your Windows user name |

A line whose placeholders are all empty (nothing playing, no battery) is hidden, and separators
left dangling by an empty one (`·`, `|`, `-`, `,`…) are trimmed from the ends. Days and months
follow the Windows regional format. The widget only updates every second if a format shows seconds.

## Start menu look

A preset of the Start menu options. Applying it changes only what the file includes.

```jsonc
{
  "type": "startmenu",
  "name": "Minimal",
  "style": "compact",       // windows11, list, fullscreen, compact, spotlight, icons or profile
  "iconSize": "small",      // small, medium or large
  "pinnedLabels": false,    // names under pinned apps
  "showPinned": true,
  "showAllApps": true,
  "showRecent": false,
  "showUser": false,
  "userPicture": true,
  "startIcon": "dot",       // strata, windows, windowsMono, circle, dot, grid or search
  "footer": ["Power"],      // footer buttons, in order (see below)
  "width": 380,             // menu size for this style
  "height": 560             // ignored by styles whose height follows their content
}
```

Footer buttons: `Power`, `Lock`, `Sleep`, `Restart`, `Shutdown`, `SignOut`, `Explorer`, `Documents`,
`Downloads`, `Pictures`, `Music`, `Videos`, `Settings`, `ControlPanel`, `TaskManager`, `Terminal`,
`StrataSettings`. Unknown ones are ignored.

**Export current** in Hub › Start menu saves your menu as it is now, ready to share.
