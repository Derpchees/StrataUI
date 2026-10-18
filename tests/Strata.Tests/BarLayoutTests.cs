using Strata.Shell;
using Strata.Shell.Services;
using Xunit;

namespace Strata.Tests;

public class BarLayoutTests
{
    private static readonly BarRole Main = new(HasTray: true, IsMainScreen: true);
    private static readonly BarRole Other = new(HasTray: false, IsMainScreen: false);

    private const string D1 = @"\\.\DISPLAY1", D2 = @"\\.\DISPLAY2";

    private static StrataSettings With(string device, params BarConfig[] bars)
    {
        var s = new StrataSettings();
        s.Layouts[device] = new ScreenLayout { Bars = [.. bars] };
        return s;
    }

    [Fact]
    public void Main_bar_keeps_the_usual_items_in_order()
    {
        var items = BarLayout.Defaults(Main, new StrataSettings { ShowBatteryModule = true });
        Assert.Equal(
            [BarItem.Start, BarItem.Apps, BarItem.Media, BarItem.Tray, BarItem.Network, BarItem.Bluetooth, BarItem.Brightness,
             BarItem.Volume, BarItem.Battery, BarItem.Clock, BarItem.Notifications],
            items);
    }

    [Fact]
    public void A_new_monitor_gets_one_bar_at_the_bottom()
    {
        var layout = BarLayout.DefaultLayout(isMainScreen: false, new StrataSettings { StatusOnAllTaskbars = false });
        var bar = Assert.Single(layout.Bars);
        Assert.Equal(TaskbarPosition.Bottom, bar.Edge);
        Assert.Equal([BarItem.Start, BarItem.Apps], bar.Items);
    }

    [Fact]
    public void Bars_chosen_by_hand_get_the_bell_next_to_the_clock_once()
    {
        var s = With(D1, new BarConfig { Items = [BarItem.Start, BarItem.Clock, BarItem.Volume] },
                         new BarConfig { Edge = TaskbarPosition.Top, Items = [BarItem.Start] });
        Assert.True(BarLayout.AddNotificationBell(s));
        Assert.Equal([BarItem.Start, BarItem.Clock, BarItem.Notifications, BarItem.Volume], s.Layouts[D1].Bars[0].Items);
        Assert.Equal([BarItem.Start], s.Layouts[D1].Bars[1].Items);

        // Si después se quita, no vuelve.
        s.Layouts[D1].Bars[0].Items.Remove(BarItem.Notifications);
        Assert.False(BarLayout.AddNotificationBell(s));
        Assert.DoesNotContain(BarItem.Notifications, s.Layouts[D1].Bars[0].Items);
    }

    [Fact]
    public void Other_monitor_follows_status_on_all_taskbars()
    {
        Assert.Equal([BarItem.Start, BarItem.Apps], BarLayout.Defaults(Other, new StrataSettings { StatusOnAllTaskbars = false }));
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
    public void Items_are_read_by_monitor_and_edge_without_repeats()
    {
        var s = With(D1, new BarConfig { Edge = TaskbarPosition.Left, Items = [BarItem.Cpu, BarItem.Clock, BarItem.Cpu] });
        Assert.Equal([BarItem.Cpu, BarItem.Clock], BarLayout.ItemsFor(BarLayout.Id(D1, TaskbarPosition.Left), s));
        Assert.Empty(BarLayout.ItemsFor(BarLayout.Id(D1, TaskbarPosition.Right), s));
    }

    [Fact]
    public void Bar_ids_name_the_monitor_and_the_edge()
    {
        string id = BarLayout.Id(D2, TaskbarPosition.Right);
        Assert.Equal(@"\\.\DISPLAY2|Right", id);
        Assert.Equal(D2, BarLayout.DeviceOf(id));
        Assert.Equal(TaskbarPosition.Right, BarLayout.EdgeOf(id));
    }

    [Fact]
    public void Each_monitor_carries_each_item_once_and_one_bar_per_edge()
    {
        var layout = new ScreenLayout
        {
            Bars =
            [
                new BarConfig { Edge = TaskbarPosition.Left, Items = [BarItem.Volume, BarItem.Clock], Zones = { [BarItem.Clock] = BarZone.Start } },
                new BarConfig { Edge = TaskbarPosition.Bottom, Items = [BarItem.Clock, BarItem.Start] },
                new BarConfig { Edge = TaskbarPosition.Bottom, Items = [BarItem.Tray] },
            ],
        };
        BarLayout.Normalize(layout);
        // La de abajo se crea antes (se queda el reloj); la segunda de abajo sobra.
        Assert.Equal([TaskbarPosition.Bottom, TaskbarPosition.Left], layout.Bars.Select(b => b.Edge));
        Assert.Equal([BarItem.Clock, BarItem.Start], layout.Bars[0].Items);
        Assert.Equal([BarItem.Volume], layout.Bars[1].Items);
        Assert.Empty(layout.Bars[1].Zones);
    }

    [Fact]
    public void Horizontal_bars_are_created_first_to_keep_the_corners()
    {
        var layout = new ScreenLayout
        {
            Bars = [new BarConfig { Edge = TaskbarPosition.Right }, new BarConfig { Edge = TaskbarPosition.Top }, new BarConfig { Edge = TaskbarPosition.Left }, new BarConfig { Edge = TaskbarPosition.Bottom }],
        };
        Assert.Equal([TaskbarPosition.Bottom, TaskbarPosition.Top, TaskbarPosition.Left, TaskbarPosition.Right],
                     BarLayout.InCreationOrder(layout).Select(b => b.Edge));
    }

    [Fact]
    public void Tray_apps_and_edges_change_the_structure_key_but_other_items_do_not()
    {
        var s = With(D1, new BarConfig { Items = [BarItem.Start, BarItem.Clock] });
        string before = BarLayout.StructureKey(s);
        s.Layouts[D1].Bars[0].Items.Add(BarItem.Volume);
        Assert.Equal(before, BarLayout.StructureKey(s));
        s.Layouts[D1].Bars[0].Items.Add(BarItem.Tray);
        Assert.NotEqual(before, BarLayout.StructureKey(s));
        string withTray = BarLayout.StructureKey(s);
        s.Layouts[D1].Bars.Add(new BarConfig { Edge = TaskbarPosition.Top });
        Assert.NotEqual(withTray, BarLayout.StructureKey(s));
    }

    [Fact]
    public void New_items_go_to_their_usual_place()
    {
        var items = new List<BarItem> { BarItem.Volume, BarItem.Clock };
        BarLayout.Insert(items, BarItem.Start);
        BarLayout.Insert(items, BarItem.Apps);
        BarLayout.Insert(items, BarItem.Network);
        Assert.Equal([BarItem.Start, BarItem.Apps, BarItem.Volume, BarItem.Network, BarItem.Clock], items);
    }

    [Fact]
    public void Start_and_apps_have_a_zone_like_the_rest()
    {
        var bar = new BarConfig { Items = [BarItem.Start, BarItem.Apps, BarItem.Clock] };
        BarLayout.SetZone(bar, BarItem.Start, BarZone.Center);
        BarLayout.SetZone(bar, BarItem.Apps, BarZone.Start);
        Assert.Equal(BarZone.Center, BarLayout.ZoneOf(bar, BarItem.Start));
        Assert.Equal(BarZone.Start, BarLayout.ZoneOf(bar, BarItem.Apps));
        Assert.Equal(BarZone.End, BarLayout.ZoneOf(bar, BarItem.Clock));
    }

    [Theory]
    // Alineación de antes → zona de Inicio y de las apps.
    [InlineData(TaskbarAlignment.Left, false, BarZone.Start, BarZone.Start)]
    [InlineData(TaskbarAlignment.Center, false, BarZone.Center, BarZone.Center)]
    [InlineData(TaskbarAlignment.Left, true, BarZone.Start, BarZone.Center)]
    public void Old_alignment_becomes_zones(TaskbarAlignment alignment, bool centerApps, BarZone start, BarZone apps)
    {
        var s = With(D1, new BarConfig { Items = [BarItem.Start, BarItem.Apps, BarItem.Clock] });
        s.Screens[D1] = new ScreenSettings { TaskbarAlignment = alignment, TaskbarCenterApps = centerApps };
        Assert.True(BarLayout.ZoneStartAndApps(s));
        var bar = s.Layouts[D1].Bars[0];
        Assert.Equal(start, BarLayout.ZoneOf(bar, BarItem.Start));
        Assert.Equal(apps, BarLayout.ZoneOf(bar, BarItem.Apps));
        Assert.True(bar.Items.IndexOf(BarItem.Start) < bar.Items.IndexOf(BarItem.Apps));
        Assert.False(BarLayout.ZoneStartAndApps(s)); // una sola vez
    }

    [Fact]
    public void Free_ram_becomes_the_ram_meter_once()
    {
        var bar = new BarConfig { Items = [BarItem.Clock, BarItem.FreeRam] };
        BarLayout.SetZone(bar, BarItem.FreeRam, BarZone.Start);
        var s = With(D1, bar);
        Assert.True(BarLayout.MergeFreeRam(s));
        Assert.Equal([BarItem.Clock, BarItem.Ram], bar.Items);
        Assert.Equal(BarZone.Start, BarLayout.ZoneOf(bar, BarItem.Ram));
        Assert.False(BarLayout.MergeFreeRam(s));

        // Con el medidor ya puesto, el botón solo se quita.
        var both = new BarConfig { Items = [BarItem.Ram, BarItem.FreeRam] };
        Assert.True(BarLayout.MergeFreeRam(With(D1, both)));
        Assert.Equal([BarItem.Ram], both.Items);
    }

    [Fact]
    public void Start_and_apps_stay_after_what_was_already_at_the_start()
    {
        // Antes la zona de inicio iba delante de Inicio y las apps.
        var bar = new BarConfig { Items = [BarItem.Start, BarItem.Apps, BarItem.Media, BarItem.Clock] };
        BarLayout.SetZone(bar, BarItem.Media, BarZone.Start);
        BarLayout.ZoneLikeAlignment(bar, new ScreenSettings { TaskbarAlignment = TaskbarAlignment.Left });
        Assert.Equal([BarItem.Media, BarItem.Start, BarItem.Apps, BarItem.Clock], bar.Items);
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
