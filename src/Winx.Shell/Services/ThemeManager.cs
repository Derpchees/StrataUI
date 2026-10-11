using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Winx.Shell.Services;

/// <summary>Sigue el tema claro/oscuro y el color de acento de Windows.</summary>
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
    }

    public static void Shutdown() =>
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    public static bool IsLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    public static Color GetAccentColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DwmKey);
        if (key?.GetValue("AccentColor") is not int raw)
            return DefaultAccent;
        uint abgr = unchecked((uint)raw);
        return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
    }

    private static void Apply()
    {
        var resources = Application.Current.Resources;
        var theme = IsLightTheme() ? "Light" : "Dark";
        var next = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{theme}.xaml", UriKind.Absolute),
        };
        if (_current != null)
            resources.MergedDictionaries.Remove(_current);
        resources.MergedDictionaries.Insert(0, next);
        _current = next;
        var accent = GetAccentColor();
        // Como Windows: sobre fondo oscuro se usa una variante clara del acento para que contraste.
        resources["AccentBrush"] = new SolidColorBrush(IsLightTheme() ? accent : Lighten(accent, 0.4));
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
