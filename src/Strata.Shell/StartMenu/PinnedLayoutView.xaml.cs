using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Strata.Shell.Services.StartMenu;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.StartMenu;

/// <summary>
/// Anclados organizados en secciones con nombre y carpetas. Arrastrar una app:
/// al centro de otra crea una carpeta (o entra en ella), a un lado la mueve antes o después,
/// a un hueco o a cualquier celda vacía la deja ahí (su lugar queda vacío y nada se corre).
/// </summary>
/// <remarks>
/// El arrastre se hace capturando el mouse en lugar de DragDrop de OLE: así el ícono sigue
/// al cursor y no depende del bucle de OLE, que cancela en silencio ante cualquier excepción.
/// Dentro del menú el ícono es un adorno de su ventana (se mueve en el mismo cuadro que el resto,
/// sin retraso); fuera de ella, camino a la barra, una ventana emergente.
/// </remarks>
public partial class PinnedLayoutView : UserControl
{
    /// <summary>Lo que se arrastra: un elemento del diseño o una app que sale de una carpeta.</summary>
    private sealed record PinDrag(string? EntryId, string? AppId, string? FromFolderId)
    {
        /// <summary>La app que se arrastra (para anclarla a la barra si se suelta encima).</summary>
        public string? App { get; init; } = AppId;
    }

    private Point _dragStart;
    private object? _dragSource;
    private PinDrag? _drag;
    private FrameworkElement? _hintTile;
    private PinFolderView? _openFolder;
    private GhostAdorner? _ghost;

    public PinnedLayoutView()
    {
        InitializeComponent();
        if (Root.ContextMenu is { } menu)
            menu.Items.Insert(0, StartViewMenu.Create());
        Root.ContextMenuOpening += (_, _) => _menuPoint = Mouse.GetPosition(this);
        // La carpeta se abre sobre el centro del menú: crece o aparece con la animación elegida.
        Services.Themes.Motion.Attach(FolderPopup, () => Services.TaskbarPosition.Bottom);
        MouseMove += (_, e) => { if (_drag != null) DragMove(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, e) => { if (_drag != null) EndDrag(e.GetPosition(this)); };
        // Es un evento que sube: el botón que pierde la captura al empezar no debe cancelar el arrastre.
        LostMouseCapture += (_, e) => { if (ReferenceEquals(e.OriginalSource, this)) CancelDrag(); };
    }

    // ── Arrastrar ───────────────────────────────────────────────────────────

    private void Tile_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragSource = sender;
    }

    private bool ShouldStartDrag(object sender, MouseEventArgs e)
    {
        if (_drag != null || e.LeftButton != MouseButtonState.Pressed || !ReferenceEquals(sender, _dragSource))
            return false;
        var delta = e.GetPosition(this) - _dragStart;
        return Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance
               || Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance;
    }

    private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!ShouldStartDrag(sender, e) || sender is not FrameworkElement tile)
            return;
        _dragSource = null;
        if (EntryIdOf(tile.DataContext) is not { } entryId)
            return;
        var icon = tile.DataContext switch
        {
            StartApp app => app.Icon,
            PinFolderView folder => folder.Preview.FirstOrDefault(),
            _ => null,
        };
        BeginDrag(new PinDrag(entryId, null, null) { App = (tile.DataContext as StartApp)?.Id }, icon, e.GetPosition(this));
        tile.Opacity = 0.4;
    }

    private void FolderApp_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!ShouldStartDrag(sender, e) || sender is not FrameworkElement { DataContext: StartApp app } || _openFolder is null)
            return;
        _dragSource = null;
        var folderId = _openFolder.Id;
        FolderPopup.IsOpen = false; // se cierra para poder soltar sobre el menú
        BeginDrag(new PinDrag(null, app.Id, folderId), app.Icon, Mouse.GetPosition(this));
    }

    private void BeginDrag(PinDrag drag, ImageSource? icon, Point position)
    {
        _drag = drag;
        DragGhostIcon.Source = icon;
        DragGhost.PlacementTarget = this;
        _ghost = GhostAdorner.Attach(this, icon);
        CaptureMouse(); // el botón tenía la captura: así tampoco se dispara su clic al soltar
        // Una fila más en cada sección: se puede soltar debajo de lo que ya hay.
        foreach (var panel in SectionPanels())
        {
            _grown.Add((panel, panel.MinHeight));
            panel.MinHeight = panel.ActualHeight + (CellSize(panel)?.Height ?? 0);
        }
        DragMove(position);
    }

    private readonly List<(ItemsControl Panel, double MinHeight)> _grown = new();

    private void DragMove(Point position)
    {
        // El ícono queda centrado bajo el cursor: en el menú, el adorno; fuera, la ventana emergente.
        if (_ghost != null && _ghost.MoveTo(this, position))
        {
            DragGhost.IsOpen = false;
        }
        else
        {
            double half = (TryFindResource("StartTileIconSize") is double size ? size : 32) / 2 + 8;
            DragGhost.HorizontalOffset = position.X - half;
            DragGhost.VerticalOffset = position.Y - half;
            DragGhost.IsOpen = true;
        }

        var tile = TileAt(position);
        if (!ReferenceEquals(tile, _hintTile))
        {
            if (_hintTile != null)
                SetHint(_hintTile, 1, 0);
            _hintTile = tile;
        }
        if (tile != null)
        {
            // Pista visual: agrandar = crear carpeta; desplazar = hacer hueco a ese lado.
            var zone = ZoneOf(tile, TranslatePoint(position, tile), _drag!);
            SetHint(tile, zone == DropZone.Merge ? 1.12 : 1, zone switch { DropZone.Before => 8, DropZone.After => -8, _ => 0 });
        }

        // Fuera de los mosaicos: se marca la celda vacía donde caería.
        if (tile is null && CellAt(position) is { } cell)
        {
            Canvas.SetLeft(CellHint, cell.Bounds.X);
            Canvas.SetTop(CellHint, cell.Bounds.Y);
            CellHint.Width = cell.Bounds.Width;
            CellHint.Height = cell.Bounds.Height;
            CellHint.Visibility = Visibility.Visible;
        }
        else
        {
            CellHint.Visibility = Visibility.Collapsed;
        }
    }

    private void EndDrag(Point position)
    {
        var drag = _drag!;
        // Soltada sobre la barra de tareas: se ancla ahí (y en Inicio queda donde estaba).
        if (drag.App is { } app && Services.TaskbarPins.DropOnTaskbar(app))
        {
            CancelDrag();
            return;
        }
        var tile = TileAt(position);
        var section = SectionAt(position);
        var cell = tile is null ? CellAt(position) : null;
        var pointInTile = tile != null ? TranslatePoint(position, tile) : default;
        CancelDrag();

        if (tile != null)
            DropOnTile(tile, pointInTile, drag);
        else if (cell != null)
            PinLayout.PlaceAtCell(cell.Value.SectionId, cell.Value.Index, drag.EntryId, drag.FromFolderId, drag.AppId);
        else if (section != null)
            Place(drag, section.Id, int.MaxValue); // en la sección, fuera de la cuadrícula: al final
    }

    // ── Celdas vacías ───────────────────────────────────────────────────────

    /// <summary>
    /// Celda vacía bajo el cursor (después del último elemento de una sección o en la fila nueva
    /// que aparece al arrastrar). Las columnas salen del ancho real del menú y del de un mosaico.
    /// </summary>
    private (string SectionId, int Index, Rect Bounds)? CellAt(Point position)
    {
        if (Ancestor(position, d => d is ItemsControl { DataContext: PinSectionView }) is not ItemsControl { DataContext: PinSectionView section } panel
            || CellSize(panel) is not { } size)
            return null;
        var p = TranslatePoint(position, panel);
        int columns = Math.Max(1, (int)(panel.ActualWidth / size.Width + 0.01));
        int column = (int)Math.Floor(p.X / size.Width);
        if (column < 0 || column >= columns || p.Y < 0)
            return null;

        // Las filas pueden medir distinto (nombres de una o dos líneas): se usan las reales y, debajo, la más alta.
        var rows = RowTops(panel);
        int row = rows.FindLastIndex(top => top <= p.Y);
        double rowTop;
        if (row < 0)
        {
            row = 0;
            rowTop = 0;
        }
        else if (row == rows.Count - 1 && p.Y >= rows[row] + size.Height)
        {
            int extra = (int)Math.Floor((p.Y - rows[row]) / size.Height);
            rowTop = rows[row] + extra * size.Height;
            row += extra;
        }
        else
        {
            rowTop = rows[row];
        }
        int index = row * columns + column;
        if (index < section.Tiles.Count)
            return null; // ahí hay un mosaico (o un hueco, que se atiende como mosaico)
        var origin = panel.TranslatePoint(new Point(column * size.Width, rowTop), this);
        return (section.Id, index, new Rect(origin, size));
    }

    private static List<double> RowTops(ItemsControl panel)
    {
        var tops = new List<double>();
        for (int i = 0; i < panel.Items.Count; i++)
        {
            if (panel.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
            {
                double top = container.TranslatePoint(default, panel).Y;
                if (tops.Count == 0 || top > tops[^1] + 1)
                    tops.Add(top);
            }
        }
        return tops;
    }

    private Size _lastCellSize;

    /// <summary>Tamaño de una celda (el de un mosaico con su margen); en una sección vacía, el último conocido.</summary>
    private Size? CellSize(ItemsControl panel)
    {
        double height = 0, width = 0;
        for (int i = 0; i < panel.Items.Count; i++)
        {
            if (panel.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement { ActualWidth: > 0 } container)
            {
                width = container.ActualWidth;
                height = Math.Max(height, container.ActualHeight);
            }
        }
        if (width > 0)
            _lastCellSize = new Size(width, height);
        return _lastCellSize.Width > 0 ? _lastCellSize : null;
    }

    private IEnumerable<ItemsControl> SectionPanels()
    {
        var stack = new Stack<DependencyObject>([this]);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is ItemsControl { DataContext: PinSectionView } panel)
            {
                yield return panel;
                continue;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    }

    private void CancelDrag()
    {
        if (_drag is null)
            return;
        _drag = null;
        DragGhost.IsOpen = false;
        _ghost?.Detach();
        _ghost = null;
        CellHint.Visibility = Visibility.Collapsed;
        foreach (var (panel, minHeight) in _grown)
            panel.MinHeight = minHeight;
        _grown.Clear();
        if (_hintTile != null)
            SetHint(_hintTile, 1, 0);
        _hintTile = null;
        foreach (var tile in Tiles())
            tile.Opacity = 1;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private static string? EntryIdOf(object? tileData) => tileData switch
    {
        StartApp app => PinLayout.EntryIdOf(app.Id),
        PinFolderView folder => folder.Id,
        PinGapView gap => gap.Id,
        _ => null,
    };

    // ── Destino bajo el cursor ──────────────────────────────────────────────

    private static bool IsTile(DependencyObject d) => d is Button { DataContext: StartApp or PinFolderView or PinGapView };

    private FrameworkElement? TileAt(Point position) => Ancestor(position, IsTile) as FrameworkElement;

    private PinSectionView? SectionAt(Point position) =>
        (Ancestor(position, d => d is ItemsControl { DataContext: PinSectionView }) as ItemsControl)?.DataContext as PinSectionView;

    private DependencyObject? Ancestor(Point position, Func<DependencyObject, bool> match)
    {
        // El contorno de la celda (encima de todo) no cuenta aunque no reciba clics.
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(this,
            d => ReferenceEquals(d, CellHint) ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            r =>
            {
                hit = r.VisualHit;
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(position));
        for (var d = hit; d != null && !ReferenceEquals(d, this); d = VisualTreeHelper.GetParent(d))
            if (match(d))
                return d;
        return null;
    }

    private IEnumerable<FrameworkElement> Tiles()
    {
        var stack = new Stack<DependencyObject>([this]);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (IsTile(d))
                yield return (FrameworkElement)d;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    }

    // ── Soltar ──────────────────────────────────────────────────────────────

    private enum DropZone { Before, After, Merge }

    private static DropZone ZoneOf(FrameworkElement tile, Point p, PinDrag drag)
    {
        // Sobre un hueco, cualquier punto lo ocupa (se muestra como "fusionar": el mosaico crece).
        if (tile.DataContext is PinGapView)
            return DropZone.Merge;
        bool center = p.X > tile.ActualWidth * 0.28 && p.X < tile.ActualWidth * 0.72;
        bool canMerge = drag.FromFolderId != null || (drag.EntryId != null && !IsFolderEntry(drag.EntryId) && !IsGapEntry(drag.EntryId));
        if (center && canMerge && EntryIdOf(tile.DataContext) != drag.EntryId)
            return DropZone.Merge;
        return p.X < tile.ActualWidth / 2 ? DropZone.Before : DropZone.After;
    }

    private static bool IsFolderEntry(string entryId) =>
        PinLayout.Sections.SelectMany(s => s.Tiles).OfType<PinFolderView>().Any(f => f.Id == entryId);

    private static bool IsGapEntry(string entryId) =>
        PinLayout.Sections.SelectMany(s => s.Tiles).OfType<PinGapView>().Any(g => g.Id == entryId);

    private static void DropOnTile(FrameworkElement tile, Point p, PinDrag drag)
    {
        var targetId = EntryIdOf(tile.DataContext);
        if (targetId is null || targetId == drag.EntryId || PinLayout.PositionOf(targetId) is not { } target)
            return;

        if (tile.DataContext is PinGapView)
        {
            PinLayout.DropOnGap(targetId, drag.EntryId, drag.FromFolderId, drag.AppId);
            return;
        }

        var zone = ZoneOf(tile, p, drag);
        if (zone == DropZone.Merge)
        {
            var entryId = drag.EntryId
                          ?? PinLayout.TakeOutOfFolder(drag.FromFolderId!, drag.AppId!, target.SectionId, target.Index + 1);
            if (entryId != null)
                PinLayout.Merge(entryId, targetId);
            return;
        }

        int index = target.Index + (zone == DropZone.After ? 1 : 0);
        Place(drag, target.SectionId, index);
    }

    private static void Place(PinDrag drag, string sectionId, int index)
    {
        if (drag.EntryId != null)
        {
            // Al mover hacia adelante en la misma sección, quitar el elemento corre los índices.
            if (PinLayout.PositionOf(drag.EntryId) is { } source && source.SectionId == sectionId && source.Index < index)
                index--;
            PinLayout.Move(drag.EntryId, sectionId, index);
        }
        else if (drag.FromFolderId != null && drag.AppId != null)
        {
            PinLayout.TakeOutOfFolder(drag.FromFolderId, drag.AppId, sectionId, index);
        }
    }

    private static readonly Duration HintDuration = TimeSpan.FromMilliseconds(130);
    private static readonly IEasingFunction HintEasing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Agranda o corre un mosaico con una animación corta (solo si cambia lo pedido).</summary>
    private static void SetHint(FrameworkElement tile, double scale, double offset)
    {
        // La transformación de la plantilla está congelada (compartida): cada mosaico usa la suya.
        if (tile.RenderTransform is not TransformGroup { IsFrozen: false, Children: [ScaleTransform s, TranslateTransform t] })
        {
            if (scale == 1 && offset == 0)
                return;
            s = new ScaleTransform();
            t = new TranslateTransform();
            tile.RenderTransform = new TransformGroup { Children = { s, t } };
        }
        if (tile.Tag is ValueTuple<double, double> old && old.Item1 == scale && old.Item2 == offset)
            return; // ya va hacia ahí: no reiniciar la animación en cada movimiento
        tile.Tag = (scale, offset);
        var grow = new DoubleAnimation(scale, HintDuration) { EasingFunction = HintEasing };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offset, HintDuration) { EasingFunction = HintEasing });
    }

    /// <summary>
    /// El ícono arrastrado, en la capa de adornos de la ventana del menú (fuera de los recortes de
    /// las listas). La capa fija la transformación del adorno, así que lo que se mueve es su tarjeta.
    /// </summary>
    private sealed class GhostAdorner : Adorner
    {
        private readonly AdornerLayer _layer;
        private readonly Border _card;
        private readonly double _half;
        private readonly TranslateTransform _move = new();

        private GhostAdorner(UIElement root, AdornerLayer layer, ImageSource? icon, double size) : base(root)
        {
            _layer = layer;
            _half = size / 2 + 8;
            IsHitTestVisible = false;
            _card = new Border
            {
                Width = size + 16,
                Height = size + 16,
                Padding = new Thickness(7),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Child = new Image { Source = icon, Width = size, Height = size },
                RenderTransform = _move,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.35 },
            };
            RenderOptions.SetBitmapScalingMode(_card.Child, BitmapScalingMode.HighQuality);
            _card.SetResourceReference(Border.BackgroundProperty, "StrataPopupBackgroundBrush");
            _card.SetResourceReference(Border.BorderBrushProperty, "StrataAccentBrush");
            AddVisualChild(_card);
        }

        public static GhostAdorner? Attach(FrameworkElement view, ImageSource? icon)
        {
            if (Window.GetWindow(view)?.Content is not UIElement root || AdornerLayer.GetAdornerLayer(root) is not { } layer)
                return null;
            double size = view.TryFindResource("StartTileIconSize") is double d ? d : 32;
            var ghost = new GhostAdorner(root, layer, icon, size);
            layer.Add(ghost);
            return ghost;
        }

        /// <summary>Lo pone bajo el puntero; false si el puntero salió de la ventana del menú.</summary>
        public bool MoveTo(Visual view, Point position)
        {
            var element = (FrameworkElement)AdornedElement;
            var p = view.TransformToVisual(element).Transform(position);
            bool inside = p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
            _card.Visibility = inside ? Visibility.Visible : Visibility.Hidden;
            _move.X = p.X - _half;
            _move.Y = p.Y - _half;
            return inside;
        }

        public void Detach() => _layer.Remove(this);

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _card;

        protected override Size MeasureOverride(Size constraint)
        {
            _card.Measure(constraint);
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _card.Arrange(new Rect(_card.DesiredSize));
            return finalSize;
        }
    }

    // ── Carpetas ────────────────────────────────────────────────────────────

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PinFolderView folder })
            OpenFolder(folder);
    }

    private void FolderOpen_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender)?.DataContext is PinFolderView folder)
            OpenFolder(folder);
    }

    private void OpenFolder(PinFolderView folder)
    {
        _openFolder = folder;
        FolderName.Text = folder.Name;
        FolderApps.ItemsSource = folder.Apps;
        FolderPopup.PlacementTarget = this;
        FolderPopup.IsOpen = true;
    }

    private void FolderUngroup_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender)?.DataContext is PinFolderView folder)
            PinLayout.Ungroup(folder.Id);
    }

    private void FolderTakeOut_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender)?.DataContext is not StartApp app || _openFolder is null
            || PinLayout.PositionOf(_openFolder.Id) is not { } position)
            return;
        FolderPopup.IsOpen = false;
        PinLayout.TakeOutOfFolder(_openFolder.Id, app.Id, position.SectionId, position.Index + 1);
    }

    private void FolderName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CommitFolderName();
    }

    private void FolderName_LostFocus(object sender, RoutedEventArgs e) => CommitFolderName();

    private void CommitFolderName()
    {
        if (_openFolder != null && FolderName.Text.Trim() != _openFolder.Name)
            PinLayout.RenameFolder(_openFolder.Id, FolderName.Text);
    }

    // ── Secciones ───────────────────────────────────────────────────────────

    private void SectionHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Grid header)
            BeginRename(header);
    }

    private void SectionRename_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender) is Grid header)
            BeginRename(header);
    }

    private static void BeginRename(Grid header)
    {
        var label = header.Children.OfType<TextBlock>().First();
        var editor = header.Children.OfType<TextBox>().First();
        label.Visibility = Visibility.Collapsed;
        editor.Visibility = Visibility.Visible;
        editor.Focus();
        editor.SelectAll();
    }

    private void SectionEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox editor)
            return;
        if (e.Key == Key.Enter)
            CommitSectionName(editor);
        else if (e.Key == Key.Escape)
        {
            EndRename(editor);
            e.Handled = true; // que Esc no cierre el menú mientras se edita
        }
    }

    private void SectionEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Visibility: Visibility.Visible } editor)
            CommitSectionName(editor);
    }

    private static void CommitSectionName(TextBox editor)
    {
        if (editor.DataContext is PinSectionView section && editor.Text.Trim() != section.Name)
            PinLayout.RenameSection(section.Id, editor.Text);
        else
            EndRename(editor);
    }

    private static void EndRename(TextBox editor)
    {
        if (editor.Parent is Grid header)
            header.Children.OfType<TextBlock>().First().Visibility = Visibility.Visible;
        editor.Visibility = Visibility.Collapsed;
    }

    private void SectionAdd_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender)?.DataContext is PinSectionView section)
            PinLayout.AddSection(Loc.T("Nueva sección"), section.Id);
    }

    private void SectionDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender)?.DataContext is PinSectionView section)
            PinLayout.DeleteSection(section.Id);
    }

    private void NewSection_Click(object sender, RoutedEventArgs e) => PinLayout.AddSection(Loc.T("Nueva sección"));

    private Point _menuPoint;

    /// <summary>Carpeta vacía (luego se le arrastran apps), en la sección donde se hizo clic derecho.</summary>
    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        string id = PinLayout.AddFolder(SectionAt(_menuPoint)?.Id);
        // Se abre para ponerle nombre.
        Dispatcher.BeginInvoke(() =>
        {
            if (PinLayout.Sections.SelectMany(s => s.Tiles).OfType<PinFolderView>().FirstOrDefault(f => f.Id == id) is { } folder)
            {
                OpenFolder(folder);
                FolderName.Focus();
                FolderName.SelectAll();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Elemento sobre el que se abrió el menú contextual de un MenuItem.</summary>
    private static FrameworkElement? MenuTarget(object sender) =>
        (sender as MenuItem)?.Parent is ContextMenu menu ? menu.PlacementTarget as FrameworkElement : null;
}
