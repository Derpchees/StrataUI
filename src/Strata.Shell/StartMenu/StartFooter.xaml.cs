using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Flyouts;
using Strata.Shell.Services.StartMenu;

namespace Strata.Shell.StartMenu;

public partial class StartFooter : UserControl
{
    public StartFooter() => InitializeComponent();

    /// <summary>False en estilos que ya muestran al usuario en otro lugar (menú Perfil).</summary>
    public bool ShowUserBadge
    {
        get => UserBadge.Visibility == Visibility.Visible;
        set
        {
            // Ocultarlo aquí reemplaza el binding con la opción del Hub: en ese estilo nunca se muestra.
            if (!value)
                UserBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void FooterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: StartFooterButton button } source)
            return;

        if (button.IsPowerMenu)
        {
            var menu = (ContextMenu)FindResource("PowerMenu");
            var updates = StartMenuService.UpdatesPending ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in menu.Items.OfType<MenuItem>())
                if (item.CommandParameter is "UpdateRestart" or "UpdatePowerOff")
                    item.Visibility = updates;
            menu.PlacementTarget = source;
            menu.IsOpen = true;
            return;
        }
        FlyoutManager.CloseAll();
        button.Run?.Invoke();
    }
}
