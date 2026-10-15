using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Material de fondo de las ventanas de Strata UI.
/// Blur = desenfoque limpio sin ruido ni capa de luminosidad (vidrio estilo iOS/HyperOS).
/// </summary>
public enum Backdrop { Solid, Mica, Acrylic, Blur }

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
    double PanelRadius = 8);

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
    ];

    public static ThemeInfo Get(StrataTheme kind) => All.FirstOrDefault(t => t.Kind == kind) ?? All[0];
}
