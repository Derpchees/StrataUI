using System.Windows;
using System.Windows.Input;
using ManagedShell.WindowsTasks;
using Winx.Shell.Controls;
using Winx.Shell.Services;

namespace Winx.Shell;

/// <summary>
/// Ordenar las ventanas abiertas arrastrándolas, como en Windows. El nuevo orden se ve mientras
/// se arrastra y vale para todas las barras (<see cref="TaskOrder"/>).
/// </summary>
/// <remarks>
/// Igual que con la bandeja, la captura del mouse va a la ventana de la barra: los botones se
/// regeneran al reordenarse y perderían la captura. Al quitársela al botón, este no recibe el
/// clic, así que soltar no activa ni minimiza la ventana.
/// </remarks>
public partial class Taskbar
{
    private ApplicationWindow? _taskDrag;

    private void OnTaskDragStarted(TaskButton source)
    {
        if (source.DataContext is not ApplicationWindow window || !TaskList.IsAncestorOf(source))
            return;
        _taskDrag = window;
        if (!CaptureMouse())
        {
            EndTaskDrag();
            return;
        }
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void EndTaskDrag()
    {
        _taskDrag = null;
        Mouse.OverrideCursor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void TaskDragMove(ApplicationWindow moving)
    {
        GetCursorPos(out var cursor);
        var point = new Point(cursor.X, cursor.Y);
        var windows = TaskList.Items.OfType<ApplicationWindow>().ToList();
        int from = windows.IndexOf(moving);
        if (from < 0)
            return;
        for (int i = 0; i < windows.Count; i++)
        {
            if (i == from || TaskList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement item
                || !ScreenRect(item, 0).Contains(point))
                continue;
            // Sobre otro botón: ocupa su lugar (después si venía de antes, antes si venía de después).
            // Con las ventanas agrupadas se mueve el grupo entero y se pasa por encima del otro grupo completo.
            bool after = i > from;
            var target = GroupMembers(windows[i]);
            TaskOrder.Instance.Move(GroupMembers(moving), after ? target[^1] : target[0], after, AllWindows);
            return;
        }
    }

    private void OnTaskOrderChanged()
    {
        _taskView.Refresh();
        TasksChanged?.Invoke(); // el primero del grupo pudo cambiar
    }
}
