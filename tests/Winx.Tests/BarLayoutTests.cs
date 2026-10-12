using Winx.Shell;
using Winx.Shell.Services;
using Xunit;

namespace Winx.Tests;

public class BarLayoutTests
{
    private static readonly BarRole Main = new(HasTray: true, IsMainScreen: true);
    private static readonly BarRole Other = new(HasTray: false, IsMainScreen: false);

    [Fact]
    public void Main_bar_keeps_the_usual_items_in_order()
    {
        var items = BarLayout.Defaults(Main, new WinxSettings { ShowBatteryModule = true });
        Assert.Equal(
            [BarItem.Start, BarItem.Media, BarItem.Tray, BarItem.Network, BarItem.Bluetooth, BarItem.Brightness,
             BarItem.Volume, BarItem.Battery, BarItem.Clock],
            items);
    }

    [Fact]
    public void Other_monitor_follows_status_on_all_taskbars()
    {
        Assert.Equal([BarItem.Start], BarLayout.Defaults(Other, new WinxSettings { StatusOnAllTaskbars = false }));
        Assert.Contains(BarItem.Clock, BarLayout.Defaults(Other, new WinxSettings { StatusOnAllTaskbars = true }));
        Assert.DoesNotContain(BarItem.Tray, BarLayout.Defaults(Other, new WinxSettings()));
    }

    [Fact]
    public void Hidden_modules_are_not_in_the_defaults()
    {
        var items = BarLayout.Defaults(Main, new WinxSettings { ShowNetworkModule = false, ShowMediaModule = false });
        Assert.DoesNotContain(BarItem.Network, items);
        Assert.DoesNotContain(BarItem.Media, items);
    }

    [Fact]
    public void Chosen_items_replace_the_defaults_without_repeats()
    {
        var s = new WinxSettings();
        s.BarItems["A|1"] = [BarItem.Cpu, BarItem.Clock, BarItem.Cpu];
        Assert.Equal([BarItem.Cpu, BarItem.Clock], BarLayout.ItemsFor("A|1", Main, s));
    }

    [Fact]
    public void Tray_goes_to_the_first_bar_that_has_it()
    {
        var s = new WinxSettings();
        s.BarItems["A|1"] = [BarItem.Start, BarItem.Clock];
        s.BarItems["B|1"] = [BarItem.Tray, BarItem.Clock];
        Assert.Equal("B|1", BarLayout.TrayBar([("A|1", Main), ("B|1", Other)], s));
    }

    [Fact]
    public void Without_a_chosen_tray_it_stays_where_it_was()
    {
        var s = new WinxSettings();
        s.BarItems["A|1"] = [BarItem.Start];
        Assert.Equal("A|1", BarLayout.TrayBar([("A|1", Main), ("B|1", Other)], s));
    }

    [Fact]
    public void Bar_ids_name_the_monitor_and_which_bar()
    {
        string id = BarLayout.Id(@"\\.\DISPLAY2", second: true);
        Assert.Equal(@"\\.\DISPLAY2|2", id);
        Assert.Equal(@"\\.\DISPLAY2", BarLayout.DeviceOf(id));
        Assert.True(BarLayout.IsSecond(id));
    }

    [Fact]
    public void Moving_the_tray_changes_the_layout_key()
    {
        var s = new WinxSettings();
        string before = BarLayout.TrayKey(s);
        s.BarItems["B|1"] = [BarItem.Tray];
        Assert.NotEqual(before, BarLayout.TrayKey(s));
    }

    [Fact]
    public void Gpu_engines_are_grouped_without_the_process()
    {
        Assert.Equal("luid_0x00000000_0x000122fd_phys_0_eng_0_engtype_3D",
                     SystemMonitor.EngineOf("pid_4340_luid_0x00000000_0x000122fd_phys_0_eng_0_engtype_3D"));
    }

    [Fact]
    public void Sensor_lines_are_read()
    {
        Sensors.Apply("cpu=54.5;gpu=;pawnio=0;fps=1234:144,99:60");
        Assert.Equal(54.5, Sensors.CpuTemperature);
        Assert.Null(Sensors.GpuTemperature);
        Assert.True(Sensors.MissingPawnIo);
    }
}
