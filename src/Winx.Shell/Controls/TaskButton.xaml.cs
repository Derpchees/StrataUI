using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedShell.WindowsTasks;
using Winx.Shell.Services;

namespace Winx.Shell.Controls;

public partial class TaskButton : UserControl
{
    public TaskButton()
    {
        InitializeComponent();
        MouseEnter += (_, _) =>
        {
            // La vista previa ya muestra el título: el tooltip la taparía.
            ToolTipService.SetIsEnabled(MainButton, !Settings.Current.ShowTaskPreviews);
            if (Window is { } window && Mouse.LeftButton != MouseButtonState.Pressed)
                TaskPreviewWindow.HoverStart(this, window);
        };
        MouseLeave += (_, _) => TaskPreviewWindow.HoverEnd();
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

    /// <summary>Se empezó a arrastrar el botón (para ordenar las ventanas).</summary>
    public static event Action<TaskButton>? DragStarted;

    private Point? _pressedAt;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _pressedAt = e.GetPosition(this);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedAt = null;
            return;
        }
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _pressedAt = null;
            TaskPreviewWindow.Dismiss();
            DragStarted?.Invoke(this);
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        TaskPreviewWindow.Dismiss();
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
