using System.Windows;
using System.Windows.Media;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Genera los recursos de color de Strata UI a partir del tema, el modo claro/oscuro y el acento.
/// Todos los colores de la barra, paneles y menús salen de aquí.
/// </summary>
public static class Palette
{
    /// <param name="shown">Acento tal como lo pintan los controles (el de WPF-UI); si falta, se calcula.</param>
    /// <param name="barColor">Color propio de la barra (Hub › Colores), con su opacidad; null = el del tema.</param>
    /// <param name="panelColor">Color propio de los paneles y el menú Inicio; null = el del tema.</param>
    public static ResourceDictionary Build(ThemeInfo theme, bool light, Color accent, double barHeight = 48, bool liveGlass = false,
                                           Color? shown = null, Color? barColor = null, Color? panelColor = null)
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

        if (theme.Scheme is { } scheme)
        {
            // Esquema de color (Nord, Dracula…) sobre el estilo elegido: opacos en los estilos sólidos y
            // Mica; sobre vidrio o transparencia, translúcidos para que se siga viendo el efecto.
            // (Con menos, el material de Windows lo aclaraba tanto que el esquema no se reconocía.)
            (byte barAlpha, byte panelAlpha) = theme.Glass ? (liveGlass ? ((byte)0x80, (byte)0x99) : ((byte)0xB3, (byte)0xCC))
                : theme.Backdrop == Backdrop.Acrylic ? ((byte)0xE6, (byte)0xD9)
                : ((byte)0xFF, (byte)0xFF);
            background = WithAlpha(Hex(scheme.Bar), barAlpha);
            flyout = WithAlpha(Hex(scheme.Panel), panelAlpha);
            border = Hex(scheme.Border);
            flyoutBorder = Hex(scheme.Border);
            foreground = Hex(scheme.Text);
            secondary = Hex(scheme.SubText);
            hover = WithAlpha(foreground, 0x1A);
            pressed = WithAlpha(foreground, 0x10);
            active = WithAlpha(foreground, 0x14);
            indicator = secondary;
            surface = Hex(scheme.Surface);
            hubTint = flyout;
        }
        else if (theme.Tonal)
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
            if (liveGlass)
            {
                // Con refracción en vivo el shader ya tiñe y da brillo: sin capas encima.
                background = Colors.Transparent;
                flyout = Colors.Transparent;
            }
        }
        else if (theme.Backdrop == Backdrop.Acrylic)
        {
            // Transparencia: el mismo color base, semitransparente sobre el Acrylic.
            background = WithAlpha(background, light ? (byte)0xB3 : (byte)0x99);
            flyout = WithAlpha(flyout, 0x66);
            hubTint = WithAlpha(flyout, 0x33);
        }

        // Colores propios encima del tema: el texto de cada parte sale de su color para que se lea.
        Color barForeground = foreground;
        if (barColor is { } customBar)
        {
            background = customBar;
            barForeground = TextOn(customBar, light);
        }
        if (panelColor is { } customPanel)
        {
            flyout = customPanel;
            surface = Blend(customPanel, TextOn(customPanel, light), 0.08);
            foreground = TextOn(customPanel, light);
            secondary = WithAlpha(foreground, 0xB3);
            hover = WithAlpha(foreground, 0x1A);
            pressed = WithAlpha(foreground, 0x10);
            active = WithAlpha(foreground, 0x14);
            if (barColor is null)
                barForeground = foreground;
        }

        // Acento legible: sobre fondo oscuro se aclara, como hace Windows.
        Color accentShown = shown ?? (light ? accent : Blend(accent, Colors.White, 0.4));

        var dict = new ResourceDictionary
        {
            ["StrataTaskbarBackgroundBrush"] = Brush(background),
            ["StrataTaskbarBorderBrush"] = Brush(border),
            ["StrataForegroundBrush"] = Brush(foreground),
            // Texto de la barra: el mismo, salvo que la barra tenga un color propio (ver Taskbar.ApplyScreenPalette).
            ["StrataBarForegroundBrush"] = Brush(barForeground),
            ["StrataSecondaryForegroundBrush"] = Brush(secondary),
            ["StrataHoverBrush"] = Brush(hover),
            ["StrataPressedBrush"] = Brush(pressed),
            ["StrataActiveTaskBrush"] = Brush(active),
            ["StrataInactiveIndicatorBrush"] = Brush(indicator),
            ["StrataFlyoutBackgroundBrush"] = Brush(flyout),
            ["StrataFlyoutBorderBrush"] = Brush(flyoutBorder),
            // Popups de WPF no tienen Acrylic detrás: necesitan un fondo opaco para ser legibles.
            ["StrataPopupBackgroundBrush"] = Brush(WithAlpha(theme.Tonal || theme.Scheme != null ? flyout : light ? Hex("#F9F9F9") : Hex("#2B2B2B"), 0xF5)),
            ["StrataSurfaceBrush"] = Brush(surface),
            ["StrataHubTintBrush"] = Brush(hubTint),
            ["StrataAccentBrush"] = Brush(accentShown),
            ["StrataGlassEdgeBrush"] = theme.Glass ? GlassRim(light) : Brushes.Transparent,
            ["StrataGlassHighlightBrush"] = theme.Glass && !liveGlass ? GlassSheen(light) : Brushes.Transparent,
            ["StrataGlassVisibility"] = theme.Glass ? Visibility.Visible : Visibility.Collapsed,
            ["StrataCornerRadius"] = new CornerRadius(theme.Radius),
            ["StrataLargeCornerRadius"] = new CornerRadius(theme.Radius + 4),
            ["StrataPanelCornerRadius"] = new CornerRadius(theme.PanelRadius),
            // Barra: pegada al borde con línea superior, o cápsula flotante (cada barra los ajusta a
            // su borde y su grosor; ver Taskbar.ApplyBackdrop).
            ["StrataTaskbarCornerRadius"] = new CornerRadius(theme.Floating ? barHeight / 2 : 0),
            ["StrataTaskbarBorderThickness"] = theme.Floating ? new Thickness(1) : new Thickness(0, 1, 0, 0),
            ["StrataTaskbarEdgeBrush"] = theme.Glass ? GlassRim(light) : Brush(border),
        };
        AddControlOverrides(dict, theme, light, accent, background);
        if (theme.Scheme != null)
        {
            // El texto de los controles del Hub también con los colores del esquema.
            dict["TextFillColorPrimaryBrush"] = Brush(foreground);
            dict["TextFillColorSecondaryBrush"] = Brush(secondary);
            dict["TextFillColorTertiaryBrush"] = Brush(WithAlpha(secondary, 0xC0));
        }
        return dict;
    }

    /// <summary>
    /// Sobrescribe los pinceles de WPF-UI que usan los controles (tarjetas del Hub, botones, cajas
    /// de texto, menús contextuales, tooltips, desplegables) para que sigan el tema y no se queden
    /// con el gris por defecto. Los temas Sistema/Claro/Oscuro usan los de WPF-UI tal cual.
    /// </summary>
    private static void AddControlOverrides(ResourceDictionary dict, ThemeInfo theme, bool light, Color accent, Color barColor)
    {
        Color card, cardHover, cardBorder, control, controlHover, controlPressed, input, popup, popupBorder, selected, appBackground;

        if (theme.Glass)
        {
            card = light ? Hex("#59FFFFFF") : Hex("#1FFFFFFF");
            cardHover = light ? Hex("#80FFFFFF") : Hex("#2EFFFFFF");
            cardBorder = light ? Hex("#80FFFFFF") : Hex("#24FFFFFF");
            control = light ? Hex("#66FFFFFF") : Hex("#26FFFFFF");
            controlHover = light ? Hex("#8CFFFFFF") : Hex("#38FFFFFF");
            controlPressed = light ? Hex("#40FFFFFF") : Hex("#1AFFFFFF");
            input = light ? Hex("#B3FFFFFF") : Hex("#59000000");
            // Los popups de WPF no tienen desenfoque detrás: casi opacos para que se lean.
            popup = light ? Hex("#F2F5F5F8") : Hex("#F21C1C20");
            popupBorder = light ? Hex("#FFFFFFFF") : Hex("#33FFFFFF");
            selected = light ? Hex("#73FFFFFF") : Hex("#26FFFFFF");
            appBackground = Colors.Transparent;
        }
        else if (theme.Scheme is { } scheme)
        {
            // Esquema: tarjetas y controles un poco más claros (u oscuros) que el panel.
            var panel = Hex(scheme.Panel);
            var text = Hex(scheme.Text);
            card = Blend(panel, text, 0.04);
            cardHover = Blend(panel, text, 0.08);
            cardBorder = Hex(scheme.Border);
            control = Blend(panel, text, 0.07);
            controlHover = Blend(panel, text, 0.11);
            controlPressed = Blend(panel, text, 0.04);
            input = Blend(Hex(scheme.Bar), panel, 0.5);
            popup = WithAlpha(panel, 0xF8);
            popupBorder = Hex(scheme.Border);
            selected = WithAlpha(accent, 0x40);
            // El de la barra, con la transparencia que le dio el estilo.
            appBackground = barColor;
        }
        else if (theme.Tonal)
        {
            // Material Design 3: contenedores tonales a distintas alturas, sin bordes.
            var surface = light ? Hex("#FEF7FF") : Hex("#141218");
            card = Blend(surface, accent, light ? 0.10 : 0.12);
            cardHover = Blend(surface, accent, light ? 0.16 : 0.18);
            cardBorder = Colors.Transparent;
            control = Blend(surface, accent, light ? 0.20 : 0.24);
            controlHover = Blend(surface, accent, light ? 0.26 : 0.30);
            controlPressed = Blend(surface, accent, light ? 0.32 : 0.36);
            input = Blend(surface, accent, light ? 0.14 : 0.16);
            popup = Blend(surface, accent, light ? 0.12 : 0.14);
            popupBorder = Colors.Transparent;
            selected = WithAlpha(accent, 0x40);
            appBackground = barColor;
        }
        else
        {
            return;
        }

        void Set(string key, Color color) => dict[key] = Brush(color);

        Set("ApplicationBackgroundBrush", appBackground);
        foreach (var key in new[] { "CardBackground", "CardBackgroundFillColorDefaultBrush", "ExpanderHeaderBackground" })
            Set(key, card);
        foreach (var key in new[] { "CardBackgroundPointerOver", "CardBackgroundFillColorSecondaryBrush", "ExpanderContentBackground" })
            Set(key, cardHover);
        Set("CardBackgroundPressed", controlPressed);
        foreach (var key in new[] { "CardBorderBrush", "CardStrokeColorDefaultBrush", "ExpanderHeaderBorderBrush" })
            Set(key, cardBorder);

        foreach (var key in new[] { "ButtonBackground", "ComboBoxBackground", "ComboBoxBackgroundFocused", "ToggleButtonBackground",
                                    "TextControlBackground", "ControlFillColorDefaultBrush" })
            Set(key, control);
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ComboBoxBackgroundPointerOver", "ToggleButtonBackgroundPointerOver",
                                    "TextControlBackgroundPointerOver", "ControlFillColorSecondaryBrush" })
            Set(key, controlHover);
        foreach (var key in new[] { "ButtonBackgroundPressed", "ToggleButtonBackgroundPressed", "ControlFillColorTertiaryBrush" })
            Set(key, controlPressed);
        Set("TextControlBackgroundFocused", input);

        foreach (var key in new[] { "ContextMenuBackground", "ToolTipBackground", "FlyoutBackground", "ComboBoxDropDownBackground",
                                    "SnackBarBackground", "MessageBoxBackground", "ContentDialogBackground" })
            Set(key, popup);
        foreach (var key in new[] { "ContextMenuBorderBrush", "ToolTipBorderBrush", "FlyoutBorderBrush", "ComboBoxDropDownBorderBrush" })
            Set(key, popupBorder);

        foreach (var key in new[] { "NavigationViewItemBackgroundSelectedLeftFluent", "NavigationViewItemBackgroundSelected",
                                    "ComboBoxItemBackgroundSelected", "SubtleFillColorSecondaryBrush" })
            Set(key, selected);
        Set("NavigationViewItemBackgroundPointerOver", WithAlpha(selected, (byte)(selected.A * 2 / 3)));

        // Esquinas: más redondeadas en Material y vidrio (los controles de WPF-UI las leen de aquí).
        dict["ControlCornerRadius"] = new CornerRadius(theme.Tonal ? 12 : 8);
        dict["PopupCornerRadius"] = new CornerRadius(theme.Tonal ? 16 : 12);
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

    /// <summary>
    /// Texto que se lee sobre un color (con su transparencia, contra un fondo claro u oscuro según el
    /// tema): casi negro o blanco.
    /// </summary>
    internal static Color TextOn(Color color, bool lightTheme)
    {
        var behind = lightTheme ? Hex("#F3F3F3") : Hex("#202020");
        var seen = Blend(behind, Color.FromRgb(color.R, color.G, color.B), color.A / 255.0);
        return ThemeManager.Luminance(seen) > 0.42 ? Hex("#1B1B1B") : Colors.White;
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
