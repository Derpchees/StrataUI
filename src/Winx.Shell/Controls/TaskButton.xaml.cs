using System.Windows;
using System.Windows.Controls;
using ManagedShell.WindowsTasks;
using Winx.Shell.Services;

namespace Winx.Shell.Controls;

public partial class TaskButton : UserControl
{
    public TaskButton()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (System.Windows.Window.GetWindow(this) is Taskbar bar)
                FitTo(bar.Position);
        };
    }

    /// <summary>
    /// El indicador va junto al borde de la pantalla, como en Windows; en barras verticales se gira y el
    /// botón toma un alto fijo (en las horizontales lo da el alto de la barra).
    /// </summary>
    private void FitTo(TaskbarPosition position)
    {
        switch (position)
        {
            case TaskbarPosition.Top:
                Indicator.VerticalAlignment = VerticalAlignment.Top;
                Indicator.Margin = new Thickness(0, 2, 0, 0);
                break;
            case TaskbarPosition.Left or TaskbarPosition.Right:
                Root.Height = 40;
                // Girado 90° el indicador pasa de horizontal a vertical.
                Indicator.LayoutTransform = new System.Windows.Media.RotateTransform(90);
                Indicator.VerticalAlignment = VerticalAlignment.Center;
                Indicator.HorizontalAlignment = position == TaskbarPosition.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                Indicator.Margin = position == TaskbarPosition.Left ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 2, 0);
                break;
        }
    }

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
