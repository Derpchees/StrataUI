using Strata.Shell.Services;

namespace Strata.Tests;

/// <summary>Los ajustes de diseño de antes de la 2.6 pasan a un diseño por monitor que se ve igual.</summary>
public sealed class LegacyLayoutsTests
{
    private const string D1 = @"\\.\DISPLAY1", D2 = @"\\.\DISPLAY2";

    private static BarConfig Bar(StrataSettings s, string device, TaskbarPosition edge) =>
        s.Layouts[device].Bars.Single(b => b.Edge == edge);

    [Fact]
    public void Second_bar_with_the_status_area_keeps_its_items_zones_and_the_tray()
    {
        // Lo que tenía el usuario: dos monitores con la barra abajo y, en el principal, una segunda
        // barra arriba con el área de estado (y la bandeja).
        var s = new StrataSettings
        {
            NotificationBellAdded = true,
            TaskbarMainScreen = D2,
            TaskbarPositions = new() { [D1] = TaskbarPosition.Bottom, [D2] = TaskbarPosition.Bottom },
            SecondTaskbars = new() { [D2] = SecondBarContent.Status },
            BarItems = new()
            {
                [D2 + "|2"] = [BarItem.Ram, BarItem.Cpu, BarItem.Media, BarItem.Tray, BarItem.Network, BarItem.Volume],
                [D2 + "|1"] = [BarItem.Start, BarItem.Clock],
                [D1 + "|1"] = [BarItem.Start],
            },
            BarItemZones = new() { [D2 + "|2"] = new() { [BarItem.Media] = BarZone.Center, [BarItem.Tray] = BarZone.Start } },
        };
        LegacyLayouts.Convert(s, D2, present: []);

        Assert.Equal([TaskbarPosition.Bottom, TaskbarPosition.Top], BarLayout.InCreationOrder(s.Layouts[D2]).Select(b => b.Edge));
        var top = Bar(s, D2, TaskbarPosition.Top);
        Assert.Equal([BarItem.Ram, BarItem.Cpu, BarItem.Media, BarItem.Tray, BarItem.Network, BarItem.Volume], top.Items);
        Assert.Equal(BarZone.Center, BarLayout.ZoneOf(top, BarItem.Media));
        Assert.Equal(BarZone.Start, BarLayout.ZoneOf(top, BarItem.Tray));
        // La barra de abajo era la de las ventanas: ahora lleva las apps abiertas como elemento.
        Assert.Equal([BarItem.Start, BarItem.Apps, BarItem.Clock], Bar(s, D2, TaskbarPosition.Bottom).Items);
        Assert.Equal([BarItem.Start, BarItem.Apps], Bar(s, D1, TaskbarPosition.Bottom).Items);

        // Los ajustes de antes ya no se guardan.
        Assert.Null(s.TaskbarPositions);
        Assert.Null(s.SecondTaskbars);
        Assert.Null(s.BarItems);
        Assert.Null(s.BarItemZones);
        Assert.DoesNotContain("TaskbarPositions", Settings.Serialize(s));
    }

    [Fact]
    public void Hidden_monitors_get_no_bars_but_the_main_one_always_had_one()
    {
        var s = new StrataSettings { TaskbarHiddenScreens = [D1, D2] };
        LegacyLayouts.Convert(s, D2, present: []);
        Assert.Empty(s.Layouts[D1].Bars);
        var main = Assert.Single(s.Layouts[D2].Bars);
        Assert.Contains(BarItem.Tray, main.Items);
    }

    [Fact]
    public void The_tray_stays_only_where_it_was_shown()
    {
        // La bandeja movida desde el Hub a la barra de otro monitor (el Hub escribía las listas de
        // todas las barras): la principal ya no la lleva.
        var s = new StrataSettings
        {
            NotificationBellAdded = true,
            BarItems = new()
            {
                [D1 + "|1"] = [BarItem.Start, BarItem.Tray, BarItem.Clock],
                [D2 + "|1"] = [BarItem.Start, BarItem.Clock],
            },
        };
        LegacyLayouts.Convert(s, D2, present: [D1, D2]);
        Assert.Contains(BarItem.Tray, Bar(s, D1, TaskbarPosition.Bottom).Items);
        Assert.DoesNotContain(BarItem.Tray, Bar(s, D2, TaskbarPosition.Bottom).Items);
    }

    [Fact]
    public void Without_old_settings_nothing_is_written()
    {
        var s = new StrataSettings();
        LegacyLayouts.Convert(s, D1, present: [D1]);
        Assert.Empty(s.Layouts);
    }

    [Fact]
    public void Parked_monitors_are_converted_too()
    {
        var s = new StrataSettings();
        s.ParkedMonitors["DISPLAY#X"] = new MonitorSnapshot
        {
            Position = TaskbarPosition.Left,
            Items = new() { ["1"] = [BarItem.Start, BarItem.Volume] },
        };
        LegacyLayouts.Convert(s, D1, present: []);
        var snap = s.ParkedMonitors["DISPLAY#X"];
        var bar = Assert.Single(snap.Layout!.Bars);
        Assert.Equal(TaskbarPosition.Left, bar.Edge);
        Assert.Equal([BarItem.Start, BarItem.Apps, BarItem.Volume], bar.Items);
        Assert.Null(snap.Position);
        Assert.Null(snap.Items);
    }

    [Fact]
    public void Old_settings_files_are_read_and_converted()
    {
        const string json = """
            {
              "TaskbarMainScreen": "\\\\.\\DISPLAY1",
              "TaskbarPositions": { "\\\\.\\DISPLAY1": "Left" },
              "BarItems": { "\\\\.\\DISPLAY1|1": [ "Start", "Clock" ] },
              "NotificationBellAdded": true,
              "Screens": { "\\\\.\\DISPLAY1": { "TaskbarAlignment": "Left" } }
            }
            """;
        // (Alineación fija: "Igual que Windows" depende de la barra de Windows del equipo que corre la prueba.)
        var s = Settings.Deserialize(json);
        var bar = s.Layouts[D1].Bars.Single();
        Assert.Equal(TaskbarPosition.Left, bar.Edge);
        // (La bandeja depende de qué monitor es el principal en el equipo que corre la prueba.)
        Assert.Equal([BarItem.Start, BarItem.Apps], bar.Items.Take(2));
        Assert.Equal(BarItem.Clock, bar.Items[^1]);
    }
}
