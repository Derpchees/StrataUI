namespace Strata.Shell.Services;

/// <summary>
/// Hasta la 2.5 el diseño de las barras eran ajustes sueltos: posición de la barra de cada monitor,
/// una segunda barra en el borde opuesto que se repartía el contenido, monitores sin barra, los
/// elementos de cada barra ("\\.\DISPLAY1|1" y "|2") y sus zonas. Aquí se pasan a un diseño por
/// monitor (<see cref="StrataSettings.Layouts"/>) que se ve igual que antes, una sola vez.
/// </summary>
public static class LegacyLayouts
{
    /// <summary>Monitor principal de Strata UI al cargar la configuración (el elegido si está conectado, si no el de Windows).</summary>
    internal static string? CurrentMainDevice(StrataSettings s)
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (s.TaskbarMainScreen is { } chosen && screens.Any(sc => sc.DeviceName == chosen))
                return chosen;
            return screens.FirstOrDefault(sc => sc.Primary)?.DeviceName ?? s.TaskbarMainScreen;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return s.TaskbarMainScreen;
        }
    }

    private static IEnumerable<string> PresentDevices()
    {
        try
        {
            return System.Windows.Forms.Screen.AllScreens.Select(sc => sc.DeviceName).ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Pasa los ajustes de antes a diseños por monitor y los borra. <paramref name="present"/>: los
    /// monitores conectados (también reciben su diseño de antes aunque no tuvieran nada elegido,
    /// para que la bandeja quede donde estaba); null = los de este equipo.
    /// </summary>
    internal static void Convert(StrataSettings s, string? mainDevice, IEnumerable<string>? present = null)
    {
        foreach (var snap in s.ParkedMonitors.Values)
            ConvertParked(snap, s);

        if (s.TaskbarPositions is null && s.SecondTaskbars is null && s.TaskbarHiddenScreens is null
            && s.BarItems is null && s.BarItemZones is null)
            return;

        // La campana llegó antes que los diseños: las barras elegidas a mano la reciben junto al reloj.
        BarLayout.AddNotificationBell(s);

        var positions = s.TaskbarPositions ?? new();
        var seconds = s.SecondTaskbars ?? new();
        var hidden = s.TaskbarHiddenScreens ?? new();
        var items = s.BarItems ?? new();
        var zones = s.BarItemZones ?? new();

        var devices = positions.Keys.Concat(seconds.Keys).Concat(hidden)
            .Concat(items.Keys.Where(k => k.Contains('|')).Select(BarLayout.DeviceOf))
            .Concat(zones.Keys.Where(k => k.Contains('|')).Select(BarLayout.DeviceOf))
            .Concat(present ?? PresentDevices())
            .Distinct()
            .OrderByDescending(d => d == mainDevice)
            .ThenBy(d => d, StringComparer.Ordinal)
            .ToList();

        // Las barras como se armaban antes: el monitor principal primero y la bandeja en la primera
        // barra que la tuviera entre sus elementos (o en la que la llevaba siempre).
        var plan = new List<(string Device, TaskbarPosition Edge, BarRole Role, string OldId)>();
        foreach (var device in devices)
        {
            bool isMain = device == mainDevice;
            if (!isMain && hidden.Contains(device))
                continue;
            var position = positions.TryGetValue(device, out var p) ? p : TaskbarPosition.Bottom;
            var second = seconds.TryGetValue(device, out var c) ? c : SecondBarContent.None;
            foreach (var (edge, role) in BarsFor(position, second, isMain))
                plan.Add((device, edge, role, device + (edge != position ? "|2" : "|1")));
        }
        List<BarItem> OldItems(string id, BarRole role) =>
            items.TryGetValue(id, out var chosen) ? chosen.Distinct().ToList() : BarLayout.Defaults(role, s);
        string? trayBar = plan.FirstOrDefault(b => OldItems(b.OldId, b.Role).Contains(BarItem.Tray)).OldId
                          ?? plan.FirstOrDefault(b => b.Role.HasTray).OldId;

        foreach (var device in devices.Where(d => !s.Layouts.ContainsKey(d)))
        {
            var layout = new ScreenLayout();
            foreach (var (_, edge, role, oldId) in plan.Where(b => b.Device == device))
            {
                var list = OldItems(oldId, role);
                // La bandeja iba en una sola barra (y la principal la llevaba aunque no estuviera en su lista).
                if (oldId != trayBar)
                    list.Remove(BarItem.Tray);
                else if (!list.Contains(BarItem.Tray))
                    list.Insert(list.Contains(BarItem.Start) ? 1 : 0, BarItem.Tray);
                // Las ventanas abiertas no eran un elemento: iban en las barras con apps.
                if (role.ShowsApps && !list.Contains(BarItem.Apps))
                    list.Insert(list.IndexOf(BarItem.Start) + 1, BarItem.Apps);
                var bar = new BarConfig { Edge = edge, Items = list };
                if (zones.TryGetValue(oldId, out var oldZones))
                    foreach (var (item, zone) in oldZones)
                        BarLayout.SetZone(bar, item, zone);
                layout.Bars.Add(bar);
            }
            BarLayout.Normalize(layout);
            s.Layouts[device] = layout;
        }

        s.TaskbarPositions = null;
        s.SecondTaskbars = null;
        s.TaskbarHiddenScreens = null;
        s.BarItems = null;
        s.BarItemZones = null;
    }

    /// <summary>Lo guardado de un monitor desconectado, en el formato de antes.</summary>
    private static void ConvertParked(MonitorSnapshot snap, StrataSettings s)
    {
        if (snap.Layout != null || snap.Position is null && snap.Second is null && !snap.Hidden && snap.Items is null or { Count: 0 })
            return;
        var layout = new ScreenLayout();
        if (!snap.Hidden || snap.Main)
        {
            var position = snap.Position ?? TaskbarPosition.Bottom;
            foreach (var (edge, role) in BarsFor(position, snap.Second ?? SecondBarContent.None, snap.Main))
            {
                string suffix = edge != position ? "2" : "1";
                var list = snap.Items != null && snap.Items.TryGetValue(suffix, out var chosen)
                    ? chosen.Distinct().ToList() : BarLayout.Defaults(role, s);
                if (role.ShowsApps && !list.Contains(BarItem.Apps))
                    list.Insert(list.IndexOf(BarItem.Start) + 1, BarItem.Apps);
                var bar = new BarConfig { Edge = edge, Items = list };
                if (snap.Zones != null && snap.Zones.TryGetValue(suffix, out var oldZones))
                    foreach (var (item, zone) in oldZones)
                        BarLayout.SetZone(bar, item, zone);
                layout.Bars.Add(bar);
            }
        }
        BarLayout.Normalize(layout);
        snap.Layout = layout;
        (snap.Position, snap.Second, snap.Hidden, snap.Items, snap.Zones) = (null, null, false, null, null);
    }

    /// <summary>
    /// Barras de un monitor antes de la 2.6. Con dos se repartían: la segunda (borde opuesto) llevaba
    /// lo elegido y la primera el resto ("Completa": las dos todo). La bandeja iba con el área de
    /// estado del monitor principal.
    /// </summary>
    internal static List<(TaskbarPosition Edge, BarRole Role)> BarsFor(TaskbarPosition position, SecondBarContent second, bool isMain)
    {
        var primary = new BarRole(
            HasTray: isMain && second != SecondBarContent.Status,
            IsMainScreen: isMain,
            ShowsApps: second != SecondBarContent.Apps,
            ShowsStatus: second != SecondBarContent.Status);
        var bars = new List<(TaskbarPosition, BarRole)> { (position, primary) };
        if (second == SecondBarContent.None)
            return bars;
        var secondRole = new BarRole(
            HasTray: isMain && second == SecondBarContent.Status,
            IsMainScreen: isMain,
            ShowsApps: second is SecondBarContent.Apps or SecondBarContent.Full,
            ShowsStatus: second is SecondBarContent.Status or SecondBarContent.Full);
        bars.Insert(secondRole.HasTray ? 0 : 1, (Opposite(position), secondRole));
        return bars;
    }

    private static TaskbarPosition Opposite(TaskbarPosition position) => position switch
    {
        TaskbarPosition.Top => TaskbarPosition.Bottom,
        TaskbarPosition.Left => TaskbarPosition.Right,
        TaskbarPosition.Right => TaskbarPosition.Left,
        _ => TaskbarPosition.Top,
    };
}
