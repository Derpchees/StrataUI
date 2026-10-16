using Strata.Shell.Services;

namespace Strata.Tests;

/// <summary>Los ajustes siguen a cada monitor aunque Windows le cambie el nombre "\\.\DISPLAYn".</summary>
public sealed class MonitorKeysTests
{
    private const string Laptop = "DISPLAY#BOE0A1C#4&1&UID1", Left = "DISPLAY#GSM5B7F#4&2&UID2", Right = "DISPLAY#GSM5B7F#4&3&UID3";
    private const string D1 = @"\\.\DISPLAY1", D2 = @"\\.\DISPLAY2", D3 = @"\\.\DISPLAY3";

    private static void Configure(StrataSettings s, string device, TaskbarPosition position, int height)
    {
        s.TaskbarPositions[device] = position;
        s.Screens[device] = new ScreenSettings { TaskbarHeight = height };
        s.BarItems[device + "|1"] = [BarItem.Start, BarItem.Clock];
    }

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
        Assert.Equal(TaskbarPosition.Top, s.PositionOf(D1));
        Assert.Equal(41, s.Screens[D1].TaskbarHeight);

        // Y con los monitores, cada uno recupera lo suyo.
        Assert.True(MonitorKeys.Reconcile(s, [(D1, Right), (D2, Left)]));
        Assert.Equal(TaskbarPosition.Left, s.PositionOf(D1));
        Assert.Equal(42, s.Screens[D1].TaskbarHeight);
        Assert.Equal(TaskbarPosition.Right, s.PositionOf(D2));
        Assert.Equal(43, s.Screens[D2].TaskbarHeight);
    }

    [Fact]
    public void Two_monitors_that_swap_names_swap_their_settings()
    {
        var s = new StrataSettings();
        Configure(s, D1, TaskbarPosition.Left, 44);
        Configure(s, D2, TaskbarPosition.Right, 45);
        s.TaskbarMainScreen = D2;
        s.BarItems[D2 + "|2"] = [BarItem.Tray];
        MonitorKeys.Reconcile(s, [(D1, Left), (D2, Right)]);

        Assert.True(MonitorKeys.Reconcile(s, [(D1, Right), (D2, Left)]));
        Assert.Equal(TaskbarPosition.Right, s.PositionOf(D1));
        Assert.Equal(45, s.Screens[D1].TaskbarHeight);
        Assert.Equal(TaskbarPosition.Left, s.PositionOf(D2));
        Assert.Equal(D1, s.TaskbarMainScreen);
        Assert.Equal([BarItem.Tray], s.BarItems[D1 + "|2"]);
        Assert.False(s.BarItems.ContainsKey(D2 + "|2"));
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
}
