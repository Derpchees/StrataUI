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
        var glass = ThemeCatalog.Get(StrataTheme.LiquidGlass);

        var tinted = ThemeCatalog.Combine(glass, StrataTheme.Dracula);

        Assert.True(tinted.Glass);
        Assert.Equal(glass.Backdrop, tinted.Backdrop);
        Assert.Equal(ThemeCatalog.Get(StrataTheme.Dracula).Scheme, tinted.Scheme);
        Assert.False(tinted.Light);
        Assert.NotEqual(glass.Key, tinted.Key);
        Assert.Same(glass, ThemeCatalog.Combine(glass, null));
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
