namespace Winx.Shell.Services.Themes;

/// <summary>
/// Material de fondo de las ventanas de WINX.
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
    WinxTheme Kind,
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
        new(WinxTheme.System, "Sistema", "Claro u oscuro según Windows, con Mica.", null, Backdrop.Mica),
        new(WinxTheme.Light, "Claro", "Superficies claras y limpias.", true, Backdrop.Mica),
        new(WinxTheme.Dark, "Oscuro", "Superficies oscuras, ideal de noche.", false, Backdrop.Mica),
        new(WinxTheme.Transparent, "Transparencia", "Vidrio esmerilado que deja ver el fondo.", null, Backdrop.Acrylic, Radius: 6),
        new(WinxTheme.LiquidGlass, "Liquid Glass", "Vidrio claro estilo iOS: cristalino, flotante, con bordes de luz.",
            true, Backdrop.Blur, Glass: true, Floating: true, Radius: 14, PanelRadius: 22),
        new(WinxTheme.LiquidGlassDark, "Liquid Glass oscuro", "Vidrio ahumado estilo iOS: cristalino, flotante, con bordes de luz.",
            false, Backdrop.Blur, Glass: true, Floating: true, Radius: 14, PanelRadius: 22),
        new(WinxTheme.Material, "Material", "Material Design: superficies tonales con tu acento.", null, Backdrop.Solid,
            Tonal: true, Radius: 14, PanelRadius: 16),
    ];

    public static ThemeInfo Get(WinxTheme kind) => All.FirstOrDefault(t => t.Kind == kind) ?? All[0];
}
