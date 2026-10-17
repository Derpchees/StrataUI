using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Strata.Shell.Services.Localization;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Services;

/// <summary>
/// Ícono de Strata UI en la bandeja (la suya, como el de cualquier app): clic para abrir el Hub,
/// clic derecho para el modo edición, reiniciar o salir. Se registra con Shell_NotifyIcon, así que
/// se ordena, se oculta o se ancla como los demás.
/// </summary>
internal static class StrataTrayIcon
{
    private static Forms.NotifyIcon? _icon;
    private static ContextMenu? _menu;

    public static void Show()
    {
        if (_icon != null)
            return;
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/strataui.ico"));
            using var stream = resource.Stream;
            _icon = new Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
                Text = "Strata UI",
                Visible = true,
            };
            _icon.MouseUp += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                    App.Current.OpenHub();
                else if (e.Button == Forms.MouseButtons.Right)
                    ShowMenu();
            };
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo mostrar el ícono de Strata UI en la bandeja", ex);
        }
    }

    public static void Remove()
    {
        if (_icon is null)
            return;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _icon = null;
    }

    private static void ShowMenu()
    {
        _menu ??= CreateMenu();
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private static ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Entry(Loc.T("Configuración de Strata UI"), () => App.Current.OpenHub(), bold: true));
        menu.Items.Add(Entry(Loc.T("Editar diseño…"), () => EditMode.EditSession.Start()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("Reiniciar Strata UI"), () => App.Current.ExitShell(restart: true)));
        menu.Items.Add(Entry(Loc.T("Salir de Strata UI"), () => App.Current.ExitShell()));
        return menu;
    }

    private static MenuItem Entry(string header, Action action, bool bold = false)
    {
        var entry = new MenuItem { Header = header };
        if (bold)
            entry.FontWeight = FontWeights.SemiBold;
        entry.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"Bandeja de Strata UI: \"{header}\" falló", ex);
            }
        };
        return entry;
    }
}
