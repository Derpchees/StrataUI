using Strata.Shell.Services;

namespace Strata.Tests;

/// <summary>Los ajustes siguen a cada monitor aunque Windows le cambie el nombre "\\.\DISPLAYn".</summary>
public sealed class MonitorKeysTests
{
    private const string Laptop = "DISPLAY#BOE0A1C#4&1&UID1", Left = "DISPLAY#GSM5B7F#4&2&UID2", Right = "DISPLAY#GSM5B7F#4&3&UID3";
    private const string D1 = @"\\.\DISPLAY1", D2 = @"\\.\DISPLAY2", D3 = @"\\.\DISPLAY3";

    private static void Configure(StrataSettings s, string device, TaskbarPosition position, int height)
    {
        s.Layouts[device] = new ScreenLayout { Bars = [new BarConfig { Edge = position, Items = [BarItem.Start, BarItem.Apps, BarItem.Clock] }] };
        s.Screens[device] = new ScreenSettings { TaskbarHeight = height };
    }

    private static TaskbarPosition EdgeOf(StrataSettings s, string device) => s.Layouts[device].Bars[0].Edge;

    [Fact]
    public void Laptop_and_external_monitor_sharing_DISPLAY1_keep_their_own_settings()
    {
        var s = new StrataSettings();
        // Laptop sola: barra arriba.
        Configure(s, D1, TaskbarPosition.Top, 41);
        MonitorKeys.Reconcile(s, [(D1, Laptop)]);

        // Con los monitores (tapa cerrada): el principal pasa a ser DISPLAY1. La primera vez hereda
        // lo que había; el usuario lo configura en vertical.
        MonitorKeys.Reconcile(s, [(D1, Right), (D2, Left)]);
        Configure(s, D1, TaskbarPosition.Left, 42);
        Configure(s, D2, TaskbarPosition.Right, 43);

        // Sin monitores otra vez: la laptop vuelve a DISPLAY1 con lo suyo.
        Assert.True(MonitorKeys.Reconcile(s, [(D1, Laptop)]));
        Assert.Equal(TaskbarPosition.Top, EdgeOf(s, D1));
        Assert.Equal(41, s.Screens[D1].TaskbarHeight);

        // Y con los monitores, cada uno recupera lo suyo.
        Assert.True(MonitorKeys.Reconcile(s, [(D1, Right), (D2, Left)]));
        Assert.Equal(TaskbarPosition.Left, EdgeOf(s, D1));
        Assert.Equal(42, s.Screens[D1].TaskbarHeight);
        Assert.Equal(TaskbarPosition.Right, EdgeOf(s, D2));
        Assert.Equal(43, s.Screens[D2].TaskbarHeight);
    }

    [Fact]
    public void Two_monitors_that_swap_names_swap_their_settings()
    {
        var s = new StrataSettings();
        Configure(s, D1, TaskbarPosition.Left, 44);
        Configure(s, D2, TaskbarPosition.Right, 45);
        s.TaskbarMainScreen = D2;
        s.Layouts[D2].Bars.Add(new BarConfig { Edge = TaskbarPosition.Top, Items = [BarItem.Tray] });
        MonitorKeys.Reconcile(s, [(D1, Left), (D2, Right)]);

        Assert.True(MonitorKeys.Reconcile(s, [(D1, Right), (D2, Left)]));
        Assert.Equal(TaskbarPosition.Right, EdgeOf(s, D1));
        Assert.Equal(45, s.Screens[D1].TaskbarHeight);
        Assert.Equal(TaskbarPosition.Left, EdgeOf(s, D2));
        Assert.Equal(D1, s.TaskbarMainScreen);
        Assert.Contains(s.Layouts[D1].Bars, b => b.Edge == TaskbarPosition.Top && b.Items.Contains(BarItem.Tray));
        Assert.DoesNotContain(s.Layouts[D2].Bars, b => b.Items.Contains(BarItem.Tray));
    }

    [Fact]
    public void Same_names_change_nothing_and_new_monitors_keep_what_is_under_their_name()
    {
        var s = new StrataSettings();
        Configure(s, D1, TaskbarPosition.Top, 44);
        Assert.True(MonitorKeys.Reconcile(s, [(D1, Laptop)])); // primera vez: solo anota
        Assert.False(MonitorKeys.Reconcile(s, [(D1, Laptop)]));
        Assert.Equal(44, s.Screens[D1].TaskbarHeight);

        Configure(s, D3, TaskbarPosition.Bottom, 46);
        MonitorKeys.Reconcile(s, [(D1, Laptop), (D3, Left)]);
        Assert.Equal(46, s.Screens[D3].TaskbarHeight);
    }

    [Fact]
    public void Monitors_without_an_id_are_left_alone()
    {
        var s = new StrataSettings();
        Configure(s, D1, TaskbarPosition.Top, 44);
        Assert.False(MonitorKeys.Reconcile(s, [(D1, "")]));
        Assert.Equal(44, s.Screens[D1].TaskbarHeight);
    }

    [Fact]
    public void A_monitor_without_bars_stays_without_bars_when_it_comes_back()
    {
        var s = new StrataSettings();
        Configure(s, D1, TaskbarPosition.Bottom, 44);
        s.Layouts[D2] = new ScreenLayout();
        MonitorKeys.Reconcile(s, [(D1, Right), (D2, Left)]);
        MonitorKeys.Reconcile(s, [(D1, Right)]);
        Assert.Empty(s.ParkedMonitors[Left].Layout!.Bars);
        // Otro monitor toma su nombre mientras no está y lo configura con una barra.
        MonitorKeys.Reconcile(s, [(D1, Right), (D2, Laptop)]);
        Configure(s, D2, TaskbarPosition.Top, 40);
        MonitorKeys.Reconcile(s, [(D1, Right), (D3, Laptop), (D2, Left)]);
        Assert.Empty(s.Layouts[D2].Bars);
        Assert.Equal(TaskbarPosition.Top, EdgeOf(s, D3));
    }
}
