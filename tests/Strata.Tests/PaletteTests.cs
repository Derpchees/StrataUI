using System.Windows.Media;
using Strata.Shell.Services.Themes;

namespace Strata.Tests;

/// <summary>Esquemas de color y colores propios de la paleta.</summary>
public sealed class PaletteTests
{
    private static Color Of(System.Windows.ResourceDictionary dict, string key) => ((SolidColorBrush)dict[key]).Color;

    [Fact]
    public void Schemes_use_their_own_colors()
    {
        var nord = ThemeCatalog.Get(Shell.Services.StrataTheme.Nord);
        var dict = Palette.Build(nord, light: false, Colors.Blue);
        Assert.Equal(Palette.Hex("#2E3440"), Of(dict, "StrataTaskbarBackgroundBrush"));
        Assert.Equal(Palette.Hex("#3B4252"), Of(dict, "StrataFlyoutBackgroundBrush"));
        Assert.Equal(Palette.Hex("#ECEFF4"), Of(dict, "StrataForegroundBrush"));
    }

    [Fact]
    public void Every_scheme_has_valid_colors()
    {
        foreach (var theme in ThemeCatalog.All.Where(t => t.Scheme != null))
        {
            var s = theme.Scheme!;
            foreach (var hex in new[] { s.Bar, s.Panel, s.Surface, s.Text, s.SubText, s.Border }.Concat(s.Accent is null ? [] : [s.Accent]))
                Palette.Hex(hex); // lanza si no es un color
            Assert.NotNull(theme.Light);
        }
    }

    [Fact]
    public void Custom_colors_pick_readable_text()
    {
        var dark = ThemeCatalog.Get(Shell.Services.StrataTheme.Dark);
        var dict = Palette.Build(dark, light: false, Colors.Blue, barColor: Palette.Hex("#FFF5F5F5"), panelColor: Palette.Hex("#E6101010"));
        Assert.Equal(Palette.Hex("#FFF5F5F5"), Of(dict, "StrataTaskbarBackgroundBrush"));
        Assert.Equal(Palette.Hex("#1B1B1B"), Of(dict, "StrataBarForegroundBrush")); // barra clara: texto oscuro
        Assert.Equal(Colors.White, Of(dict, "StrataForegroundBrush"));              // paneles oscuros: texto blanco
    }
}
