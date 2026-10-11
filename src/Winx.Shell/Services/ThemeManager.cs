using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Winx.Shell.Services;

/// <summary>
/// Tema claro/oscuro y color de acento de WINX. Por defecto siguen a Windows;
/// el Hub permite fijarlos. Se aplica tanto a la barra como a los controles de WPF-UI.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    private static ResourceDictionary? _current;

    public static void Initialize()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Settings.Changed += Apply;
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Settings.Changed -= Apply;
    }

    public static bool IsLight => Settings.Current.Theme switch
    {
        WinxTheme.Light => true,
        WinxTheme.Dark => false,
        _ => WindowsUsesLightTheme(),
    };

    public static Color Accent =>
        TryParseColor(Settings.Current.AccentColor) ?? GetWindowsAccentColor();

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
        bool light = IsLight;
        var resources = Application.Current.Resources;
        var next = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(light ? "Light" : "Dark")}.xaml", UriKind.Absolute),
        };
        if (_current != null)
            resources.MergedDictionaries.Remove(_current);
        resources.MergedDictionaries.Add(next);
        _current = next;

        var accent = Accent;
        // Como Windows: sobre fondo oscuro se usa una variante clara del acento para que contraste.
        resources["WinxAccentBrush"] = new SolidColorBrush(light ? accent : Lighten(accent, 0.4));

        var theme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
        ApplicationAccentColorManager.Apply(accent, theme, false, false);
    }

    private static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)(color.R + (255 - color.R) * amount),
        (byte)(color.G + (255 - color.G) * amount),
        (byte)(color.B + (255 - color.B) * amount));

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)
        {
            Application.Current.Dispatcher.BeginInvoke(Apply);
        }
    }
}
