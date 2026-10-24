using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;
using Strata.Shell.Services.StartMenu;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;
using Image = System.Windows.Controls.Image;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Strata.Shell.Desktop;

/// <summary>
/// El escritorio de Strata UI en un monitor: una capa transparente pegada al fondo (debajo de las
/// ventanas, encima del fondo de pantalla, dueña de los widgets) con los íconos del Escritorio del
/// usuario y el público (solo en el monitor principal de Strata UI) y su menú contextual, que mezcla
/// el de Strata UI con el del escritorio de Windows (Configuración de pantalla, Personalizar, el panel
/// de la tarjeta de video…). Los íconos se acomodan solos o donde se arrastraron.
/// </summary>
public sealed class DesktopWindow : Window
{
    private const string Marker = "StrataUI.DesktopIcons";

    /// <summary>Un ícono: su archivo, su lugar en la cuadrícula y lo que se dibuja.</summary>
    private sealed class Tile
    {
        public required FileEntry Entry;
        public required Border Element;
        public int Column, Row;
    }

    private readonly string _device;
    private bool _showsIcons;
    private readonly Canvas _canvas = new();
    private readonly Border _marquee;
    private readonly List<Tile> _tiles = [];
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly DispatcherTimer _reload;
    private CancellationTokenSource? _loading;
    private Point? _press;
    private Tile? _pressedTile;
    private bool _marqueeing;
    private TextBox? _renameBox;

    private static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string PublicDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    public DesktopWindow(string device)
    {
        _device = device;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "Strata UI escritorio";
        // Casi invisible (no del todo): con alfa 0 el clic pasaría al escritorio de Windows.
        var hit = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        hit.Freeze();
        Background = hit;
        _marquee = new Border { BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _marquee.SetResourceReference(Border.BorderBrushProperty, "StrataAccentBrush");
        _canvas.Children.Add(_marquee);
        Content = _canvas;
        Focusable = true;
        AllowDrop = true;

        _reload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _reload.Tick += (_, _) =>
        {
            _reload.Stop();
            _ = LoadAsync();
        };
        SourceInitialized += (_, _) => OnSourceInitialized();
        SizeChanged += (_, _) => Arrange();
        PreviewMouseLeftButtonDown += OnLeftDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnLeftUp;
        MouseRightButtonUp += OnRightUp;
        PreviewKeyDown += OnKeyDown;
        DragOver += OnDragOver;
        Drop += OnDrop;
        Closed += (_, _) =>
        {
            _loading?.Cancel();
            StopWatching();
        };
    }

    // ── Ventana al fondo ────────────────────────────────────────────────────

    private void OnSourceInitialized()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Ventana de herramientas: no sale en Alt+Tab ni en la barra.
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExToolWindow);
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        AttachToDesktop();
    }

    /// <summary>La ventana del escritorio de Windows es su dueña: sigue a la vista con Win+D, debajo de todo.</summary>
    public void AttachToDesktop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var desktop = Widgets.WidgetWindow.DesktopWindow();
        if (hwnd != IntPtr.Zero && desktop != IntPtr.Zero)
            SetWindowLongPtr(hwnd, GwlpHwndParent, desktop);
    }

    /// <summary>
    /// Cualquier intento de subirla (al hacer clic) la deja abajo de todo. Y cuando cambia el área
    /// de trabajo (las barras reservan su espacio un momento después de abrirse, o cambian de diseño),
    /// se vuelve a ajustar a ella.
    /// </summary>
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSettingChange && (int)wParam == SpiSetWorkArea)
            Dispatcher.BeginInvoke(() => Place(_showsIcons), DispatcherPriority.Background);
        if (msg == WmWindowPosChanging)
        {
            var pos = Marshal.PtrToStructure<WindowPos>(lParam);
            pos.InsertAfter = HwndBottom;
            pos.Flags &= ~SwpNoZOrder;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return IntPtr.Zero;
    }

    /// <summary>Cubre el área de trabajo de su monitor (sin las barras) y, si es el principal, muestra los íconos.</summary>
    public void Place(bool main)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _device) ?? Forms.Screen.PrimaryScreen!;
        var work = WorkArea(screen.Bounds);
        // En unidades de WPF (si no, WPF le vuelve a poner su tamaño): dos veces, por si al moverla
        // de monitor cambia la escala.
        for (int i = 0; i < 2; i++)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = work.Left / dpi.DpiScaleX;
            Top = work.Top / dpi.DpiScaleY;
            Width = work.Width / dpi.DpiScaleX;
            Height = work.Height / dpi.DpiScaleY;
        }
        SetWindowPos(new WindowInteropHelper(this).Handle, HwndBottom, 0, 0, 0, 0, SwpNoActivate | SwpNoMove | SwpNoSize);
        // Releer los íconos solo la primera vez o si cambió el monitor principal; si no, se reacomodan.
        bool reload = !_placed || _showsIcons != main;
        _placed = true;
        _showsIcons = main;
        if (reload)
            Refresh();
        else
            Arrange();
    }

    private bool _placed;

    /// <summary>
    /// El área de trabajo actual del monitor, leída de Windows: la de <c>Screen.WorkingArea</c> puede
    /// quedar vieja (WinForms la guarda), y al arrancar todavía no incluía lo que reservan las barras.
    /// </summary>
    private static System.Drawing.Rectangle WorkArea(System.Drawing.Rectangle bounds)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 }, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info)
            ? System.Drawing.Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom)
            : bounds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public int Flags;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>Vuelve a leer y acomodar los íconos (cambió una opción del escritorio).</summary>
    public void Refresh()
    {
        if (_showsIcons && Settings.Current.DesktopShowIcons == true)
        {
            Watch();
            _ = LoadAsync();
        }
        else
        {
            _loading?.Cancel();
            StopWatching();
            Clear();
        }
    }

    // ── Íconos ──────────────────────────────────────────────────────────────

    private void Clear()
    {
        foreach (var tile in _tiles)
            _canvas.Children.Remove(tile.Element);
        _tiles.Clear();
        _selected.Clear();
    }

    private async Task LoadAsync()
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        bool hidden = Settings.Current.FilesShowHidden;
        List<FileEntry> entries;
        try
        {
            entries = await Task.Run(() => Read(UserDesktop, hidden).Concat(Read(PublicDesktop, hidden))
                                               .DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList(), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested)
            return;
        FileEntry.ShowExtensions = Settings.Current.FilesShowExtensions;
        Clear();
        foreach (var entry in Sorted(entries))
            _tiles.Add(new Tile { Entry = entry, Element = Visual(entry) });
        foreach (var tile in _tiles)
            _canvas.Children.Add(tile.Element);
        Arrange();
        FileIcons.Load(entries, thumbnails: true, Dispatcher, cts.Token);
    }

    private static List<FileEntry> Read(string folder, bool hidden)
    {
        var list = new List<FileEntry>();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return list;
        try
        {
            foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                var attributes = info.Attributes;
                bool isHidden = (attributes & FileAttributes.Hidden) != 0, isSystem = (attributes & FileAttributes.System) != 0;
                if ((isHidden && isSystem) || (!hidden && (isHidden || isSystem)))
                    continue;
                bool isFolder = info is DirectoryInfo;
                list.Add(new FileEntry(info, FileIcons.TypeName(isFolder ? "" : info.Extension, isFolder)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Escritorio: no se pudo leer {folder} ({ex.Message.Trim()})");
        }
        return list;
    }

    /// <summary>Carpetas primero y luego el orden elegido (nombre en orden natural para desempatar).</summary>
    private static IEnumerable<FileEntry> Sorted(IEnumerable<FileEntry> entries)
    {
        var sort = Settings.Current.DesktopSort;
        var ordered = entries.OrderByDescending(e => e.IsFolder);
        ordered = sort switch
        {
            "Modified" => ordered.ThenByDescending(e => e.Modified),
            "Type" => ordered.ThenBy(e => e.TypeName, StringComparer.CurrentCultureIgnoreCase),
            "Size" => ordered.ThenByDescending(e => e.Size),
            _ => ordered,
        };
        return ordered.ThenBy(e => e.Name, Comparer<string>.Create(StrCmpLogicalW));
    }

    /// <summary>Tamaño de una celda y de su ícono según la opción (como los íconos chicos, medianos y grandes de Windows).</summary>
    private static (double Width, double Height, double Icon) Cell() => Settings.Current.DesktopIconSize switch
    {
        StartIconSize.Small => (78, 70, 32),
        StartIconSize.Large => (112, 128, 72),
        _ => (90, 98, 48),
    };

    private Border Visual(FileEntry entry)
    {
        var (width, height, icon) = Cell();
        var image = new Image { Width = icon, Height = icon, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.SetBinding(Image.SourceProperty, new Binding(nameof(FileEntry.Preview)) { Source = entry });
        var label = new TextBlock
        {
            Text = entry.DisplayName,
            Foreground = Brushes.White,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34,
            Margin = new Thickness(0, 4, 0, 0),
            // Sombra: el nombre se lee sobre cualquier fondo de pantalla.
            Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.95, Color = Colors.Black },
        };
        var border = new Border
        {
            Width = width - 4,
            Height = height - 4,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(4, 6, 4, 2),
            Opacity = entry.IsHidden ? 0.6 : 1,
            ToolTip = entry.FullPath,
            Child = new StackPanel { Children = { image, label } },
            Tag = entry,
        };
        border.MouseEnter += (_, _) => Paint(border);
        border.MouseLeave += (_, _) => Paint(border);
        return border;
    }

    /// <summary>Elegido: teñido con el acento; con el mouse encima: un velo claro.</summary>
    private void Paint(Border element)
    {
        var entry = (FileEntry)element.Tag;
        var accent = (TryFindResource("StrataAccentBrush") as SolidColorBrush)?.Color ?? Colors.CornflowerBlue;
        if (_selected.Contains(entry.FullPath))
        {
            element.Background = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B));
            element.BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, accent.R, accent.G, accent.B));
        }
        else
        {
            element.Background = element.IsMouseOver ? new SolidColorBrush(Color.FromArgb(0x2A, 255, 255, 255)) : Brushes.Transparent;
            element.BorderBrush = Brushes.Transparent;
        }
    }

    private void PaintAll()
    {
        foreach (var tile in _tiles)
            Paint(tile.Element);
    }

    /// <summary>
    /// Acomoda los íconos en la cuadrícula, de arriba abajo y de izquierda a derecha (como Windows).
    /// Sin "organizar automáticamente", cada uno vuelve donde se dejó y los nuevos ocupan lo libre.
    /// </summary>
    private void Arrange()
    {
        if (_tiles.Count == 0 || ActualHeight <= 0)
            return;
        var (width, height, _) = Cell();
        int rows = Math.Max(1, (int)((ActualHeight - 8) / height));
        int columns = Math.Max(1, (int)((ActualWidth - 8) / width));
        var taken = new HashSet<(int, int)>();
        var slots = Settings.Current.DesktopAutoArrange ? null : Settings.Current.DesktopIconSlots;
        var waiting = new List<Tile>();
        foreach (var tile in _tiles)
        {
            if (slots != null && slots.TryGetValue(tile.Entry.FullPath, out var slot) && ParseSlot(slot) is var (column, row)
                && column < columns && row < rows && taken.Add((column, row)))
            {
                (tile.Column, tile.Row) = (column, row);
            }
            else
            {
                waiting.Add(tile);
            }
        }
        int next = 0;
        foreach (var tile in waiting)
        {
            while (taken.Contains((next / rows, next % rows)))
                next++;
            (tile.Column, tile.Row) = (next / rows, next % rows);
            taken.Add((tile.Column, tile.Row));
        }
        foreach (var tile in _tiles)
        {
            Canvas.SetLeft(tile.Element, 8 + tile.Column * width);
            Canvas.SetTop(tile.Element, 8 + tile.Row * height);
        }
    }

    private static (int, int)? ParseSlot(string slot) =>
        slot.Split(',') is [var c, var r] && int.TryParse(c, out int column) && int.TryParse(r, out int row) ? (column, row) : null;

    /// <summary>La celda bajo un punto de la ventana.</summary>
    private (int Column, int Row) CellAt(Point point)
    {
        var (width, height, _) = Cell();
        int rows = Math.Max(1, (int)((ActualHeight - 8) / height));
        int columns = Math.Max(1, (int)((ActualWidth - 8) / width));
        return (Math.Clamp((int)((point.X - 8) / width), 0, columns - 1), Math.Clamp((int)((point.Y - 8) / height), 0, rows - 1));
    }

    /// <summary>Deja esos íconos desde una celda (los siguientes, debajo) y deja de acomodarlos solos.</summary>
    private void PlaceAt(IReadOnlyList<string> paths, (int Column, int Row) start)
    {
        var (_, height, _) = Cell();
        int rows = Math.Max(1, (int)((ActualHeight - 8) / height));
        var moving = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taken = _tiles.Where(t => !moving.Contains(t.Entry.FullPath)).Select(t => (t.Column, t.Row)).ToHashSet();
        int index = start.Column * rows + start.Row;
        var slots = new Dictionary<string, string>(Settings.Current.DesktopIconSlots, StringComparer.OrdinalIgnoreCase);
        // Los que no se mueven quedan donde están ahora.
        foreach (var tile in _tiles.Where(t => !moving.Contains(t.Entry.FullPath)))
            slots[tile.Entry.FullPath] = $"{tile.Column},{tile.Row}";
        foreach (var path in paths)
        {
            while (taken.Contains((index / rows, index % rows)))
                index++;
            slots[path] = $"{index / rows},{index % rows}";
            taken.Add((index / rows, index % rows));
            index++;
        }
        Settings.Update(s =>
        {
            s.DesktopAutoArrange = false;
            s.DesktopIconSlots = slots;
        });
        Arrange();
    }

    private void Watch()
    {
        if (_watchers.Count > 0)
            return;
        foreach (var folder in new[] { UserDesktop, PublicDesktop }.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.LastWrite,
                };
                FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() =>
                {
                    _reload.Stop();
                    _reload.Start();
                });
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Changed += changed;
                watcher.Renamed += (s, e) => changed(s, e);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Sin aviso de cambios: F5 sigue sirviendo.
            }
        }
    }

    private void StopWatching()
    {
        _watchers.ForEach(w => w.Dispose());
        _watchers.Clear();
    }

    // ── Selección y mouse ───────────────────────────────────────────────────

    private Tile? TileAt(object? source)
    {
        for (var node = source as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is Border { Tag: FileEntry } border)
                return _tiles.FirstOrDefault(t => t.Element == border);
            if (node == this)
                break;
        }
        return null;
    }

    private List<FileEntry> Selection => _tiles.Where(t => _selected.Contains(t.Entry.FullPath)).Select(t => t.Entry).ToList();

    private void Select(IEnumerable<FileEntry> entries, bool add = false)
    {
        if (!add)
            _selected.Clear();
        foreach (var entry in entries)
            _selected.Add(entry.FullPath);
        PaintAll();
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (_renameBox != null && e.OriginalSource is DependencyObject d && IsInside(d, _renameBox))
            return;
        Activate();
        Keyboard.Focus(this);
        var tile = TileAt(e.OriginalSource);
        if (tile != null && e.ClickCount == 2)
        {
            Launch(tile.Entry);
            e.Handled = true;
            return;
        }
        _press = e.GetPosition(this);
        _pressedTile = tile;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (tile != null)
        {
            if (ctrl)
            {
                if (!_selected.Remove(tile.Entry.FullPath))
                    _selected.Add(tile.Entry.FullPath);
                PaintAll();
            }
            else if (!_selected.Contains(tile.Entry.FullPath))
            {
                Select([tile.Entry]);
            }
        }
        else
        {
            if (!ctrl)
                Select([]);
            _marqueeing = true;
        }
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_press is not { } start || e.LeftButton != MouseButtonState.Pressed)
            return;
        var point = e.GetPosition(this);
        if (_marqueeing)
        {
            var area = new Rect(start, point);
            Canvas.SetLeft(_marquee, area.X);
            Canvas.SetTop(_marquee, area.Y);
            _marquee.Width = area.Width;
            _marquee.Height = area.Height;
            var accent = (TryFindResource("StrataAccentBrush") as SolidColorBrush)?.Color ?? Colors.CornflowerBlue;
            _marquee.Background = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));
            _marquee.Visibility = Visibility.Visible;
            Select(_tiles.Where(t => area.IntersectsWith(new Rect(Canvas.GetLeft(t.Element), Canvas.GetTop(t.Element),
                                                                  t.Element.Width, t.Element.Height))).Select(t => t.Entry),
                   add: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            return;
        }
        if (_pressedTile is null || (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
                                     && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance))
            return;
        // Arrastrar: dentro del escritorio los acomoda; afuera (Archivos, otra app) son archivos.
        ReleaseMouseCapture();
        _press = null;
        _pressedTile = null;
        var paths = Selection.Select(s => s.FullPath).ToArray();
        var data = new DataObject(DataFormats.FileDrop, paths);
        data.SetData(Marker, _device);
        try
        {
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch (Exception ex)
        {
            Log.Error("Escritorio: falló el arrastre", ex);
        }
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        // Un clic (sin arrastrar) sobre uno de varios elegidos deja solo ese.
        if (_pressedTile is { } tile && (Keyboard.Modifiers & ModifierKeys.Control) == 0 && _selected.Count > 1)
            Select([tile.Entry]);
        _press = null;
        _pressedTile = null;
        _marqueeing = false;
        _marquee.Visibility = Visibility.Collapsed;
        ReleaseMouseCapture();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        try
        {
            e.Effects = e.Data.GetDataPresent(Marker) ? DragDropEffects.Move
                : e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0
                    ? ((e.KeyStates & DragDropKeyStates.ControlKey) != 0
                       || !files.All(f => string.Equals(Path.GetPathRoot(f), Path.GetPathRoot(UserDesktop), StringComparison.OrdinalIgnoreCase))
                        ? DragDropEffects.Copy : DragDropEffects.Move) & e.AllowedEffects
                    : DragDropEffects.None;
        }
        catch (Exception ex)
        {
            Log.Error("Escritorio: falló el arrastre", ex);
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            var cell = CellAt(e.GetPosition(this));
            if (e.Data.GetDataPresent(Marker) && e.Data.GetData(Marker) as string == _device)
            {
                PlaceAt(Selection.Select(s => s.FullPath).ToList(), cell);
                return;
            }
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
                return;
            var handle = new WindowInteropHelper(this).Handle;
            bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0
                        || !files.All(f => string.Equals(Path.GetPathRoot(f), Path.GetPathRoot(UserDesktop), StringComparison.OrdinalIgnoreCase));
            var outside = files.Where(f => !string.Equals(Path.GetDirectoryName(f), UserDesktop, StringComparison.OrdinalIgnoreCase)).ToList();
            if (outside.Count > 0 && !(copy ? await FileOperations.CopyAsync(outside, UserDesktop, handle)
                                            : await FileOperations.MoveAsync(outside, UserDesktop, handle)))
                return;
            // Quedan donde se soltaron (con el nombre que tienen en el Escritorio).
            if (!Settings.Current.DesktopAutoArrange)
                PlaceAt(files.Select(f => Path.Combine(UserDesktop, Path.GetFileName(f))).ToList(), cell);
            _reload.Stop();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Escritorio: falló al soltar", ex);
        }
    }

    private static bool IsInside(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current != null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current == ancestor)
                return true;
        }
        return false;
    }

    // ── Abrir ───────────────────────────────────────────────────────────────

    /// <summary>Como en Archivos: carpetas en Archivos, medios en el visor, comprimidos con el compresor y lo demás con su app.</summary>
    private void Launch(FileEntry entry)
    {
        if (entry.IsFolder)
        {
            StartMenuService.OpenPath(entry.FullPath);
            return;
        }
        if (entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            && ExtraApps.ShortcutTarget(entry.FullPath) is { Length: > 0 } target && Directory.Exists(target))
        {
            StartMenuService.OpenPath(target);
            return;
        }
        // Fotos, videos y música: el visor del módulo Archivos (sin él, la app predeterminada).
        if (MediaKinds.Of(entry.FullPath) != MediaKind.None && OptionalModules.Files.Load() is { } files)
        {
            var media = _tiles.Select(t => t.Entry).Where(e => !e.IsFolder && MediaKinds.Of(e.FullPath) != MediaKind.None)
                              .Select(e => e.FullPath).ToList();
            files.OpenMedia(media, media.IndexOf(entry.FullPath));
            return;
        }
        if (Archivers.IsArchive(entry.FullPath) && Archivers.Open(entry.FullPath))
            return;
        FileOperations.Open(entry.FullPath);
    }

    // ── Teclado ─────────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_renameBox != null)
            return;
        var mods = Keyboard.Modifiers;
        var selection = Selection;
        bool handled = true;
        switch (e.Key)
        {
            case Key.Enter when selection.Count > 0:
                selection.ForEach(Launch);
                break;
            case Key.F2 when selection.Count == 1:
                StartRename(selection[0]);
                break;
            case Key.Delete when selection.Count > 0:
                _ = Recycle(selection);
                break;
            case Key.A when mods == ModifierKeys.Control:
                Select(_tiles.Select(t => t.Entry));
                break;
            case Key.C when mods == ModifierKeys.Control && selection.Count > 0:
                FileOperations.SetClipboard(selection.Select(s => s.FullPath).ToList(), cut: false);
                break;
            case Key.X when mods == ModifierKeys.Control && selection.Count > 0:
                FileOperations.SetClipboard(selection.Select(s => s.FullPath).ToList(), cut: true);
                break;
            case Key.V when mods == ModifierKeys.Control:
                _ = Paste();
                break;
            case Key.F5:
                _ = LoadAsync();
                break;
            case Key.Escape:
                Select([]);
                break;
            default:
                handled = false;
                break;
        }
        e.Handled = handled;
    }

    private async Task Recycle(IReadOnlyList<FileEntry> entries)
    {
        await FileOperations.RecycleAsync(entries.Select(s => s.FullPath).ToList(), new WindowInteropHelper(this).Handle);
        _ = LoadAsync();
    }

    private async Task Paste()
    {
        if (FileOperations.ReadClipboard() is not { } clip)
            return;
        var handle = new WindowInteropHelper(this).Handle;
        if (clip.Cut)
            await FileOperations.MoveAsync(clip.Paths, UserDesktop, handle);
        else
            await FileOperations.CopyAsync(clip.Paths, UserDesktop, handle);
        _ = LoadAsync();
    }

    // ── Cambiar nombre ──────────────────────────────────────────────────────

    private void StartRename(FileEntry entry)
    {
        if (_tiles.FirstOrDefault(t => t.Entry == entry) is not { } tile)
            return;
        var (width, _, icon) = Cell();
        var box = new TextBox
        {
            Text = entry.DisplayName,
            Width = width - 4,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(2),
        };
        Canvas.SetLeft(box, Canvas.GetLeft(tile.Element));
        Canvas.SetTop(box, Canvas.GetTop(tile.Element) + icon + 8);
        _canvas.Children.Add(box);
        _renameBox = box;
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                EndRename(entry, e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => EndRename(entry, true);
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            int length = entry.IsFolder || entry.DisplayName != entry.Name || entry.Extension.Length == 0
                ? entry.DisplayName.Length
                : entry.Name.Length - entry.Extension.Length;
            box.Select(0, length);
        }, DispatcherPriority.Input);
    }

    private void EndRename(FileEntry entry, bool apply)
    {
        if (_renameBox is not { } box)
            return;
        _renameBox = null;
        _canvas.Children.Remove(box);
        Keyboard.Focus(this);
        string name = box.Text.Trim();
        if (name.Length > 0 && entry.DisplayName != entry.Name)
            name += entry.Extension;
        if (!apply || name.Length == 0 || name == entry.Name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return;
        if (FileOperations.Rename(entry.FullPath, name, entry.IsFolder) is { } error)
        {
            Log.Info($"Escritorio: no se pudo cambiar el nombre ({error})");
            return;
        }
        // Conserva su lugar con el nombre nuevo.
        string renamed = Path.Combine(Path.GetDirectoryName(entry.FullPath)!, name);
        if (Settings.Current.DesktopIconSlots.TryGetValue(entry.FullPath, out var slot))
            Settings.Update(s =>
            {
                s.DesktopIconSlots.Remove(entry.FullPath);
                s.DesktopIconSlots[renamed] = slot;
            });
        _selected.Clear();
        _selected.Add(renamed);
        _ = LoadAsync().ContinueWith(_ => Dispatcher.BeginInvoke(PaintAll));
    }

    // ── Menú contextual ─────────────────────────────────────────────────────

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        Activate();
        var tile = TileAt(e.OriginalSource);
        if (tile != null && !_selected.Contains(tile.Entry.FullPath))
            Select([tile.Entry]);
        else if (tile is null)
            Select([]);
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = this };
        if (tile != null)
            FillItemMenu(menu, Selection);
        else
            FillDesktopMenu(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void FillItemMenu(ContextMenu menu, List<FileEntry> selection)
    {
        var first = selection[0];
        menu.Items.Add(FileMenus.Item(Loc.T("Abrir"), SymbolRegular.Open24, () => selection.ForEach(Launch), bold: true));
        bool strataFiles = OptionalModules.Files.IsInstalled;
        menu.Items.Add(FileMenus.Item(first.IsFolder
                                          ? strataFiles ? Loc.T("Abrir en Archivos") : Loc.T("Abrir en el Explorador")
                                          : strataFiles ? Loc.T("Mostrar en Archivos") : Loc.T("Mostrar en el Explorador"),
                                      SymbolRegular.FolderOpen24,
                                      () =>
                                      {
                                          if (strataFiles || first.IsFolder)
                                              App.Current.OpenFiles(first.FullPath);
                                          else
                                              StartMenuService.OpenPath(first.FullPath, select: true);
                                      }));
        if (selection.Count == 1 && Archivers.IsArchive(first.FullPath))
            menu.Items.Add(FileMenus.Item(Loc.T("Extraer en una carpeta"), SymbolRegular.FolderZip24, () => _ = Archivers.ExtractAsync(first.FullPath)));
        menu.Items.Add(new Separator());
        menu.Items.Add(FileMenus.Item(Loc.T("Cortar"), SymbolRegular.Cut24,
                                       () => FileOperations.SetClipboard(selection.Select(s => s.FullPath).ToList(), cut: true)));
        menu.Items.Add(FileMenus.Item(Loc.T("Copiar"), SymbolRegular.Copy24,
                                       () => FileOperations.SetClipboard(selection.Select(s => s.FullPath).ToList(), cut: false)));
        menu.Items.Add(FileMenus.Item(Loc.T("Copiar ruta"), SymbolRegular.DocumentCopy24, () =>
            Clipboard.SetText(string.Join(Environment.NewLine, selection.Select(s => $"\"{s.FullPath}\"")))));
        if (selection.Count == 1)
        {
            bool inStart = StartMenuService.IsPinned(first.FullPath);
            menu.Items.Add(FileMenus.Item(inStart ? Loc.T("Desanclar de Inicio") : Loc.T("Anclar a Inicio"),
                                           inStart ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, () =>
                                           {
                                               if (inStart)
                                                   StartMenuService.UnpinPath(first.FullPath);
                                               else
                                                   StartMenuService.PinPaths([first.FullPath]);
                                           }));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(FileMenus.Item(Loc.T("Cambiar nombre"), SymbolRegular.Rename24, () => StartRename(first), enabled: selection.Count == 1));
        menu.Items.Add(FileMenus.Item(Loc.T("Enviar a la Papelera"), SymbolRegular.Delete24, () => _ = Recycle(selection)));
        FileMenus.AddWindowsItems(menu, selection.Select(s => s.FullPath).ToList(), background: false);
    }

    private void FillDesktopMenu(ContextMenu menu)
    {
        var s = Settings.Current;
        // Vista: tamaño, organizar y mostrar u ocultar los íconos.
        var view = new MenuItem { Header = Loc.T("Vista"), Icon = new SymbolIcon { Symbol = SymbolRegular.Grid24 } };
        foreach (var (size, text) in new[] { (StartIconSize.Large, Loc.T("Íconos grandes")), (StartIconSize.Medium, Loc.T("Íconos medianos")),
                                             (StartIconSize.Small, Loc.T("Íconos pequeños")) })
        {
            var item = FileMenus.Item(text, null, () => Settings.Update(x => x.DesktopIconSize = size));
            item.IsChecked = s.DesktopIconSize == size;
            view.Items.Add(item);
        }
        view.Items.Add(new Separator());
        var auto = FileMenus.Item(Loc.T("Organizar automáticamente"), null, () => Settings.Update(x => x.DesktopAutoArrange = !x.DesktopAutoArrange));
        auto.IsChecked = s.DesktopAutoArrange;
        view.Items.Add(auto);
        view.Items.Add(new Separator());
        var show = FileMenus.Item(Loc.T("Mostrar íconos del escritorio"), null, () => Settings.Update(x => x.DesktopShowIcons = x.DesktopShowIcons != true));
        show.IsChecked = s.DesktopShowIcons == true;
        view.Items.Add(show);
        menu.Items.Add(view);

        var sort = new MenuItem { Header = Loc.T("Ordenar por"), Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSort24 } };
        foreach (var (key, text) in new[] { ("Name", Loc.T("Nombre")), ("Size", Loc.T("Tamaño")), ("Type", Loc.T("Tipo")),
                                            ("Modified", Loc.T("Fecha de modificación")) })
        {
            // Ordenar los vuelve a acomodar solos, en ese orden.
            var item = FileMenus.Item(text, null, () => Settings.Update(x =>
            {
                x.DesktopSort = key;
                x.DesktopAutoArrange = true;
            }));
            item.IsChecked = s.DesktopSort == key;
            sort.Items.Add(item);
        }
        menu.Items.Add(sort);
        menu.Items.Add(FileMenus.Item(Loc.T("Actualizar"), SymbolRegular.ArrowClockwise24, () => _ = LoadAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(FileMenus.Item(Loc.T("Pegar"), SymbolRegular.ClipboardPaste24, () => _ = Paste(), enabled: FileOperations.ClipboardHasFiles()));
        menu.Items.Add(new Separator());

        // Widgets: agregar uno y el modo edición.
        var widgets = new MenuItem { Header = Loc.T("Agregar un widget"), Icon = new SymbolIcon { Symbol = SymbolRegular.Apps24 } };
        foreach (var entry in EditMode.WidgetCatalog.All())
        {
            widgets.Items.Add(FileMenus.Item(entry.Name, entry.Icon, () =>
            {
                var config = entry.Create();
                Services.Widgets.WidgetManager.Add(config.Kind, config.ShowDate, config.Module);
            }));
        }
        menu.Items.Add(widgets);
        menu.Items.Add(FileMenus.Item(Loc.T("Editar diseño…"), SymbolRegular.Edit24, () => EditMode.EditSession.Start()));
        menu.Items.Add(new Separator());
        menu.Items.Add(FileMenus.Item(OptionalModules.Files.IsInstalled ? Loc.T("Abrir en Archivos") : Loc.T("Abrir en el Explorador"),
                                      SymbolRegular.FolderOpen24, () => App.Current.OpenFiles(UserDesktop)));
        menu.Items.Add(FileMenus.Item(Loc.T("Abrir terminal aquí"), SymbolRegular.WindowConsole20, () => FileOperations.OpenTerminal(UserDesktop)));
        menu.Items.Add(new Separator());
        var personalize = new MenuItem { Header = Loc.T("Personalizar Strata UI"), Icon = new SymbolIcon { Symbol = SymbolRegular.PaintBrush24 } };
        personalize.Items.Add(FileMenus.Item(Loc.T("Fondo de pantalla"), SymbolRegular.Image24, () => FileOperations.Open("ms-settings:personalization-background")));
        personalize.Items.Add(FileMenus.Item(Loc.T("Tema y colores"), SymbolRegular.PaintBrush24, () => App.Current.OpenHub("Appearance")));
        personalize.Items.Add(FileMenus.Item(Loc.T("Configuración de Strata UI"), SymbolRegular.Settings24, () => App.Current.OpenHub()));
        menu.Items.Add(personalize);
        // Lo del escritorio de Windows: Nuevo, Configuración de pantalla, Personalizar, el panel de la tarjeta de video…
        FileMenus.AddWindowsItems(menu, [UserDesktop], background: true, desktop: true);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string? a, string? b);

    private const int GwlExStyle = -20, GwlpHwndParent = -8, WsExToolWindow = 0x80, WmWindowPosChanging = 0x46,
                      WmSettingChange = 0x1A, SpiSetWorkArea = 0x2F;
    private const uint SwpNoActivate = 0x10, SwpNoZOrder = 0x4, SwpNoMove = 0x2, SwpNoSize = 0x1;
    private static readonly IntPtr HwndBottom = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
