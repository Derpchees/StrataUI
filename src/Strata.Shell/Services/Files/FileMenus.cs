using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Strata.Shell.Services.Files;

/// <summary>Piezas de los menús contextuales de archivos (Archivos y el escritorio de Strata UI).</summary>
public static class FileMenus
{
    public static MenuItem Item(string header, SymbolRegular? icon, Action action, bool enabled = true, bool bold = false)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (icon is { } symbol)
            item.Icon = new SymbolIcon { Symbol = symbol };
        if (bold)
            item.FontWeight = FontWeights.SemiBold;
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"Menú de archivos: \"{header}\" falló", ex);
            }
        };
        return item;
    }

    /// <summary>
    /// Las opciones que Windows y las apps agregan al menú (NanaZip, un lector de PDF, Enviar a,
    /// Propiedades, Nuevo…), leídas en el momento en otro proceso (<see cref="ShellMenuSession"/>):
    /// el menú se abre enseguida y estas llegan debajo, también las de programas que se instalen después.
    /// </summary>
    public static void AddWindowsItems(ContextMenu menu, IReadOnlyList<string> paths, bool background, bool desktop = false)
    {
        if (ShellMenuSession.Start(paths, background, desktop) is not { } session)
            return;
        var separator = new Separator();
        var loading = new MenuItem { Header = Loc.T("Cargando opciones de Windows…"), IsEnabled = false };
        menu.Items.Add(separator);
        menu.Items.Add(loading);
        menu.Closed += Closed;
        _ = FillAsync();

        void Closed(object? sender, RoutedEventArgs e)
        {
            menu.Closed -= Closed;
            // Después del clic (si fue en una de estas, ya se pidió ejecutarla).
            menu.Dispatcher.BeginInvoke(session.Dispose, DispatcherPriority.Background);
        }

        async Task FillAsync()
        {
            var items = await session.Items;
            int index = menu.Items.IndexOf(loading);
            if (index < 0)
                return; // el menú ya se volvió a armar
            menu.Items.Remove(loading);
            if (items.Count == 0)
            {
                menu.Items.Remove(separator);
                return;
            }
            foreach (var item in items)
                menu.Items.Insert(index++, WindowsItem(item, session));
        }
    }

    private static Control WindowsItem(NativeMenuItem item, ShellMenuSession session)
    {
        if (item.Separator)
            return new Separator();
        // "_" es la tecla de acceso en WPF: se duplica para que se vea tal cual.
        var menuItem = new MenuItem { Header = item.Text.Replace("_", "__"), IsEnabled = item.Enabled, IsChecked = item.Checked };
        if (ShellMenuSession.IconOf(item) is { } icon)
            menuItem.Icon = new System.Windows.Controls.Image { Source = icon, Width = 16, Height = 16 };
        if (item.Children is { } children)
        {
            foreach (var child in children)
                menuItem.Items.Add(WindowsItem(child, session));
        }
        else
        {
            menuItem.Click += (_, _) => session.Invoke(item.Id);
        }
        return menuItem;
    }
}
