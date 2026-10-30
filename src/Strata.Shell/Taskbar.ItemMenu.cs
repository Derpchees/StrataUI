using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;

namespace Strata.Shell;

/// <summary>
/// Clic derecho en un elemento de la barra (red, volumen, reloj, medidores…): abrir su
/// configuración en Windows, sus opciones en Strata UI o quitarlo de esta barra. Inicio conserva su menú
/// (Win+X) y los íconos de la bandeja, el de su app.
/// </summary>
public partial class Taskbar
{
    private void WatchItemMenu()
    {
        foreach (var panel in ZonePanels)
            panel.ContextMenuOpening += OnItemMenuOpening;
    }

    private void OnItemMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ItemAt(e.OriginalSource as DependencyObject) is not { } item || item == BarItem.Start || Element(item) is not { } element)
            return; // fuera de un elemento (o un ícono de la bandeja): el menú de la barra; Inicio tiene el suyo (Win+X)
        e.Handled = true;
        var menu = ItemMenu(item);
        menu.PlacementTarget = element;
        menu.Placement = Position switch
        {
            TaskbarPosition.Top => PlacementMode.Bottom,
            TaskbarPosition.Left => PlacementMode.Right,
            TaskbarPosition.Right => PlacementMode.Left,
            _ => PlacementMode.Top,
        };
        menu.IsOpen = true;
    }

    private ContextMenu ItemMenu(BarItem item)
    {
        var menu = new ContextMenu();
        foreach (var (label, uri) in WindowsSettings(item))
            menu.Items.Add(Entry(label, () => OpenUri(uri)));
        if (item is BarItem.Cpu or BarItem.Gpu or BarItem.Ram or BarItem.NetSpeed)
            menu.Items.Add(Entry(Loc.T("Administrador de tareas"), ManagedShell.Common.Helpers.ShellHelper.StartTaskManager));
        foreach (var option in ItemShortcuts(item))
            menu.Items.Add(option);
        menu.Items.Add(Entry(Loc.T("Opciones en Strata UI"), () => App.Current.OpenHub(HubPage(item))));
        menu.Items.Add(new Separator());
        // Posición a lo largo de la barra: izquierda/centro/derecha (arriba/centro/abajo en vertical).
        var position = new MenuItem { Header = Loc.T("Posición en la barra") };
        foreach (var zone in new[] { BarZone.Start, BarZone.Center, BarZone.End })
        {
            var entry = Entry(BarLayout.ZoneName(zone, IsVertical), () => Settings.Update(s => BarLayout.SetZone(BarId, item, zone, s)));
            entry.IsCheckable = true; // para que se vea la marca en la zona actual
            entry.IsChecked = ZoneOf(item) == zone;
            position.Items.Add(entry);
        }
        menu.Items.Add(position);
        if (item != BarItem.Tray)
            menu.Items.Add(Entry(Loc.T("Quitar de esta barra"), () => RemoveItem(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("Editar diseño…"), () => EditMode.EditSession.Start()));
        return menu;
    }

    /// <summary>Lo propio de cada elemento en su clic derecho: qué hace el clic de la RAM, la prueba y la unidad de la velocidad.</summary>
    private IEnumerable<object> ItemShortcuts(BarItem item)
    {
        switch (item)
        {
            case BarItem.Ram:
            {
                yield return Entry(Loc.T("Liberar RAM ahora"), () => FreeRam(RamButton));
                var toggle = Entry(Loc.T("Liberar RAM al hacer clic"), () => Settings.Update(s => s.RamClickFreesRam = !s.RamClickFreesRam));
                toggle.IsCheckable = true;
                toggle.IsChecked = Settings.Current.RamClickFreesRam;
                yield return toggle;
                break;
            }
            case BarItem.NetSpeed:
            {
                yield return Entry(SpeedTest.Running ? Loc.T("Cancelar la prueba") : Loc.T("Probar velocidad"), () =>
                {
                    if (SpeedTest.Running)
                        SpeedTest.Cancel();
                    else
                    {
                        SpeedTest.Start();
                        Flyouts.FlyoutManager.Toggle<Flyouts.NetSpeedFlyout>(NetSpeedButton);
                    }
                });
                var unit = new MenuItem { Header = Loc.T("Unidad") };
                foreach (var (name, bits) in new[] { (Loc.T("Megabytes por segundo (MB/s)"), false), (Loc.T("Megabits por segundo (Mb/s)"), true) })
                {
                    var entry = Entry(name, () => Settings.Update(s => s.NetSpeedInBits = bits));
                    entry.IsCheckable = true;
                    entry.IsChecked = Settings.Current.NetSpeedInBits == bits;
                    unit.Items.Add(entry);
                }
                yield return unit;
                break;
            }
        }
    }

    private static MenuItem Entry(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo abrir \"{header}\"", ex);
            }
        };
        return entry;
    }

    /// <summary>Página del Hub con las opciones de cada elemento.</summary>
    internal static string HubPage(BarItem item) => item switch
    {
        BarItem.Media => "Media",
        BarItem.Tray => "Tray",
        BarItem.Network or BarItem.Bluetooth or BarItem.Brightness or BarItem.Volume or BarItem.Battery => "Flyouts",
        _ => "Taskbar",
    };

    /// <summary>Página de Configuración de Windows de cada elemento.</summary>
    internal static IEnumerable<(string Label, string Uri)> WindowsSettings(BarItem item) => item switch
    {
        BarItem.Network => [(Loc.T("Configuración de red"), "ms-settings:network")],
        BarItem.Bluetooth => [(Loc.T("Configuración de Bluetooth"), "ms-settings:bluetooth")],
        BarItem.Brightness => [(Loc.T("Configuración de pantalla"), "ms-settings:display")],
        BarItem.Volume => [(Loc.T("Configuración de sonido"), "ms-settings:sound")],
        BarItem.Battery => [(Loc.T("Configuración de energía y batería"), "ms-settings:powersleep")],
        BarItem.Notifications => [(Loc.T("Configuración de notificaciones"), "ms-settings:notifications")],
        BarItem.Screenshot => [(Loc.T("Configuración de captura de pantalla"), "ms-settings:keyboard")],
        BarItem.Power => [(Loc.T("Configuración de energía y batería"), "ms-settings:powersleep")],
        BarItem.Clock => [(Loc.T("Configuración de fecha y hora"), "ms-settings:dateandtime"),
                          (Loc.T("Configuración de notificaciones"), "ms-settings:notifications")],
        BarItem.Media => [(Loc.T("Configuración de sonido"), "ms-settings:sound")],
        BarItem.Tray => [(Loc.T("Configuración de la barra de tareas"), "ms-settings:taskbar")],
        BarItem.Fps => [(Loc.T("Configuración de gráficos"), "ms-settings:display-advancedgraphics")],
        BarItem.FreeRam => [(Loc.T("Configuración de almacenamiento"), "ms-settings:storagesense")],
        BarItem.NetSpeed => [(Loc.T("Configuración de red"), "ms-settings:network")],
        _ => [],
    };

    /// <summary>Quita el elemento de esta barra y lo guarda.</summary>
    private void RemoveItem(BarItem item) =>
        Settings.Update(s => BarLayout.SetItems(s, BarId, _items.Where(i => i != item).ToList()));
}
