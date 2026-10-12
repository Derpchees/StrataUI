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
}
