using System.Windows;
using System.Windows.Controls;
using ManagedShell.WindowsTasks;

namespace Winx.Shell.Controls;

public partial class TaskButton : UserControl
{
    public TaskButton() => InitializeComponent();

    private ApplicationWindow? Window => DataContext as ApplicationWindow;

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (Window is not { } window)
            return;

        // Como en Windows: clic en la ventana activa la minimiza; en otra, la trae al frente.
        if (window.State == ApplicationWindow.WindowState.Active && !window.IsMinimized)
            window.Minimize();
        else
            window.BringToFront();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Window?.Close();
}
