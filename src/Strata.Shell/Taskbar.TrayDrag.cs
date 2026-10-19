using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Strata.Shell.Controls;
using Strata.Shell.Services;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace Strata.Shell;

/// <summary>
/// Ordenar la bandeja arrastrando, como en Windows: dentro de los ocultos los reordena, y entre
/// los ocultos y la barra ancla o desancla (también soltándolo sobre la flecha). Los cambios se
/// ven mientras se arrastra y se guardan al soltar.
/// </summary>
/// <remarks>
/// La captura del mouse va a la ventana de la barra (no al ícono): los íconos se regeneran al
/// moverse y perderían la captura. El panel de ocultos se mantiene abierto mientras tanto y, si
/// el arrastre empezó en la barra (pulsar ahí lo cerró), se abre al pasar sobre la flecha (^).
/// </remarks>
public partial class Taskbar
{
    private ShellNotifyIcon? _trayDrag;

    private void OnTrayDragStarted(TrayIcon source)
    {
        if (!HasTray || source.NotifyIcon is not { } icon
            || !(HiddenTray.IsAncestorOf(source) || PinnedTray.IsAncestorOf(source)))
            return;
        _trayDrag = icon;
        HiddenTrayPopup.StaysOpen = true;
        ChevronButton.Visibility = Visibility.Visible; // para poder soltar en los ocultos aunque no quede ninguno
        if (!CaptureMouse())
        {
            EndTrayDrag();
            return;
        }
        Mouse.OverrideCursor = Cursors.SizeAll;
        Controls.DragGhost.Begin(source); // antes de atenuarlo: la imagen lo muestra entero
        MarkTrayDragging();
    }

    /// <summary>
    /// El ícono que se arrastra se ve tenue en su lugar. Los íconos se regeneran al reordenarse:
    /// se marca por el ícono de la app, no por el control, cada vez que cambia el orden.
    /// </summary>
    private void MarkTrayDragging()
    {
        foreach (var list in new[] { PinnedTray, HiddenTray })
        {
            foreach (var tray in TrayIcons(list))
                tray.Opacity = _trayDrag != null && ReferenceEquals(tray.NotifyIcon, _trayDrag) ? 0.55 : 1;
        }
    }

    private static IEnumerable<TrayIcon> TrayIcons(ItemsControl list)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject container
                && FindTrayIcon(container) is { } tray)
                yield return tray;
        }
    }

    private static TrayIcon? FindTrayIcon(DependencyObject parent)
    {
        if (parent is TrayIcon tray)
            return tray;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (FindTrayIcon(System.Windows.Media.VisualTreeHelper.GetChild(parent, i)) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>Dónde se ve cada ícono de la lista ahora (en pantalla, con lo que lleve de su animación).</summary>
    private static Dictionary<ShellNotifyIcon, Point> TrayPositions(ItemsControl list)
    {
        var positions = new Dictionary<ShellNotifyIcon, Point>();
        foreach (var tray in TrayIcons(list))
        {
            if (tray.NotifyIcon is { } icon && tray.IsVisible && PresentationSource.FromVisual(tray) != null)
                positions[icon] = tray.PointToScreen(default);
        }
        return positions;
    }

    /// <summary>
    /// Los demás íconos se deslizan desde donde estaban, en vez de saltar (el arrastrado no: lo
    /// representa la imagen en la mano). Igual que <see cref="SlideFrom"/>, pero por ícono.
    /// </summary>
    private void SlideTray(ItemsControl list, Dictionary<ShellNotifyIcon, Point> before)
    {
        if (Settings.Current.PanelAnimation == PanelAnimation.None)
            return;
        list.UpdateLayout();
        foreach (var tray in TrayIcons(list))
        {
            if (tray.NotifyIcon is not { } icon || ReferenceEquals(icon, _trayDrag) || !before.TryGetValue(icon, out var old)
                || !tray.IsVisible || PresentationSource.FromVisual(tray) is null)
                continue;
            var now = tray.PointToScreen(default);
            double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(tray).PixelsPerDip;
            double dx = (old.X - now.X) / dpi, dy = (old.Y - now.Y) / dpi;
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
                continue;
            if (tray.RenderTransform is not System.Windows.Media.TranslateTransform slide)
                tray.RenderTransform = slide = new System.Windows.Media.TranslateTransform();
            slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new System.Windows.Media.Animation.DoubleAnimation(slide.X + dx, 0, SlideDuration) { EasingFunction = SlideEasing });
            slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(slide.Y + dy, 0, SlideDuration) { EasingFunction = SlideEasing });
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_trayDrag is { } icon)
            TrayDragMove(icon);
        else if (_pinDrag is { } pin)
            PinDragMove(pin);
        else if (_taskDrag is { } window)
            TaskDragMove(window);
        else if (_itemDrag is { } item)
            ItemDragMove(item);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_trayDrag != null)
        {
            EndTrayDrag();
            e.Handled = true;
        }
        else if (_pinDrag != null)
        {
            EndPinDrag();
            e.Handled = true;
        }
        else if (_taskDrag != null)
        {
            EndTaskDrag();
            e.Handled = true;
        }
        else if (_itemDrag != null)
        {
            EndItemDrag();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_trayDrag != null && ReferenceEquals(e.OriginalSource, this))
            EndTrayDrag();
        if (_taskDrag != null && ReferenceEquals(e.OriginalSource, this))
            EndTaskDrag();
        if (_pinDrag != null && ReferenceEquals(e.OriginalSource, this))
            EndPinDrag();
        if (_itemDrag != null && ReferenceEquals(e.OriginalSource, this))
            EndItemDrag();
    }

    private void EndTrayDrag()
    {
        // Soltar sobre la flecha (^) lo manda al final de los ocultos, como en Windows.
        if (_trayDrag is { IsPinned: true } dropped && GetCursorPos(out var cursor)
            && ScreenRect(ChevronButton, 2).Contains(new Point(cursor.X, cursor.Y)))
            TrayPromotion.MoveToHidden(dropped, TrayPromotion.HiddenIcons().Count, save: false);
        _trayDrag = null;
        Controls.DragGhost.End();
        MarkTrayDragging();
        Mouse.OverrideCursor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        HiddenTrayPopup.StaysOpen = false;
        TrayPromotion.SaveOrder();
        UpdateChevron();
    }

    private void TrayDragMove(ShellNotifyIcon icon)
    {
        Controls.DragGhost.Move();
        GetCursorPos(out var cursor);
        var point = new Point(cursor.X, cursor.Y);

        if (!HiddenTrayPopup.IsOpen && ScreenRect(ChevronButton, 2).Contains(point))
        {
            HiddenTrayPopup.IsOpen = true;
            // Ya abierto: la imagen en la mano vuelve encima del panel y el ícono, si está ahí, se ve tenue.
            Dispatcher.BeginInvoke(() =>
            {
                Controls.DragGhost.BringToFront();
                MarkTrayDragging();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        if (HiddenTrayPopup.IsOpen && HiddenTrayPopup.Child is FrameworkElement popup && ScreenRect(popup, 0).Contains(point))
        {
            var hidden = TrayPromotion.HiddenIcons();
            int target = IndexUnder(HiddenTray, point, hidden.Count);
            if (target >= 0 && (icon.IsPinned || hidden.IndexOf(icon) != target))
                MoveTrayIcon(() => TrayPromotion.MoveToHidden(icon, target, save: false));
        }
        else if (ScreenRect(TrayArea, 16).Contains(point))
        {
            var pinned = _shell.NotificationArea.PinnedIcons.OfType<ShellNotifyIcon>().ToList();
            int target = IndexUnder(PinnedTray, point, pinned.Count);
            if (target >= 0 && (!icon.IsPinned || pinned.IndexOf(icon) != target))
                MoveTrayIcon(() => TrayPromotion.MoveToPinned(icon, target, save: false));
        }
    }

    /// <summary>Cambia el orden y lo hace ver: los vecinos se deslizan y el arrastrado sigue tenue.</summary>
    private void MoveTrayIcon(Action move)
    {
        var pinned = TrayPositions(PinnedTray);
        var hidden = TrayPositions(HiddenTray);
        move();
        SlideTray(PinnedTray, pinned);
        SlideTray(HiddenTray, hidden);
        MarkTrayDragging();
    }

    /// <summary>
    /// Posición de la lista bajo el cursor: la del ícono que está debajo (el arrastrado ocupa su
    /// lugar), el final si está en un espacio libre de la lista, o -1 si no está sobre ella.
    /// </summary>
    private static int IndexUnder(ItemsControl list, Point screen, int count)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement { IsVisible: true } item
                && ScreenRect(item, 0).Contains(screen))
                return i;
        }
        return count;
    }

    private static Rect ScreenRect(FrameworkElement element, double inflate)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is null)
            return Rect.Empty;
        var a = element.PointToScreen(new Point(0, 0));
        var b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        var rect = new Rect(a, b);
        rect.Inflate(inflate, inflate);
        return rect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
