using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Winx.Shell.Services.StartMenu;

namespace Winx.Shell.StartMenu;

/// <summary>
/// Anclados organizados en secciones con nombre y carpetas. Arrastrar una app:
/// al centro de otra crea una carpeta (o entra en ella), a un lado la mueve antes o después,
/// a un hueco de una sección la deja al final.
/// </summary>
/// <remarks>
/// El arrastre se hace capturando el mouse en lugar de DragDrop de OLE: así el ícono sigue
/// al cursor y no depende del bucle de OLE, que cancela en silencio ante cualquier excepción.
/// </remarks>
public partial class PinnedLayoutView : UserControl
{
    /// <summary>Lo que se arrastra: un elemento del diseño o una app que sale de una carpeta.</summary>
    private sealed record PinDrag(string? EntryId, string? AppId, string? FromFolderId);

    private Point _dragStart;
    private object? _dragSource;
    private PinDrag? _drag;
    private FrameworkElement? _hintTile;
    private PinFolderView? _openFolder;

    public PinnedLayoutView()
    {
        InitializeComponent();
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
        BeginDrag(new PinDrag(entryId, null, null), icon, e.GetPosition(this));
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
        DragGhost.IsOpen = true;
        CaptureMouse(); // el botón tenía la captura: así tampoco se dispara su clic al soltar
        DragMove(position);
    }

    private void DragMove(Point position)
    {
        // El ícono queda centrado bajo el cursor.
        double half = DragGhostIcon.ActualWidth / 2 + 8;
        DragGhost.HorizontalOffset = position.X - half;
        DragGhost.VerticalOffset = position.Y - half;

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
    }

    private void EndDrag(Point position)
    {
        var drag = _drag!;
        var tile = TileAt(position);
        var section = SectionAt(position);
        var pointInTile = tile != null ? TranslatePoint(position, tile) : default;
        CancelDrag();

        if (tile != null)
            DropOnTile(tile, pointInTile, drag);
        else if (section != null)
            Place(drag, section.Id, int.MaxValue); // en un hueco de la sección: al final
    }

    private void CancelDrag()
    {
        if (_drag is null)
            return;
        _drag = null;
        DragGhost.IsOpen = false;
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
        _ => null,
    };

    // ── Destino bajo el cursor ──────────────────────────────────────────────

    private static bool IsTile(DependencyObject d) => d is Button { DataContext: StartApp or PinFolderView };

    private FrameworkElement? TileAt(Point position) => Ancestor(position, IsTile) as FrameworkElement;

    private PinSectionView? SectionAt(Point position) =>
        (Ancestor(position, d => d is ItemsControl { DataContext: PinSectionView }) as ItemsControl)?.DataContext as PinSectionView;

    private DependencyObject? Ancestor(Point position, Func<DependencyObject, bool> match)
    {
        var hit = VisualTreeHelper.HitTest(this, position)?.VisualHit;
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
        bool center = p.X > tile.ActualWidth * 0.28 && p.X < tile.ActualWidth * 0.72;
        bool canMerge = drag.FromFolderId != null || (drag.EntryId != null && !IsFolderEntry(drag.EntryId));
        if (center && canMerge && EntryIdOf(tile.DataContext) != drag.EntryId)
            return DropZone.Merge;
        return p.X < tile.ActualWidth / 2 ? DropZone.Before : DropZone.After;
    }

    private static bool IsFolderEntry(string entryId) =>
        PinLayout.Sections.SelectMany(s => s.Tiles).OfType<PinFolderView>().Any(f => f.Id == entryId);

    private static void DropOnTile(FrameworkElement tile, Point p, PinDrag drag)
    {
        var targetId = EntryIdOf(tile.DataContext);
        if (targetId is null || targetId == drag.EntryId || PinLayout.PositionOf(targetId) is not { } target)
            return;

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
        s.ScaleX = s.ScaleY = scale;
        t.X = offset;
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
            PinLayout.AddSection("Nueva sección", section.Id);
    }

    private void SectionDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender)?.DataContext is PinSectionView section)
            PinLayout.DeleteSection(section.Id);
    }

    private void NewSection_Click(object sender, RoutedEventArgs e) => PinLayout.AddSection("Nueva sección");

    /// <summary>Elemento sobre el que se abrió el menú contextual de un MenuItem.</summary>
    private static FrameworkElement? MenuTarget(object sender) =>
        (sender as MenuItem)?.Parent is ContextMenu menu ? menu.PlacementTarget as FrameworkElement : null;
}
