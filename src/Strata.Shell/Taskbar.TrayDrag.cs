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
        Mouse.OverrideCursor = Cursors.Hand;
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
        Mouse.OverrideCursor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        HiddenTrayPopup.StaysOpen = false;
        TrayPromotion.SaveOrder();
        UpdateChevron();
    }

    private void TrayDragMove(ShellNotifyIcon icon)
    {
        GetCursorPos(out var cursor);
        var point = new Point(cursor.X, cursor.Y);

        if (!HiddenTrayPopup.IsOpen && ScreenRect(ChevronButton, 2).Contains(point))
        {
            HiddenTrayPopup.IsOpen = true;
            return;
        }

        if (HiddenTrayPopup.IsOpen && HiddenTrayPopup.Child is FrameworkElement popup && ScreenRect(popup, 0).Contains(point))
        {
            var hidden = TrayPromotion.HiddenIcons();
            int target = IndexUnder(HiddenTray, point, hidden.Count);
            if (target >= 0 && (icon.IsPinned || hidden.IndexOf(icon) != target))
                TrayPromotion.MoveToHidden(icon, target, save: false);
        }
        else if (ScreenRect(TrayArea, 16).Contains(point))
        {
            var pinned = _shell.NotificationArea.PinnedIcons.OfType<ShellNotifyIcon>().ToList();
            int target = IndexUnder(PinnedTray, point, pinned.Count);
            if (target >= 0 && (!icon.IsPinned || pinned.IndexOf(icon) != target))
                TrayPromotion.MoveToPinned(icon, target, save: false);
        }
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
