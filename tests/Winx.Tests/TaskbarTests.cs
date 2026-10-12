using System.Globalization;
using Winx.Shell;
using Winx.Shell.Services;

namespace Winx.Tests;

public sealed class TaskbarTests
{
    [Theory]
    [InlineData("es-MX", "dd/MM")]
    [InlineData("en-US", "M/d")]
    [InlineData("de-DE", "dd.MM")]
    [InlineData("ja-JP", "MM/dd")]
    [InlineData("sv-SE", "MM-dd")]
    public void Vertical_bar_date_drops_the_year(string culture, string expected) =>
        Assert.Equal(expected, Taskbar.ShortDateWithoutYear(CultureInfo.GetCultureInfo(culture)));

    [Theory]
    [InlineData(0, TaskbarAlignment.Left)]
    [InlineData(1, TaskbarAlignment.Center)]
    [InlineData(null, TaskbarAlignment.Center)]   // Windows 11 sin valor guardado: centro
    public void Windows_alignment_is_read_from_TaskbarAl(int? value, TaskbarAlignment expected) =>
        Assert.Equal(expected, WindowsTaskbarPrefs.AlignmentFrom(value));

    [Theory]
    [InlineData(null, TaskbarAppsMode.AllTaskbars)]
    [InlineData(0, TaskbarAppsMode.AllTaskbars)]
    [InlineData(1, TaskbarAppsMode.MainAndOwn)]
    [InlineData(2, TaskbarAppsMode.OwnOnly)]
    public void Windows_apps_mode_is_read_from_MMTaskbarMode(int? value, TaskbarAppsMode expected) =>
        Assert.Equal(expected, WindowsTaskbarPrefs.AppsModeFrom(value));

    [Fact]
    public void Main_screen_is_the_chosen_one_while_connected()
    {
        Settings.SuppressSave = true;
        var screens = new[] { (@"\\.\DISPLAY1", true), (@"\\.\DISPLAY2", false) };
        Settings.Current.TaskbarMainScreen = null;
        Assert.Equal(@"\\.\DISPLAY1", App.MainScreenDevice(screens));
        Settings.Current.TaskbarMainScreen = @"\\.\DISPLAY2";
        Assert.Equal(@"\\.\DISPLAY2", App.MainScreenDevice(screens));
        Settings.Current.TaskbarMainScreen = @"\\.\DISPLAY3";   // desconectado: vuelve al de Windows
        Assert.Equal(@"\\.\DISPLAY1", App.MainScreenDevice(screens));
        Settings.Current.TaskbarMainScreen = null;
    }

    [Fact]
    public void One_bar_per_monitor_by_default()
    {
        var bars = App.BarsFor(TaskbarPosition.Bottom, SecondBarContent.None, isMain: true);
        var (edge, role) = Assert.Single(bars);
        Assert.Equal(TaskbarPosition.Bottom, edge);
        Assert.Equal(new BarRole(HasTray: true, IsMainScreen: true), role);
    }

    [Fact]
    public void Second_bar_with_apps_leaves_the_status_area_and_tray_on_the_first()
    {
        var bars = App.BarsFor(TaskbarPosition.Left, SecondBarContent.Apps, isMain: true);
        Assert.Equal([TaskbarPosition.Left, TaskbarPosition.Right], bars.Select(b => b.Edge));
        Assert.Equal(new BarRole(true, true, ShowsApps: false, ShowsStatus: true), bars[0].Role);
        Assert.Equal(new BarRole(false, true, ShowsApps: true, ShowsStatus: false), bars[1].Role);
    }

    [Fact]
    public void Second_bar_with_the_status_area_takes_the_tray_and_is_created_first()
    {
        var bars = App.BarsFor(TaskbarPosition.Bottom, SecondBarContent.Status, isMain: true);
        Assert.Equal([TaskbarPosition.Top, TaskbarPosition.Bottom], bars.Select(b => b.Edge));
        Assert.True(bars[0].Role is { HasTray: true, ShowsStatus: true, ShowsApps: false });
        Assert.True(bars[1].Role is { HasTray: false, ShowsStatus: false, ShowsApps: true });
    }

    [Fact]
    public void Full_second_bar_repeats_everything_but_the_tray()
    {
        var bars = App.BarsFor(TaskbarPosition.Top, SecondBarContent.Full, isMain: true);
        Assert.All(bars, b => Assert.True(b.Role is { ShowsApps: true, ShowsStatus: true }));
        Assert.Single(bars, b => b.Role.HasTray);
        // En otro monitor ninguna lleva la bandeja.
        Assert.DoesNotContain(App.BarsFor(TaskbarPosition.Top, SecondBarContent.Full, isMain: false), b => b.Role.HasTray);
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("308046B0AF4A39CB", "Firefox")]
    [InlineData("Deezer.62021768415AF_q7m17pa7q8kj0!Deezer.Music", "Deezer")]
    public void Media_app_names_are_readable(string id, string expected) =>
        Assert.Equal(expected, Winx.Shell.Services.Media.MediaService.CleanName(id));
}
