using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Winx.Shell.Services.Themes;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Winx.Shell.Services;

/// <summary>
/// Aplica el tema de WINX (ver <see cref="ThemeCatalog"/>) a toda la app: genera la paleta,
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

    public static ThemeInfo Theme => ThemeCatalog.Get(Settings.Current.Theme);

    public static bool IsLight => Theme.Light ?? WindowsUsesLightTheme();

    /// <summary>Tema de vidrio con refracción real activa (y soportada por el equipo).</summary>
    public static bool UseLiveGlass =>
        Theme.Glass && Settings.Current.LiveGlass && Services.Themes.Glass.LiquidGlassEffect.IsSupported;

    public static Color Accent =>
        TryParseColor(Settings.Current.AccentColor) ?? GetWindowsAccentColor();

    /// <summary>Material para ventanas FluentWindow (Hub, paneles) según el tema.</summary>
    public static WindowBackdropType WindowBackdrop => Theme.Backdrop switch
    {
        Backdrop.Acrylic or Backdrop.Blur => WindowBackdropType.Acrylic,
        Backdrop.Mica => WindowBackdropType.Mica,
        _ => WindowBackdropType.None,
    };

    private static (WinxTheme, string?, int, bool) _appliedKey;

    public static void Initialize()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Settings.Changed += OnSettingsChanged;
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Settings.Changed -= OnSettingsChanged;
    }

    /// <summary>Solo se reaplica si cambió algo que afecta al tema (no, p. ej., los segundos del reloj).</summary>
    private static void OnSettingsChanged()
    {
        var s = Settings.Current;
        if ((s.Theme, s.AccentColor, s.TaskbarHeight, s.LiveGlass) != _appliedKey)
            Apply();
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
        var s = Settings.Current;
        _appliedKey = (s.Theme, s.AccentColor, s.TaskbarHeight, s.LiveGlass);
        var theme = Theme;
        bool light = IsLight;
        var accent = Accent;

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

        // 2) Paleta de WINX al final: así sus claves sobrescriben las de WPF-UI (tarjetas, menús,
        //    tooltips) y todo el programa sigue el tema, no solo la barra.
        var resources = Application.Current.Resources;
        var next = Palette.Build(theme, light, accent, Settings.Current.TaskbarHeight, UseLiveGlass);
        if (_current != null)
            resources.MergedDictionaries.Remove(_current);
        resources.MergedDictionaries.Add(next);
        _current = next;
        _appliedLook = (theme.Kind, light, accent);

        ThemeChanged?.Invoke();
        Log.Info($"Tema {theme.Name} ({(light ? "claro" : "oscuro")}) aplicado en {watch.ElapsedMilliseconds} ms");
    }

    private static ApplicationTheme? _uiTheme;
    private static Color? _uiAccent;
    private static (WinxTheme, bool, Color) _appliedLook;

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color
                               or UserPreferenceCategory.VisualStyle))
            return;
        // Windows manda "General" por muchos motivos (fondo, fuentes…): solo se reaplica si
        // cambió algo que el tema usa (claro/oscuro de Windows o su acento).
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if ((Theme.Kind, IsLight, Accent) != _appliedLook)
                Apply();
        });
    }
}
