using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Flyouts;
using Winx.Shell.Services.StartMenu;

namespace Winx.Shell.StartMenu;

public partial class StartFooter : UserControl
{
    public StartFooter() => InitializeComponent();

    private void FooterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: StartFooterButton button } source)
            return;

        if (button.IsPowerMenu)
        {
            var menu = (ContextMenu)FindResource("PowerMenu");
            menu.PlacementTarget = source;
            menu.IsOpen = true;
            return;
        }
        FlyoutManager.CloseAll();
        button.Run?.Invoke();
    }
}
