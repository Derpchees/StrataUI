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

    public static Color Accent =>
        TryParseColor(Settings.Current.AccentColor) ?? GetWindowsAccentColor();

    /// <summary>Material para ventanas FluentWindow (Hub, paneles) según el tema.</summary>
    public static WindowBackdropType WindowBackdrop => Theme.Backdrop switch
    {
        Backdrop.Acrylic or Backdrop.Blur => WindowBackdropType.Acrylic,
        Backdrop.Mica => WindowBackdropType.Mica,
        _ => WindowBackdropType.None,
    };

    private static (WinxTheme, string?, int) _appliedKey;

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
        if ((s.Theme, s.AccentColor, s.TaskbarHeight) != _appliedKey)
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
        var s = Settings.Current;
        _appliedKey = (s.Theme, s.AccentColor, s.TaskbarHeight);
        var theme = Theme;
        bool light = IsLight;
        var accent = Accent;

        var resources = Application.Current.Resources;
        var next = Palette.Build(theme, light, accent, Settings.Current.TaskbarHeight);
        if (_current != null)
            resources.MergedDictionaries.Remove(_current);
        resources.MergedDictionaries.Add(next);
        _current = next;

        // WPF-UI: solo el tema de controles y el acento. El material de cada ventana lo pone
        // cada ventana (pasar un backdrop aquí lo aplicaría a MainWindow, que es la barra).
        var uiTheme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(uiTheme, WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply(accent, uiTheme, false, false);

        ThemeChanged?.Invoke();
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)
        {
            Application.Current.Dispatcher.BeginInvoke(Apply);
        }
    }
}
