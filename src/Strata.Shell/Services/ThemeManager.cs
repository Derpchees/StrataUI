using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Strata.Shell.Services.Modules;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Strata.Shell.Services;

/// <summary>
/// Aplica el tema de Strata UI (ver <see cref="ThemeCatalog"/>) a toda la app: genera la paleta,
/// sincroniza WPF-UI y avisa a las ventanas para que actualicen su material de fondo.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    private static ResourceDictionary? _current;

    /// <summary>Se dispara después de aplicar un tema (las ventanas actualizan su fondo).</summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// Tema general (Hub, menús, recursos de toda la app): el del monitor principal de Strata UI. Cada
    /// barra y los paneles que se abren desde ella usan el de su monitor (<see cref="ThemeFor"/>).
    /// </summary>
    public static ThemeInfo Theme => ThemeFor(MainDevice);

    public static bool IsLight => IsLightFor(MainDevice);

    /// <summary>Tema de vidrio con refracción real activa (y soportada por el equipo).</summary>
    public static bool UseLiveGlass => UseLiveGlassFor(MainDevice);

    /// <summary>Monitor principal de Strata UI (el de la bandeja), aunque las barras aún no existan.</summary>
    public static string MainDevice =>
        App.MainScreenDevice(System.Windows.Forms.Screen.AllScreens.Select(sc => (sc.DeviceName, sc.Primary)));

    public static ThemeInfo ThemeFor(string? device)
    {
        var screen = Settings.Current.For(device);
        return ModuleStore.Get<ThemeModule>(ModuleKind.Theme, screen.ThemeModule)?.ToTheme() ?? ThemeCatalog.Get(screen.Theme);
    }

    public static bool IsLightFor(string? device) => ThemeFor(device).Light ?? WindowsUsesLightTheme();

    public static bool UseLiveGlassFor(string? device) =>
        ThemeFor(device).Glass && Settings.Current.LiveGlass && Services.Themes.Glass.LiquidGlassEffect.IsSupported;

    /// <summary>
    /// Colores de un monitor cuando no coinciden con los generales (otro tema, claro/oscuro u otro
    /// tamaño de barra); null si son los mismos. La barra y sus paneles los agregan a sus recursos.
    /// </summary>
    public static ResourceDictionary? PaletteFor(string? device)
    {
        var theme = ThemeFor(device);
        bool light = IsLightFor(device);
        int height = Settings.Current.For(device).TaskbarHeight;
        if ((theme.Key, light, height) == (Theme.Key, IsLight, Settings.Current.For(MainDevice).TaskbarHeight))
            return null;
        var accent = Readable(AccentFor(theme), light);
        return Palette.Build(theme, light, accent, height, UseLiveGlassFor(device), light == IsLight ? _shownAccent : null,
                             CustomBarColor, CustomPanelColor);
    }

    private static Color? _shownAccent;

    /// <summary>
    /// Con un color propio de paneles que pide texto oscuro en un tema oscuro (o claro en uno claro),
    /// los textos y controles de WPF-UI del panel (que siguen el modo del tema) se ajustan a ese color.
    /// Null si no hace falta. Solo para los paneles: el Hub sigue con los colores del tema.
    /// </summary>
    public static ResourceDictionary? PanelContrastFor(string? device)
    {
        if (CustomPanelColor is not { } panel)
            return null;
        bool light = IsLightFor(device);
        var text = Palette.TextOn(panel, light);
        bool darkText = Luminance(text) < 0.5;
        if (darkText == light)
            return null; // el modo del tema ya da texto legible
        SolidColorBrush Brush(byte alpha) => new(Color.FromArgb(alpha, text.R, text.G, text.B));
        var dict = new ResourceDictionary
        {
            ["TextFillColorPrimaryBrush"] = Brush(0xFF),
            ["TextFillColorSecondaryBrush"] = Brush(0xC0),
            ["TextFillColorTertiaryBrush"] = Brush(0x8A),
            ["TextControlForeground"] = Brush(0xFF),
            ["TextControlForegroundPointerOver"] = Brush(0xFF),
            ["TextControlForegroundFocused"] = Brush(0xFF),
            ["TextControlPlaceholderForeground"] = Brush(0x9A),
            ["ButtonForeground"] = Brush(0xFF),
            ["ButtonForegroundPointerOver"] = Brush(0xFF),
            ["ButtonForegroundPressed"] = Brush(0xC0),
        };
        foreach (var (key, alpha) in new (string, byte)[]
                 {
                     ("TextControlBackground", 0x0F), ("TextControlBackgroundPointerOver", 0x16), ("TextControlBackgroundFocused", 0x0A),
                     ("ButtonBackground", 0x0F), ("ButtonBackgroundPointerOver", 0x18), ("ButtonBackgroundPressed", 0x0A),
                     ("ControlFillColorDefaultBrush", 0x0F), ("ControlFillColorSecondaryBrush", 0x18), ("ControlFillColorTertiaryBrush", 0x0A),
                     ("SubtleFillColorSecondaryBrush", 0x0F), ("CardBackgroundFillColorDefaultBrush", 0x08),
                 })
            dict[key] = Brush(alpha);
        return dict;
    }

    /// <summary>Colores propios de Hub › Colores (null = los del tema).</summary>
    public static Color? CustomBarColor => TryParseColor(Settings.Current.CustomBarColor);
    public static Color? CustomPanelColor => TryParseColor(Settings.Current.CustomPanelColor);

    /// <summary>
    /// Acento elegido: el del fondo de pantalla, uno propio o el de Windows. El del fondo queda en
    /// pausa mientras Wallpaper Engine ajusta el color de Windows (los dos se pisarían).
    /// </summary>
    public static Color Accent => AccentFor(Theme);

    /// <summary>Acento con un tema dado: el del fondo o el propio si se eligieron; si no, el del esquema o el de Windows.</summary>
    public static Color AccentFor(ThemeInfo theme) =>
        (UsesWallpaperAccent ? WallpaperAccent.Current : null)
        ?? TryParseColor(Settings.Current.AccentColor)
        ?? (theme.Scheme?.Accent is { } schemeAccent ? Palette.Hex(schemeAccent) : GetWindowsAccentColor());

    /// <summary>El acento sale del fondo (opción activa y sin Wallpaper Engine ajustando el color).</summary>
    public static bool UsesWallpaperAccent =>
        Settings.Current.AccentFromWallpaper && !WallpaperEngine.AdjustingWindowsColor;

    /// <summary>El acento es el de Windows (nada que llevarle a Windows).</summary>
    public static bool FollowsWindowsAccent =>
        !UsesWallpaperAccent && TryParseColor(Settings.Current.AccentColor) is null && Theme.Scheme?.Accent is null;

    /// <summary>Material para ventanas FluentWindow (Hub, paneles) según el tema.</summary>
    public static WindowBackdropType WindowBackdrop => Theme.Backdrop switch
    {
        Backdrop.Acrylic or Backdrop.Blur => WindowBackdropType.Acrylic,
        Backdrop.Mica => WindowBackdropType.Mica,
        _ => WindowBackdropType.None,
    };

    private static (string, string?, int, bool, bool, string?, string?) _appliedKey;
    private static HwndSource? _colorWatcher;

    public static void Initialize()
    {
        WallpaperEngine.Watch();
        WallpaperEngine.Recheck();
        WallpaperAccent.Update();
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Settings.Changed += OnSettingsChanged;
        WallpaperAccent.Changed += Recheck;
        WallpaperEngine.Changed += Recheck;
        ModuleStore.Changed += OnModulesChanged;
        WatchColorization();
    }

    /// <summary>Se reimportó o quitó un tema importado: si algún monitor lo usa, todo se vuelve a pintar.</summary>
    private static void OnModulesChanged(ModuleKind kind)
    {
        var s = Settings.Current;
        if (kind != ModuleKind.Theme || (s.ThemeModule is null && s.Screens.Values.All(screen => screen.ThemeModule is null)))
            return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _appliedKey = default;
            Settings.Update(_ => { });
        });
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Settings.Changed -= OnSettingsChanged;
        WallpaperAccent.Changed -= Recheck;
        WallpaperEngine.Changed -= Recheck;
        ModuleStore.Changed -= OnModulesChanged;
        WallpaperAccent.Shutdown();
        WallpaperEngine.Shutdown();
        _colorWatcher?.Dispose();
        _colorWatcher = null;
    }

    /// <summary>Solo se reaplica si cambió algo que afecta al tema (no, p. ej., los segundos del reloj).</summary>
    private static void OnSettingsChanged()
    {
        WallpaperAccent.Update();
        if (AppliedKey() != _appliedKey)
            Apply();
    }

    /// <summary>Lo que cambia los recursos generales: el tema y la barra del monitor principal, el acento y el vidrio.</summary>
    private static (string, string?, int, bool, bool, string?, string?) AppliedKey()
    {
        var s = Settings.Current;
        var main = s.For(MainDevice);
        return (ThemeFor(MainDevice).Key, s.AccentColor, main.TaskbarHeight, s.LiveGlass, s.AccentFromWallpaper, s.CustomBarColor, s.CustomPanelColor);
    }

    /// <summary>Reaplica si cambió lo que se ve (claro/oscuro o el acento efectivo).</summary>
    private static void Recheck()
    {
        if ((Theme.Key, IsLight, Accent) != _appliedLook)
            Apply();
    }

    /// <summary>
    /// Windows (o Wallpaper Engine) puede cambiar el acento sin mandar WM_SETTINGCHANGE: DWM avisa a
    /// todas las ventanas de primer nivel con WM_DWMCOLORIZATIONCOLORCHANGED, y una invisible lo recibe.
    /// </summary>
    private static void WatchColorization()
    {
        const int WmDwmColorizationColorChanged = 0x0320;
        _colorWatcher = new HwndSource(new HwndSourceParameters("Strata UI color")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, nunca se muestra
        });
        _colorWatcher.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WmDwmColorizationColorChanged)
                Application.Current.Dispatcher.BeginInvoke(Recheck, System.Windows.Threading.DispatcherPriority.Background);
            return IntPtr.Zero;
        });
    }

    /// <summary>
    /// Acento que se puede ver: sobre el tema oscuro no puede ser casi negro, ni casi blanco sobre
    /// el claro (Wallpaper Engine, p. ej., deja a Windows con el acento negro).
    /// </summary>
    public static Color Readable(Color accent, bool light)
    {
        var color = accent;
        for (int i = 0; i < 20; i++)
        {
            double luminance = Luminance(color);
            if (light ? luminance <= 0.45 : luminance >= 0.16)
                break;
            color = Palette.Blend(color, light ? Colors.Black : Colors.White, 0.08);
        }
        return color;
    }

    /// <summary>Luminancia relativa (WCAG), de 0 (negro) a 1 (blanco).</summary>
    internal static double Luminance(Color c)
    {
        static double Channel(byte value)
        {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    public static bool WindowsUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    public static Color GetWindowsAccentColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DwmKey);
        if (key?.GetValue("AccentColor") is not int raw)
            return DefaultAccent;
        uint abgr = unchecked((uint)raw);
        return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
    }

    public static Color? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void Apply()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _appliedKey = AppliedKey();
        var theme = Theme;
        bool light = IsLight;
        var chosen = Accent;
        var accent = Readable(chosen, light);

        // 1) WPF-UI: recargar su tema re-evalúa todos los controles abiertos, así que solo se hace
        //    si cambió claro/oscuro. El material de cada ventana lo pone cada ventana (pasar un
        //    backdrop aquí lo aplicaría a MainWindow).
        var uiTheme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        if (uiTheme != _uiTheme)
        {
            ApplicationThemeManager.Apply(uiTheme, WindowBackdropType.None, updateAccent: false);
            _uiTheme = uiTheme;
            _uiAccent = null;
        }
        if (accent != _uiAccent)
        {
            ApplicationAccentColorManager.Apply(accent, uiTheme, false, false);
            _uiAccent = accent;
        }

        // 2) Paleta de Strata UI al final: así sus claves sobrescriben las de WPF-UI (tarjetas, menús,
        //    tooltips) y todo el programa sigue el tema, no solo la barra.
        var resources = Application.Current.Resources;
        // El acento que se ve en Strata UI es el mismo que usan los controles de WPF-UI (interruptores,
        // botones principales, selección), no un cálculo aparte con otro tono.
        Color? shown = (resources["AccentFillColorDefaultBrush"] as SolidColorBrush)?.Color;
        _shownAccent = shown;
        var next = Palette.Build(theme, light, accent, Settings.Current.For(MainDevice).TaskbarHeight, UseLiveGlass, shown,
                                 CustomBarColor, CustomPanelColor);
        if (_current != null)
            resources.MergedDictionaries.Remove(_current);
        resources.MergedDictionaries.Add(next);
        _current = next;
        _appliedLook = (theme.Key, light, chosen);

        ThemeChanged?.Invoke();
        Log.Info($"Tema {theme.Name} ({(light ? "claro" : "oscuro")}) aplicado en {watch.ElapsedMilliseconds} ms");
    }

    private static ApplicationTheme? _uiTheme;
    private static Color? _uiAccent;
    private static (string?, bool, Color) _appliedLook;

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color
                               or UserPreferenceCategory.VisualStyle))
            return;
        // Windows manda "General" por muchos motivos (fondo, fuentes…): solo se reaplica si
        // cambió algo que el tema usa (claro/oscuro de Windows o su acento).
        Application.Current.Dispatcher.BeginInvoke(Recheck);
    }
}
