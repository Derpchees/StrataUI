using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Material de fondo de las ventanas de Strata UI.
/// Blur = desenfoque limpio sin ruido ni capa de luminosidad (vidrio estilo iOS/HyperOS).
/// </summary>
public enum Backdrop { Solid, Mica, Acrylic, Blur }

/// <summary>
/// Colores fijos de un esquema (Nord, Dracula…): barra, paneles, superficies, texto y bordes. El
/// acento es el del esquema salvo que se elija otro (del fondo o propio).
/// </summary>
public sealed record ColorScheme(string Bar, string Panel, string Surface, string Text, string SubText, string Border, string? Accent = null);

/// <summary>
/// Definición de un tema. <see cref="Light"/> null = sigue el modo claro/oscuro de Windows.
/// <see cref="Glass"/> agrega bordes de luz especulares (Liquid Glass); <see cref="Floating"/>
/// separa la barra de los bordes en forma de cápsula; <see cref="Tonal"/> tiñe las superficies
/// con el acento (Material Design). <see cref="PanelRadius"/> es el redondeo de los paneles.
/// </summary>
public sealed record ThemeInfo(
    StrataTheme Kind,
    string Name,
    string Description,
    bool? Light,
    Backdrop Backdrop,
    bool Glass = false,
    bool Tonal = false,
    bool Floating = false,
    double Radius = 5,
    double PanelRadius = 8,
    ColorScheme? Scheme = null);

public static class ThemeCatalog
{
    /// <summary>Separación de la barra flotante respecto a los bordes de la pantalla.</summary>
    public const double FloatingMargin = 10;

    public static IReadOnlyList<ThemeInfo> All { get; } =
    [
        new(StrataTheme.System, Loc.T("Sistema"), Loc.T("Claro u oscuro según Windows, con Mica."), null, Backdrop.Mica),
        new(StrataTheme.Light, Loc.T("Claro"), Loc.T("Superficies claras y limpias."), true, Backdrop.Mica),
        new(StrataTheme.Dark, Loc.T("Oscuro"), Loc.T("Superficies oscuras, ideal de noche."), false, Backdrop.Mica),
        new(StrataTheme.Transparent, Loc.T("Transparencia"), Loc.T("Vidrio esmerilado que deja ver el fondo."), null, Backdrop.Acrylic, Radius: 6),
        new(StrataTheme.LiquidGlass, "Liquid Glass", Loc.T("Vidrio claro estilo iOS: cristalino, flotante, con bordes de luz."),
            true, Backdrop.Blur, Glass: true, Floating: true, Radius: 14, PanelRadius: 22),
        new(StrataTheme.LiquidGlassDark, Loc.T("Liquid Glass oscuro"), Loc.T("Vidrio ahumado estilo iOS: cristalino, flotante, con bordes de luz."),
            false, Backdrop.Blur, Glass: true, Floating: true, Radius: 14, PanelRadius: 22),
        new(StrataTheme.Material, "Material", Loc.T("Material Design: superficies tonales con tu acento."), null, Backdrop.Solid,
            Tonal: true, Radius: 14, PanelRadius: 16),

        // ── Esquemas de color ──────────────────────────────────────────────────
        new(StrataTheme.Midnight, Loc.T("Medianoche"), Loc.T("Negro puro para pantallas OLED, con tu acento."), false, Backdrop.Solid, Radius: 8, PanelRadius: 12,
            Scheme: new("#000000", "#0A0A0A", "#161616", "#FFFFFF", "#A3A3A3", "#1F1F1F")),
        new(StrataTheme.Nord, "Nord", Loc.T("Azules árticos, suaves y de bajo contraste."), false, Backdrop.Solid, Radius: 8, PanelRadius: 12,
            Scheme: new("#2E3440", "#3B4252", "#434C5E", "#ECEFF4", "#D8DEE9", "#4C566A", "#88C0D0")),
        new(StrataTheme.Dracula, "Dracula", Loc.T("Oscuro con morados, rosas y verdes intensos."), false, Backdrop.Solid, Radius: 8, PanelRadius: 12,
            Scheme: new("#21222C", "#282A36", "#343746", "#F8F8F2", "#C9CBD8", "#44475A", "#BD93F9")),
        new(StrataTheme.CatppuccinMocha, "Catppuccin Mocha", Loc.T("Pasteles cálidos sobre un fondo oscuro."), false, Backdrop.Solid, Radius: 10, PanelRadius: 14,
            Scheme: new("#181825", "#1E1E2E", "#313244", "#CDD6F4", "#A6ADC8", "#313244", "#CBA6F7")),
        new(StrataTheme.CatppuccinLatte, "Catppuccin Latte", Loc.T("Pasteles sobre un fondo claro y cálido."), true, Backdrop.Solid, Radius: 10, PanelRadius: 14,
            Scheme: new("#DCE0E8", "#EFF1F5", "#E6E9EF", "#4C4F69", "#6C6F85", "#CCD0DA", "#8839EF")),
        new(StrataTheme.TokyoNight, "Tokyo Night", Loc.T("Noche de ciudad: azules profundos y neón suave."), false, Backdrop.Solid, Radius: 8, PanelRadius: 12,
            Scheme: new("#16161E", "#1A1B26", "#24283B", "#C0CAF5", "#A9B1D6", "#292E42", "#7AA2F7")),
        new(StrataTheme.RosePine, "Rosé Pine", Loc.T("Tonos apagados con rosas y lavandas."), false, Backdrop.Solid, Radius: 10, PanelRadius: 14,
            Scheme: new("#191724", "#1F1D2E", "#26233A", "#E0DEF4", "#908CAA", "#26233A", "#C4A7E7")),
        new(StrataTheme.Gruvbox, "Gruvbox", Loc.T("Retro: marrones cálidos con amarillo y naranja."), false, Backdrop.Solid, Radius: 6, PanelRadius: 10,
            Scheme: new("#1D2021", "#282828", "#3C3836", "#EBDBB2", "#D5C4A1", "#504945", "#FABD2F")),
    ];

    public static ThemeInfo Get(StrataTheme kind) => All.FirstOrDefault(t => t.Kind == kind) ?? All[0];
}
