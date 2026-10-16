using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Strata.Shell.Services;

namespace Strata.Shell;

/// <summary>
/// Ordenar los elementos de la barra arrastrándolos en la barra misma (red, volumen, reloj,
/// medidores…): el orden cambia en vivo y, sobre otra barra, el elemento pasa a esa. Se guarda al
/// soltar (lo mismo que el editor del Hub, <see cref="BarLayout"/>).
/// </summary>
/// <remarks>
/// Un clic sigue abriendo su panel: el arrastre empieza al mover unos píxeles con el botón
/// pulsado. Entonces la captura pasa a la ventana de la barra (como al ordenar ventanas o íconos
/// de la bandeja), así que el botón no recibe el clic al soltar. Mientras tanto se cambian las
/// listas de <see cref="StrataSettings.BarItems"/> sin guardar y cada barra afectada se rehace con
/// sus propios botones. La bandeja solo se ordena dentro de su barra: cambiarla de barra recrea
/// las barras (se hace desde el Hub). Inicio puede pasar a otra barra, siempre delante de las ventanas.
/// </remarks>
public partial class Taskbar
{
    private const double ItemDragThreshold = 6;

    private BarItem? _itemPressed;
    private Point _itemPressPoint;
    private BarItem? _itemDrag;
    private Taskbar? _itemOwner; // barra que lleva ahora el elemento arrastrado

    private void WatchItemDrag()
    {
        foreach (var panel in ZonePanels)
            panel.PreviewMouseLeftButtonDown += OnItemMouseDown;
        StartButton.PreviewMouseLeftButtonDown += OnItemMouseDown;
        PreviewMouseMove += OnItemPreviewMove;
        PreviewMouseLeftButtonUp += (_, _) => _itemPressed = null;
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        _itemPressed = ItemAt(e.OriginalSource as DependencyObject);
        _itemPressPoint = e.GetPosition(this);
    }

    /// <summary>El elemento de la barra que contiene lo pulsado (no los íconos de la bandeja: esos tienen su propio arrastre).</summary>
    private BarItem? ItemAt(DependencyObject? source)
    {
        for (var current = source; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, PinnedTray) || ReferenceEquals(current, HiddenTray))
                return null;
            if (ReferenceEquals(current, StartButton))
                return BarItem.Start;
            if (current is FrameworkElement element && element.Parent is System.Windows.Controls.StackPanel parent && ZonePanels.Contains(parent))
                return ItemOf(element);
        }
        return null;
    }

    private BarItem? ItemOf(FrameworkElement element) =>
        Enum.GetValues<BarItem>().Cast<BarItem?>().FirstOrDefault(item => ReferenceEquals(Element(item!.Value), element));

    private void OnItemPreviewMove(object sender, MouseEventArgs e)
    {
        if (_itemPressed is not { } item || _itemDrag != null || e.LeftButton != MouseButtonState.Pressed
            || _taskDrag != null || _trayDrag != null)
            return;
        if ((e.GetPosition(this) - _itemPressPoint).Length < ItemDragThreshold)
            return;
        _itemPressed = null;
        StartItemDrag(item);
    }

    private void StartItemDrag(BarItem item)
    {
        // Todas las barras quedan con su lista explícita: así mover algo no deja a otra con los de siempre.
        foreach (var bar in App.Current.Taskbars)
            Settings.Current.BarItems[bar.BarId] = bar.Items.ToList();
        _itemDrag = item;
        _itemOwner = this;
        if (!CaptureMouse())
        {
            EndItemDrag();
            return;
        }
        Mouse.OverrideCursor = Cursors.SizeAll;
        if ((item == BarItem.Start ? StartButton : Element(item)) is { } element)
            Controls.DragGhost.Begin(element); // antes de atenuarlo: la imagen lo muestra entero
        SetDragging(item, true);
        if (item != BarItem.Start)
            foreach (var bar in App.Current.Taskbars)
                bar.ShowDropZones(true);
    }

    private void ItemDragMove(BarItem item)
    {
        Controls.DragGhost.Move();
        GetCursorPos(out var cursor);
        var point = new Point(cursor.X, cursor.Y);
        if (App.Current.Taskbars.FirstOrDefault(bar => ScreenRect(bar.BarBorder, 0).Contains(point)) is not { } target
            || _itemOwner is not { } owner)
            return;
        if (!ReferenceEquals(target, owner) && (item == BarItem.Tray || target.Has(item)))
            return; // la bandeja no cambia de barra aquí; y ninguna barra lleva dos veces lo mismo

        var targetItems = target.Items.ToList();
        int index;
        if (item == BarItem.Start)
        {
            if (ReferenceEquals(target, owner))
                return; // Inicio va siempre delante de las ventanas
            index = 0;
        }
        else
        {
            (index, var zone) = target.IndexAt(point, item);
            if (index < 0)
                return;
            // Pasar a otra zona (inicio, centro, final) cambia la del elemento en esa barra.
            if (zone != target.ZoneOf(item) || !ReferenceEquals(target, owner))
                BarLayout.SetZone(target.BarId, item, zone, Settings.Current);
        }

        int current = ReferenceEquals(target, owner) ? targetItems.IndexOf(item) : -1;
        if (current >= 0)
        {
            bool sameZone = BarLayout.ZoneOf(target.BarId, item, Settings.Current) == target.ZoneOf(item);
            if (sameZone && (index == current || index == current + 1))
                return; // ya está ahí
            targetItems.RemoveAt(current);
            if (index > current)
                index--;
        }
        else
        {
            var ownerItems = owner.Items.ToList();
            ownerItems.Remove(item);
            owner.SetDragging(item, false);
            Settings.Current.BarItems[owner.BarId] = ownerItems;
            owner.ApplyLayout();
        }
        targetItems.Insert(Math.Clamp(index, 0, targetItems.Count), item);
        Settings.Current.BarItems[target.BarId] = targetItems;
        target.ApplyLayout();
        target.SetDragging(item, true);
        _itemOwner = target;
    }

    /// <summary>
    /// Posición en la lista de elementos donde caería lo arrastrado y en qué zona: antes del primero
    /// de esa zona que quede después del puntero (a la derecha, o abajo en las barras verticales).
    /// -1 si el puntero no está sobre ninguna zona (sin ninguna visible vale toda la barra, al final).
    /// </summary>
    private (int Index, BarZone Zone) IndexAt(Point screen, BarItem moving)
    {
        BarZone? zone = null;
        foreach (var candidate in new[] { BarZone.Start, BarZone.Center, BarZone.End })
        {
            if (ScreenRect(ZonePanel(candidate), 12).Contains(screen))
                zone = candidate;
        }
        if (zone is null && !ZonePanels.Any(p => p.IsVisible) && ScreenRect(BarBorder, 0).Contains(screen))
            zone = BarZone.End;
        if (zone is not { } target)
            return (-1, BarZone.End);
        int last = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i] == moving || _zones[i] != target || Element(_items[i]) is not { IsVisible: true } element)
                continue;
            var rect = ScreenRect(element, 0);
            double center = IsVertical ? rect.Top + rect.Height / 2 : rect.Left + rect.Width / 2;
            if ((IsVertical ? screen.Y : screen.X) < center)
                return (i, target);
            last = i;
        }
        // Después del último de esa zona (o al final de la lista si la zona está vacía).
        return (last >= 0 ? last + 1 : _items.Count, target);
    }

    /// <summary>
    /// Mientras se arrastra un elemento, las zonas vacías se ven como un espacio donde soltarlo
    /// (si no, no habría dónde llevarlo al centro o al principio).
    /// </summary>
    private bool _showingDropZones;

    private void ShowDropZones(bool show)
    {
        _showingDropZones = show;
        foreach (var panel in new[] { StartZonePanel, CenterZonePanel })
        {
            if (show && panel.Children.Count == 0)
            {
                panel.Visibility = Visibility.Visible;
                panel.MinWidth = IsVertical ? 0 : 44;
                panel.MinHeight = IsVertical ? 44 : 0;
                panel.Background = (System.Windows.Media.Brush)FindResource("StrataHoverBrush");
            }
            else
            {
                panel.ClearValue(MinWidthProperty);
                panel.ClearValue(MinHeightProperty);
                panel.ClearValue(BackgroundProperty);
                panel.Visibility = panel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>El elemento que se arrastra se ve tenue en la barra donde está.</summary>
    private void SetDragging(BarItem item, bool dragging)
    {
        var element = item == BarItem.Start ? StartButton : Element(item);
        if (element != null)
            element.Opacity = dragging ? 0.55 : 1;
    }

    private void EndItemDrag()
    {
        if (_itemDrag is not { } item)
            return;
        _itemDrag = null;
        Controls.DragGhost.End();
        foreach (var bar in App.Current.Taskbars)
            bar.ShowDropZones(false);
        _itemOwner?.SetDragging(item, false);
        SetDragging(item, false);
        _itemOwner = null;
        Mouse.OverrideCursor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        // Guarda (y avisa al Hub y a las barras) con las listas que quedaron.
        Settings.Update(_ => { });
    }
}
