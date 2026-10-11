using System.Windows;
using System.Windows.Media;

namespace Winx.Shell.Services.Themes;

/// <summary>
/// Genera los recursos de color de WINX a partir del tema, el modo claro/oscuro y el acento.
/// Todos los colores de la barra, paneles y menús salen de aquí.
/// </summary>
public static class Palette
{
    public static ResourceDictionary Build(ThemeInfo theme, bool light, Color accent, double barHeight = 48)
    {
        // Base neutra (equivalente a la paleta de Windows 11)
        Color background = light ? Hex("#EEEEEE") : Hex("#1C1C1C");
        Color flyout = light ? Hex("#F9F9F9") : Hex("#2B2B2B");
        Color border = light ? Hex("#D6D6D6") : Hex("#2E2E2E");
        Color flyoutBorder = light ? Hex("#DADADA") : Hex("#3C3C3C");
        Color foreground = light ? Hex("#1B1B1B") : Colors.White;
        Color secondary = light ? Hex("#5C5C5C") : Hex("#C5C5C5");
        Color hover = light ? Hex("#14000000") : Hex("#1AFFFFFF");
        Color pressed = light ? Hex("#0A000000") : Hex("#0FFFFFFF");
        Color active = light ? Hex("#0F000000") : Hex("#14FFFFFF");
        Color indicator = light ? Hex("#8A8A8A") : Hex("#9A9A9A");
        Color surface = light ? Hex("#B3FFFFFF") : Hex("#0DFFFFFF");
        Color hubTint = Colors.Transparent;

        if (theme.Tonal)
        {
            // Material Design: superficies tonales derivadas del acento, sin transparencia.
            background = Blend(light ? Hex("#FEF7FF") : Hex("#141218"), accent, light ? 0.08 : 0.10);
            flyout = Blend(light ? Hex("#F7F2FA") : Hex("#211F26"), accent, light ? 0.10 : 0.12);
            border = Blend(border, accent, 0.25);
            flyoutBorder = Blend(flyoutBorder, accent, 0.25);
            hover = WithAlpha(accent, 0x22);
            pressed = WithAlpha(accent, 0x33);
            active = WithAlpha(accent, 0x2E);
            surface = Blend(flyout, accent, 0.10);
            hubTint = flyout;
        }
        else if (theme.Glass)
        {
            // Liquid Glass (iOS/HyperOS): desenfoque limpio con un tinte mínimo, sin el brillo
            // lechoso de Aero. La profundidad la dan los bordes de luz (ver GlassRim).
            background = light ? Hex("#4DF5F5FA") : Hex("#3D0A0A0C");
            flyout = light ? Hex("#5CF5F5FA") : Hex("#4D0E0E10");
            border = Colors.Transparent;
            flyoutBorder = light ? Hex("#33FFFFFF") : Hex("#1AFFFFFF");
            hover = light ? Hex("#26FFFFFF") : Hex("#1FFFFFFF");
            pressed = light ? Hex("#14FFFFFF") : Hex("#14FFFFFF");
            active = light ? Hex("#40FFFFFF") : Hex("#26FFFFFF");
            surface = light ? Hex("#40FFFFFF") : Hex("#14FFFFFF");
            hubTint = light ? Hex("#1AFFFFFF") : Hex("#1A000000");
            foreground = light ? Hex("#111114") : Colors.White;
            secondary = light ? Hex("#4A4A52") : Hex("#D0D0D6");
        }
        else if (theme.Backdrop == Backdrop.Acrylic)
        {
            // Transparencia: el mismo color base, semitransparente sobre el Acrylic.
            background = WithAlpha(background, light ? (byte)0xB3 : (byte)0x99);
            flyout = WithAlpha(flyout, 0x66);
            hubTint = WithAlpha(flyout, 0x33);
        }

        // Acento legible: sobre fondo oscuro se aclara, como hace Windows.
        Color accentShown = light ? accent : Blend(accent, Colors.White, 0.4);

        var dict = new ResourceDictionary
        {
            ["WinxTaskbarBackgroundBrush"] = Brush(background),
            ["WinxTaskbarBorderBrush"] = Brush(border),
            ["WinxForegroundBrush"] = Brush(foreground),
            ["WinxSecondaryForegroundBrush"] = Brush(secondary),
            ["WinxHoverBrush"] = Brush(hover),
            ["WinxPressedBrush"] = Brush(pressed),
            ["WinxActiveTaskBrush"] = Brush(active),
            ["WinxInactiveIndicatorBrush"] = Brush(indicator),
            ["WinxFlyoutBackgroundBrush"] = Brush(flyout),
            ["WinxFlyoutBorderBrush"] = Brush(flyoutBorder),
            // Popups de WPF no tienen Acrylic detrás: necesitan un fondo opaco para ser legibles.
            ["WinxPopupBackgroundBrush"] = Brush(WithAlpha(theme.Tonal ? flyout : light ? Hex("#F9F9F9") : Hex("#2B2B2B"), 0xF5)),
            ["WinxSurfaceBrush"] = Brush(surface),
            ["WinxHubTintBrush"] = Brush(hubTint),
            ["WinxAccentBrush"] = Brush(accentShown),
            ["WinxGlassEdgeBrush"] = theme.Glass ? GlassRim(light) : Brushes.Transparent,
            ["WinxGlassHighlightBrush"] = theme.Glass ? GlassSheen(light) : Brushes.Transparent,
            ["WinxGlassVisibility"] = theme.Glass ? Visibility.Visible : Visibility.Collapsed,
            ["WinxCornerRadius"] = new CornerRadius(theme.Radius),
            ["WinxLargeCornerRadius"] = new CornerRadius(theme.Radius + 4),
            ["WinxPanelCornerRadius"] = new CornerRadius(theme.PanelRadius),
            // Barra: pegada al borde con línea superior, o cápsula flotante.
            ["WinxTaskbarMargin"] = theme.Floating
                ? new Thickness(ThemeCatalog.FloatingMargin, 0, ThemeCatalog.FloatingMargin, ThemeCatalog.FloatingMargin)
                : new Thickness(0),
            ["WinxTaskbarCornerRadius"] = new CornerRadius(theme.Floating ? barHeight / 2 : 0),
            ["WinxTaskbarBorderThickness"] = theme.Floating ? new Thickness(1) : new Thickness(0, 1, 0, 0),
            ["WinxTaskbarEdgeBrush"] = theme.Glass ? GlassRim(light) : Brush(border),
        };
        return dict;
    }

    /// <summary>
    /// Borde de luz del vidrio (iOS/HyperOS): un filo brillante arriba a la izquierda que se
    /// apaga hacia el centro y reaparece tenue abajo a la derecha, como luz refractada.
    /// </summary>
    private static Brush GlassRim(bool light)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(light ? Hex("#F2FFFFFF") : Hex("#B3FFFFFF"), 0));
        brush.GradientStops.Add(new GradientStop(light ? Hex("#40FFFFFF") : Hex("#1FFFFFFF"), 0.35));
        brush.GradientStops.Add(new GradientStop(light ? Hex("#1AFFFFFF") : Hex("#0DFFFFFF"), 0.6));
        brush.GradientStops.Add(new GradientStop(light ? Hex("#99FFFFFF") : Hex("#4DFFFFFF"), 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>Brillo interior apenas perceptible en el tercio superior (no el degradado de Aero).</summary>
    private static Brush GlassSheen(bool light)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(light ? Hex("#26FFFFFF") : Hex("#14FFFFFF"), 0));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.35));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    public static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        from.A,
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));
}
