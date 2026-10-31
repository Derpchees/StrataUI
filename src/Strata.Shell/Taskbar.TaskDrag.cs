using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ManagedShell.WindowsTasks;
using Strata.Shell.Controls;
using Strata.Shell.Services;

namespace Strata.Shell;

/// <summary>
/// Ordenar las ventanas abiertas arrastrándolas, como en Windows. El nuevo orden se ve mientras
/// se arrastra y vale para todas las barras (<see cref="TaskOrder"/>).
/// </summary>
/// <remarks>
/// Igual que con la bandeja, la captura del mouse va a la ventana de la barra: los botones se
/// regeneran al reordenarse y perderían la captura. Al quitársela al botón, este no recibe el
/// clic, así que soltar no activa ni minimiza la ventana.
/// <para>
/// El botón se desliza por su fila (no sale de la barra ni flota encima de los demás): su lugar
/// queda vacío, y cuando su centro pasa sobre un vecino, este se corre al lugar libre.
/// </para>
/// </remarks>
public partial class Taskbar
{
    private ApplicationWindow? _taskDrag;

    private void OnTaskDragStarted(TaskButton source)
    {
        if (StartPinDrag(source))
            return;
        if (source.DataContext is not ApplicationWindow window || !TaskList.IsAncestorOf(source))
            return;
        _taskDrag = window;
        if (!CaptureMouse())
        {
            EndTaskDrag();
            return;
        }
        Mouse.OverrideCursor = Cursors.SizeAll;
        BeginRowDrag(source, TaskList);
        MarkRowDragging(TaskList, IsTaskDragged);
    }

    private bool IsTaskDragged(object item) =>
        _taskDrag is { } moving && item is ApplicationWindow window && GroupMembers(moving).Contains(window);

    private void EndTaskDrag()
    {
        EndRowDrag(TaskList, TaskKey, IsTaskDragged);
        _taskDrag = null;
        Mouse.OverrideCursor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private static object TaskKey(object item) => item;

    private void TaskDragMove(ApplicationWindow moving)
    {
        DragGhost.Move();
        if (DragGhost.Center is not { } point)
            return;
        var windows = TaskList.Items.OfType<ApplicationWindow>().ToList();
        int from = windows.FindIndex(w => IsTaskDragged(w));
        if (from < 0)
            return;
        for (int i = 0; i < windows.Count; i++)
        {
            if (i == from || TaskList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement item
                || !SlotRect(item).Contains(point))
                continue;
            // Sobre otro botón: ocupa su lugar (después si venía de antes, antes si venía de después).
            // Con las ventanas agrupadas se mueve el grupo entero y se pasa por encima del otro grupo completo.
            bool after = i > from;
            var target = GroupMembers(windows[i]);
            MoveInRow(TaskList, TaskKey, IsTaskDragged,
                      () => TaskOrder.Instance.Move(GroupMembers(moving), after ? target[^1] : target[0], after, AllWindows));
            return;
        }
    }

    private void OnTaskOrderChanged()
    {
        _taskView.Refresh();
        TasksChanged?.Invoke(); // el primero del grupo pudo cambiar
    }

    // ── Arrastrar dentro de una fila (ventanas y anclados) ──────────────────

    /// <summary>La imagen del botón, que se desliza por la fila de <paramref name="list"/>.</summary>
    private void BeginRowDrag(FrameworkElement source, ItemsControl list) =>
        DragGhost.Begin(source, ScreenRect(list, 0), IsVertical);

    /// <summary>
    /// Cambia el orden y lo hace ver: el lugar del arrastrado queda vacío y los vecinos que se
    /// corrieron se deslizan desde donde estaban.
    /// </summary>
    private void MoveInRow(ItemsControl list, Func<object, object> key, Func<object, bool> dragged, Action move)
    {
        var before = RowPositions(list, key);
        move();
        list.UpdateLayout();
        MarkRowDragging(list, dragged);
        if (Settings.Current.PanelAnimation == PanelAnimation.None)
            return;
        foreach (var (item, element) in RowContainers(list))
        {
            if (dragged(item) || !before.TryGetValue(key(item), out var old))
                continue;
            SlideBy(element, old - element.TranslatePoint(default, list));
        }
    }

    /// <summary>El lugar del arrastrado, vacío (la imagen ocupa su lugar); los demás, visibles.</summary>
    private static void MarkRowDragging(ItemsControl list, Func<object, bool> dragged)
    {
        foreach (var (item, element) in RowContainers(list))
            element.Opacity = dragged(item) ? 0 : 1;
    }

    /// <summary>Al soltar: el botón vuelve a verse y llega desde donde estaba la imagen a su lugar.</summary>
    private void EndRowDrag(ItemsControl list, Func<object, object> key, Func<object, bool> dragged)
    {
        var at = DragGhost.Position;
        DragGhost.End();
        foreach (var (item, element) in RowContainers(list))
        {
            element.Opacity = 1;
            if (at is not { } ghost || !dragged(item) || PresentationSource.FromVisual(element) is null
                || Settings.Current.PanelAnimation == PanelAnimation.None)
                continue;
            double dpi = VisualTreeHelper.GetDpi(element).DpiScaleX;
            var now = SlotRect(element).TopLeft;
            SlideBy(element, new Vector((ghost.X - now.X) / dpi, (ghost.Y - now.Y) / dpi));
        }
    }

    private static IEnumerable<(object Item, FrameworkElement Element)> RowContainers(ItemsControl list)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement element)
                yield return (list.Items[i], element);
        }
    }

    /// <summary>Dónde se ve cada botón de la fila ahora (con lo que lleve de su animación).</summary>
    private static Dictionary<object, Point> RowPositions(ItemsControl list, Func<object, object> key)
    {
        var positions = new Dictionary<object, Point>();
        foreach (var (item, element) in RowContainers(list))
        {
            if (element.IsVisible)
                positions[key(item)] = element.TranslatePoint(default, list);
        }
        return positions;
    }

    /// <summary>Lo lleva <paramref name="offset"/> (en DIP, sumado a lo que ya lleve corrido) y lo desliza a su lugar.</summary>
    private static void SlideBy(FrameworkElement element, Vector offset)
    {
        if (Math.Abs(offset.X) < 0.5 && Math.Abs(offset.Y) < 0.5)
            return;
        if (element.RenderTransform is not TranslateTransform slide)
        {
            if (element.RenderTransform is { } other && !other.Value.IsIdentity)
                return; // tiene otra transformación (no se toca)
            element.RenderTransform = slide = new TranslateTransform();
        }
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(slide.X + offset.X, 0, SlideDuration) { EasingFunction = SlideEasing });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(slide.Y + offset.Y, 0, SlideDuration) { EasingFunction = SlideEasing });
    }

    /// <summary>
    /// Su lugar en la fila (en pantalla), sin lo que lleve de su deslizamiento: si no, un vecino que
    /// recién se corrió seguiría bajo la imagen y volvería a cambiar de lugar.
    /// </summary>
    private static Rect SlotRect(FrameworkElement element)
    {
        var rect = ScreenRect(element, 0);
        if (!rect.IsEmpty && element.RenderTransform is TranslateTransform slide)
        {
            double dpi = VisualTreeHelper.GetDpi(element).DpiScaleX;
            rect.Offset(-slide.X * dpi, -slide.Y * dpi);
        }
        return rect;
    }
}
