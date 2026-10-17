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
/// <see cref="Glass"/> agrega bordes de luz especulares (temas importados); <see cref="Floating"/>
/// separa la barra de los bordes en forma de cápsula; <see cref="Tonal"/> tiñe las superficies
/// con el acento (Material Design). <see cref="PanelRadius"/> es el redondeo de los paneles.
/// <see cref="ModuleId"/>: tema importado (Services/Modules). <see cref="Mode"/>: esquema que solo
/// fija claro u oscuro (Claro, Oscuro) y deja los colores del estilo.
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
    ColorScheme? Scheme = null,
    string? ModuleId = null,
    bool ClearBar = false,
    StrataTheme? SchemeKind = null,
    bool Mode = false)
{
    /// <summary>
    /// Identifica el tema (el de la lista o uno importado, con su esquema de color si lo tiñe uno)
    /// para saber si cambió.
    /// </summary>
    public string Key => (ModuleId is null ? Kind.ToString() : "module:" + ModuleId) + (SchemeKind is { } k ? "+" + k : "");

    /// <summary>Es un esquema (Claro, Oscuro, Nord, Dracula…), que se pone sobre un estilo, y no un estilo.</summary>
    public bool IsScheme => (Scheme != null || Mode) && SchemeKind is null && ModuleId is null;
}

public static class ThemeCatalog
{
    /// <summary>Separación de la barra flotante respecto a los bordes de la pantalla.</summary>
    public const double FloatingMargin = 10;

    /// <summary>
    /// Estilos (el material y las formas; claro u oscuro según Windows o el esquema) y esquemas (los
    /// colores o solo claro/oscuro), que se combinan (<see cref="Combine"/>).
    /// </summary>
    public static IReadOnlyList<ThemeInfo> All { get; } =
    [
        // ── Estilos ────────────────────────────────────────────────────────────
        new(StrataTheme.System, "Mica", Loc.T("El material de Windows 11: sólido y sobrio, con un toque del fondo."), null, Backdrop.Mica),
        new(StrataTheme.Transparent, Loc.T("Translúcido"), Loc.T("Vidrio esmerilado que deja ver el fondo."), null, Backdrop.Acrylic, Radius: 6),
        new(StrataTheme.Material, "Material", Loc.T("Material Design: superficies tonales con tu acento."), null, Backdrop.Solid,
            Tonal: true, Radius: 14, PanelRadius: 16),
        // Solo la barra va sin fondo (Taskbar.Transparency); los paneles, el menú Inicio y los menús
        // llevan el vidrio esmerilado de Translúcido, que se lee sobre cualquier fondo.
        new(StrataTheme.Clear, Loc.T("Transparente total"),
            Loc.T("Barra sin fondo: solo íconos y texto, claros u oscuros según lo que tengan detrás. Los paneles y menús llevan vidrio esmerilado para que se lean."),
            null, Backdrop.Acrylic, Radius: 6, ClearBar: true),

        // ── Esquemas: claro u oscuro con los colores del estilo ────────────────
        new(StrataTheme.Light, Loc.T("Claro"), Loc.T("Siempre claro, con los colores del estilo."), true, Backdrop.Mica, Mode: true),
        new(StrataTheme.Dark, Loc.T("Oscuro"), Loc.T("Siempre oscuro, con los colores del estilo."), false, Backdrop.Mica, Mode: true),

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

    /// <summary>
    /// El estilo con los colores de un esquema encima (null: el estilo tal cual). El esquema pone
    /// colores y claro/oscuro; el estilo, el material (Mica, vidrio, transparencia…) y las formas.
    /// </summary>
    public static ThemeInfo Combine(ThemeInfo style, StrataTheme? scheme) =>
        scheme is { } kind && style.Scheme is null && Get(kind) is { IsScheme: true } colors
            ? colors.Mode
                ? style with { Light = colors.Light, SchemeKind = kind }
                : style with { Scheme = colors.Scheme, Light = colors.Light, SchemeKind = kind }
            : style;
}
