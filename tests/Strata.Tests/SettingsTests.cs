using Strata.Shell.Services;
using Strata.Shell.Services.Themes;
using Xunit;

namespace Strata.Tests;

public class SettingsTests
{
    [Fact]
    public void Fully_transparent_switch_becomes_the_clear_theme()
    {
        var s = new StrataSettings();
        s.Screens["\\\\.\\DISPLAY1"] = new ScreenSettings { Theme = StrataTheme.Dark, TaskbarFullyTransparent = true };

        Settings.Migrate(s);

        Assert.Equal(StrataTheme.Clear, s.Theme);
        Assert.False(s.Screens["\\\\.\\DISPLAY1"].TaskbarFullyTransparent);
    }

    [Fact]
    public void The_theme_is_one_for_every_monitor()
    {
        var s = new StrataSettings();
        s.Screens["\\\\.\\DISPLAY1"] = new ScreenSettings { Theme = StrataTheme.Material };
        s.Screens["\\\\.\\DISPLAY2"] = new ScreenSettings { Theme = StrataTheme.Clear };

        Settings.Migrate(s);

        Assert.Equal(StrataTheme.Material, s.Theme);
        Assert.All(s.Screens.Values, screen => Assert.Equal(StrataTheme.Material, screen.Theme));
    }

    [Fact]
    public void A_color_scheme_theme_becomes_a_scheme_over_the_system_style()
    {
        var s = new StrataSettings { Theme = StrataTheme.Nord };

        Settings.Migrate(s);

        Assert.Equal(StrataTheme.System, s.Theme);
        Assert.Equal(StrataTheme.Nord, s.ColorScheme);
    }

    [Fact]
    public void A_scheme_tints_the_style_but_keeps_its_material()
    {
        var translucent = ThemeCatalog.Get(StrataTheme.Transparent);

        var tinted = ThemeCatalog.Combine(translucent, StrataTheme.Dracula);

        Assert.Equal(translucent.Backdrop, tinted.Backdrop);
        Assert.Equal(ThemeCatalog.Get(StrataTheme.Dracula).Scheme, tinted.Scheme);
        Assert.False(tinted.Light);
        Assert.NotEqual(translucent.Key, tinted.Key);
        Assert.Same(translucent, ThemeCatalog.Combine(translucent, null));
    }

    [Fact]
    public void Light_and_dark_are_schemes_that_keep_the_style_colors()
    {
        var material = ThemeCatalog.Get(StrataTheme.Material);

        var dark = ThemeCatalog.Combine(material, StrataTheme.Dark);

        Assert.False(dark.Light);
        Assert.Null(dark.Scheme);                 // los colores tonales de Material, no los de un esquema
        Assert.True(dark.Tonal);
        Assert.NotEqual(material.Key, dark.Key);
        Assert.True(ThemeCatalog.Get(StrataTheme.Light).IsScheme);
        Assert.DoesNotContain(ThemeCatalog.All, t => t.Kind is StrataTheme.LiquidGlass or StrataTheme.LiquidGlassDark);
    }

    [Theory]
    [InlineData(StrataTheme.Light, StrataTheme.System, StrataTheme.Light)]
    [InlineData(StrataTheme.Dark, StrataTheme.System, StrataTheme.Dark)]
    [InlineData(StrataTheme.LiquidGlass, StrataTheme.Transparent, StrataTheme.Light)]
    [InlineData(StrataTheme.LiquidGlassDark, StrataTheme.Transparent, StrataTheme.Dark)]
    public void Old_light_dark_and_liquid_glass_themes_become_a_style_and_a_scheme(StrataTheme old, StrataTheme style, StrataTheme scheme)
    {
        var s = new StrataSettings { Theme = old };

        Settings.Migrate(s);

        Assert.Equal(style, s.Theme);
        Assert.Equal(scheme, s.ColorScheme);
    }

    [Fact]
    public void A_color_scheme_already_chosen_wins_over_the_old_dark_theme()
    {
        var s = new StrataSettings { Theme = StrataTheme.LiquidGlassDark, ColorScheme = StrataTheme.Nord };

        Settings.Migrate(s);

        Assert.Equal(StrataTheme.Transparent, s.Theme);
        Assert.Equal(StrataTheme.Nord, s.ColorScheme);
    }

    [Fact]
    public void The_bar_color_becomes_the_style_of_each_bar()
    {
        var s = new StrataSettings { CustomBarColor = "#CC102030" };
        s.Screens["\\\\.\\DISPLAY1"] = new ScreenSettings();
        s.Screens["\\\\.\\DISPLAY2"] = new ScreenSettings { BarStyle = BarStyle.Clear };

        Settings.Migrate(s);

        Assert.Null(s.CustomBarColor);
        Assert.Equal(BarStyle.Color, s.Screens["\\\\.\\DISPLAY1"].BarStyle);
        Assert.Equal("#CC102030", s.Screens["\\\\.\\DISPLAY1"].BarColor);
        Assert.Equal(BarStyle.Clear, s.Screens["\\\\.\\DISPLAY2"].BarStyle);
    }

    [Fact]
    public void Settings_survive_a_serialize_round_trip()
    {
        var s = new StrataSettings { Theme = StrataTheme.Clear, NotificationSounds = false };

        var copy = Settings.Deserialize(Settings.Serialize(s));

        Assert.Equal(StrataTheme.Clear, copy.Theme);
        Assert.False(copy.NotificationSounds);
    }
}
