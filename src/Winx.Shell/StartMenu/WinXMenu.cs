using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;
using Winx.Shell.Services.StartMenu;

namespace Winx.Shell.StartMenu;

/// <summary>
/// Menú del clic derecho en Inicio (y de Win+X), con las mismas entradas que el de Windows. Se
/// arma con controles de WPF para que siga el tema de WINX y se abra junto al botón aunque la
/// barra esté centrada o en un costado; el de Windows aparece anclado a su barra oculta.
/// </summary>
public static class WinXMenu
{
    public static ContextMenu Create(FrameworkElement startButton)
    {
        var menu = new ContextMenu();
        Add(menu, Loc.T("Aplicaciones instaladas"), () => Open("ms-settings:appsfeatures"));
        Add(menu, Loc.T("Opciones de energía"), () => Open("ms-settings:powersleep"));
        Add(menu, Loc.T("Visor de eventos"), () => Open("eventvwr.msc"));
        Add(menu, Loc.T("Sistema"), () => Open("ms-settings:about"));
        Add(menu, Loc.T("Administrador de dispositivos"), () => Open("devmgmt.msc"));
        Add(menu, Loc.T("Conexiones de red"), () => Open("ms-settings:network"));
        Add(menu, Loc.T("Administración de discos"), () => Open("diskmgmt.msc"));
        Add(menu, Loc.T("Administración de equipos"), () => Open("compmgmt.msc"));
        Add(menu, Loc.T("Terminal"), () => Open("wt.exe"));
        Add(menu, Loc.T("Terminal (administrador)"), () => Open("wt.exe", elevated: true));
        menu.Items.Add(new Separator());
        Add(menu, Loc.T("Administrador de tareas"), () => Open("taskmgr.exe"));
        Add(menu, Loc.T("Configuración"), () => Open("ms-settings:"));
        Add(menu, Loc.T("Explorador de archivos"), () => Open("explorer.exe"));
        Add(menu, Loc.T("Buscar"), () => StartMenuLauncher.Toggle(startButton));
        Add(menu, Loc.T("Ejecutar"), RunDialog);
        menu.Items.Add(new Separator());
        var power = new MenuItem { Header = Loc.T("Apagar o cerrar sesión") };
        Add(power, Loc.T("Cerrar sesión"), StartMenuService.SignOut);
        Add(power, Loc.T("Suspender"), StartMenuService.Sleep);
        Add(power, Loc.T("Apagar"), StartMenuService.PowerOff);
        Add(power, Loc.T("Reiniciar"), StartMenuService.Restart);
        menu.Items.Add(power);
        Add(menu, Loc.T("Escritorio"), ShowDesktop);
        return menu;
    }

    /// <summary>Abre el menú pegado al botón (para Win+X, que no tiene posición de mouse).</summary>
    public static void ShowAt(ContextMenu menu, FrameworkElement startButton, TaskbarPosition bar)
    {
        menu.PlacementTarget = startButton;
        menu.Placement = bar switch
        {
            TaskbarPosition.Top => PlacementMode.Bottom,
            TaskbarPosition.Left => PlacementMode.Right,
            TaskbarPosition.Right => PlacementMode.Left,
            _ => PlacementMode.Top,
        };
        menu.IsOpen = true;
        // Al abrirse con el mouse, el menú vuelve a ubicarse donde se hizo clic.
        menu.Closed += Reset;
        void Reset(object? sender, RoutedEventArgs e)
        {
            menu.Closed -= Reset;
            menu.Placement = PlacementMode.MousePoint;
        }
    }

    private static void Add(ItemsControl parent, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        parent.Items.Add(item);
    }

    private static void Open(string target, bool elevated = false)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Verb = elevated ? "runas" : "" });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == Tweaks.Elevation.Cancelled)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir {target}", ex);
        }
    }

    private static void RunDialog() => Shell(app => app.FileRun());

    private static void ShowDesktop() => Shell(app => app.ToggleDesktop());

    private static void Shell(Action<dynamic> action)
    {
        if (Type.GetTypeFromProgID("Shell.Application") is { } type)
            action(Activator.CreateInstance(type)!);
    }
}
