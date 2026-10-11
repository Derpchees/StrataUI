using System.Windows;
using System.Windows.Controls;

namespace Winx.Shell.StartMenu;

public partial class StartFooter : UserControl
{
    public StartFooter() => InitializeComponent();

    // El menú de energía se abre con clic normal, no solo con clic derecho.
    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        if (PowerButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = PowerButton;
            menu.IsOpen = true;
        }
    }
}
