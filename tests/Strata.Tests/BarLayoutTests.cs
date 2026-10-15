using Strata.Shell;
using Strata.Shell.Services;
using Xunit;

namespace Strata.Tests;

public class BarLayoutTests
{
    private static readonly BarRole Main = new(HasTray: true, IsMainScreen: true);
    private static readonly BarRole Other = new(HasTray: false, IsMainScreen: false);

    [Fact]
    public void Main_bar_keeps_the_usual_items_in_order()
    {
        var items = BarLayout.Defaults(Main, new StrataSettings { ShowBatteryModule = true });
        Assert.Equal(
            [BarItem.Start, BarItem.Media, BarItem.Tray, BarItem.Network, BarItem.Bluetooth, BarItem.Brightness,
             BarItem.Volume, BarItem.Battery, BarItem.Clock],
            items);
    }

    [Fact]
    public void Other_monitor_follows_status_on_all_taskbars()
    {
        Assert.Equal([BarItem.Start], BarLayout.Defaults(Other, new StrataSettings { StatusOnAllTaskbars = false }));
        Assert.Contains(BarItem.Clock, BarLayout.Defaults(Other, new StrataSettings { StatusOnAllTaskbars = true }));
        Assert.DoesNotContain(BarItem.Tray, BarLayout.Defaults(Other, new StrataSettings()));
    }

    [Fact]
    public void Hidden_modules_are_not_in_the_defaults()
    {
        var items = BarLayout.Defaults(Main, new StrataSettings { ShowNetworkModule = false, ShowMediaModule = false });
        Assert.DoesNotContain(BarItem.Network, items);
        Assert.DoesNotContain(BarItem.Media, items);
    }

    [Fact]
    public void Chosen_items_replace_the_defaults_without_repeats()
    {
        var s = new StrataSettings();
        s.BarItems["A|1"] = [BarItem.Cpu, BarItem.Clock, BarItem.Cpu];
        Assert.Equal([BarItem.Cpu, BarItem.Clock], BarLayout.ItemsFor("A|1", Main, s));
    }

    [Fact]
    public void Tray_goes_to_the_first_bar_that_has_it()
    {
        var s = new StrataSettings();
        s.BarItems["A|1"] = [BarItem.Start, BarItem.Clock];
        s.BarItems["B|1"] = [BarItem.Tray, BarItem.Clock];
        Assert.Equal("B|1", BarLayout.TrayBar([("A|1", Main), ("B|1", Other)], s));
    }

    [Fact]
    public void Without_a_chosen_tray_it_stays_where_it_was()
    {
        var s = new StrataSettings();
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
        var s = new StrataSettings();
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
    public void Sensors_task_has_a_stable_name_and_pipe_per_user()
    {
        const string sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
        Assert.Equal(SensorsTask.PipeName(sid), SensorsTask.PipeName(sid));
        Assert.Matches(@"^StrataUI-sensors-[0-9a-f]{32}$", SensorsTask.PipeName(sid));
        Assert.NotEqual(SensorsTask.TaskName(sid), SensorsTask.TaskName("S-1-5-21-1-2-3-1002"));
    }

    [Fact]
    public void Sensors_task_xml_runs_elevated_for_that_user()
    {
        var xml = System.Xml.Linq.XDocument.Parse(SensorsTask.TaskXml("S-1-5-21-1-2-3-1001", @"C:\Program Files\StrataUI & Co\StrataUI.exe"));
        string text = xml.ToString();
        Assert.Contains("<RunLevel>HighestAvailable</RunLevel>", text);
        Assert.Contains("<UserId>S-1-5-21-1-2-3-1001</UserId>", text);
        Assert.Contains("--sensors-task", text);
        Assert.Contains("StrataUI &amp; Co", text);
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
