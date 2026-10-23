using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.StartMenu;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Strata.Shell.Files;

/// <summary>
/// Explorador de archivos de Strata UI: portada (Este equipo) con carpetas, unidades y recientes, y
/// cada carpeta en lista o cuadrícula. Lee con System.IO y opera con el motor de Windows
/// (<see cref="FileOperations"/>); no carga extensiones del Shell (ver <see cref="FileIcons"/>).
/// Se abre con <c>StrataUI.exe --files [ruta]</c>, desde el pie del menú Inicio o la bandeja.
/// </summary>
public partial class FilesWindow : FluentWindow
{
    private enum SortKey { Name, Modified, Type, Size }

    /// <summary>Un tramo de la ruta de navegación; <see cref="Path"/> null es Este equipo.</summary>
    private sealed record Crumb(string Name, string? Path, bool IsHome, bool HasChevron);

    private readonly List<string?> _back = [], _forward = [];
    private bool _started;
    private string? _path;
    private ObservableCollection<FileEntry> _entries = [];
    private ListCollectionView? _view;
    private CancellationTokenSource? _loading;
    private FileSystemWatcher? _watcher;
    private string? _watchedPath;
    private readonly DispatcherTimer _reload;
    private readonly DispatcherTimer _messageTimer;
    private SortKey _sort = SortKey.Name;
    private bool _descending;
    private readonly Place _home;
    private readonly List<Place> _folders;
    private List<Place> _drives = [], _cloud = [], _network = [];
    private DateTime _drivesRead;
    private HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectAfterLoad;
    private bool _renameAfterLoad;
    private Point? _dragStart;
    private FileEntry? _clickedSelected;

    public FilesWindow(string? path = null)
    {
        InitializeComponent();
        FluentBackdrop.Follow(this);
        SourceInitialized += (_, _) => Controls.FilesIcon.ApplyAppId(new WindowInteropHelper(this).Handle);
        _home = new Place(Loc.T("Este equipo"), null, SymbolRegular.Home24);
        _folders = Places.Folders();
        SideFolders.ItemsSource = new[] { _home }.Concat(_folders).ToList();
        HomeFolders.ItemsSource = _folders;

        _reload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _reload.Tick += (_, _) =>
        {
            _reload.Stop();
            _ = LoadAsync(merge: true);
        };
        _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            MessageText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        };

        BuildHeader();
        HiddenToggle.IsChecked = Settings.Current.FilesShowHidden;
        FileEntry.ShowExtensions = Settings.Current.FilesShowExtensions;
        ExtensionsToggle.IsChecked = FileEntry.ShowExtensions;
        ApplyView();
        foreach (var list in new[] { DetailsList, GridList })
        {
            list.MouseDoubleClick += List_MouseDoubleClick;
            list.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
            list.PreviewMouseLeftButtonUp += List_PreviewMouseLeftButtonUp;
            list.PreviewMouseMove += List_PreviewMouseMove;
            list.SelectionChanged += (_, _) => UpdateStatus();
            // Un menú vacío para que llegue ContextMenuOpening; se arma al abrirse.
            list.ContextMenu = new ContextMenu();
            list.ContextMenuOpening += List_ContextMenuOpening;
        }
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseDown += (_, e) =>
        {
            // Botones laterales del mouse: atrás y adelante.
            if (e.ChangedButton == MouseButton.XButton1)
                GoBack();
            else if (e.ChangedButton == MouseButton.XButton2)
                GoForward();
        };
        Activated += (_, _) =>
        {
            UpdateCommands();
            if (DateTime.UtcNow - _drivesRead > TimeSpan.FromSeconds(10))
                _ = LoadDrivesAsync();
        };
        Closed += (_, _) =>
        {
            _loading?.Cancel();
            _watcher?.Dispose();
            _reload.Stop();
            MemoryTrim.Schedule(TimeSpan.FromSeconds(3));
        };
        Loaded += (_, _) => Motion.In(Root, Root, TaskbarPosition.Bottom, 0.8);
        // Un archivo: su carpeta, con él seleccionado (como "Mostrar en la carpeta").
        if (path != null && File.Exists(path))
        {
            _selectAfterLoad = System.IO.Path.GetFullPath(path);
            path = System.IO.Path.GetDirectoryName(_selectAfterLoad);
        }
        Navigate(path);
    }

    // ── Navegación ──────────────────────────────────────────────────────────

    public void Navigate(string? path, bool record = true)
    {
        path = Normalize(path);
        if (_started && record && !SamePath(path, _path))
        {
            _back.Add(_path);
            _forward.Clear();
        }
        _started = true;
        _path = path;
        SearchBox.Text = "";
        _ = LoadAsync(merge: false);
    }

    private void GoBack()
    {
        if (_back.Count == 0)
            return;
        _forward.Add(_path);
        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Navigate(target, record: false);
    }

    private void GoForward()
    {
        if (_forward.Count == 0)
            return;
        _back.Add(_path);
        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        Navigate(target, record: false);
    }

    private void GoUp()
    {
        if (_path is null)
            return;
        // Al subir queda marcada la carpeta de la que se venía, como en el Explorador.
        _selectAfterLoad = _path;
        // De "\\servidor\recurso" se sube a "\\servidor" (sus carpetas compartidas).
        if (Places.ShareRoot(_path) is { } share && SamePath(share, _path))
        {
            Navigate(share[..share.LastIndexOf('\\')]);
            return;
        }
        Navigate(Places.IsServer(_path) ? null
                 : Directory.GetParent(_path.TrimEnd('\\'))?.FullName is { } parent && !SamePath(parent, _path) ? parent : null);
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).TrimEnd('\\');
        if (Places.IsServer(path))
            return path;
        try
        {
            path = System.IO.Path.GetFullPath(path);
            // "C:" es la carpeta actual de esa unidad; la raíz es "C:\".
            return System.IO.Path.GetPathRoot(path) is { } root && path.Length <= root.Length ? root.TrimEnd('\\') + "\\" : path.TrimEnd('\\');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool SamePath(string? a, string? b) =>
        string.Equals(a?.TrimEnd('\\'), b?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // ── Carga ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lee la carpeta actual. Al navegar se reemplaza todo; con <paramref name="merge"/> (cambios en
    /// la carpeta, F5, ocultos) solo entra y sale lo que cambió: no se pierde el lugar ni la selección.
    /// </summary>
    private async Task LoadAsync(bool merge)
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        string? path = _path;
        UpdateChrome();
        Watch(path);
        if (path is null)
        {
            ShowHome(animate: !merge);
            return;
        }

        bool hidden = Settings.Current.FilesShowHidden;
        bool inCloud = CloudPlace(path) != null;
        RetryButton.Visibility = Visibility.Collapsed;
        List<FileEntry> fresh;
        try
        {
            var read = Task.Run(() => Places.IsServer(path) ? ReadServer(path) : Read(path, hidden, inCloud), cts.Token);
            // Una carpeta de red sin conexión puede tardar mucho en fallar: se avisa y se deja de esperar.
            if (await Task.WhenAny(read, Task.Delay(400, cts.Token)) != read && !merge && !cts.IsCancellationRequested)
                ShowConnecting(path);
            if (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10), cts.Token)) != read)
            {
                if (!cts.IsCancellationRequested)
                    ShowNoResponse(path);
                return;
            }
            fresh = await read;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (!cts.IsCancellationRequested)
                ShowError(Loc.T("No tienes permiso para abrir esta carpeta."));
            return;
        }
        catch (Exception ex) when (ex is IOException)
        {
            if (!cts.IsCancellationRequested)
                ShowError(ex is DirectoryNotFoundException ? Loc.T("Esta carpeta ya no existe.") : ex.Message);
            return;
        }
        if (cts.IsCancellationRequested)
            return;

        List<FileEntry> toLoad;
        if (merge && _view != null)
        {
            toLoad = Merge(fresh);
        }
        else
        {
            _entries = new ObservableCollection<FileEntry>(fresh);
            _view = new ListCollectionView(_entries) { CustomSort = new EntryComparer(_sort, _descending), Filter = Matches };
            DetailsList.ItemsSource = _view;
            GridList.ItemsSource = _view;
            toLoad = fresh;
        }
        foreach (var entry in toLoad)
            entry.IsCut = _cutPaths.Contains(entry.FullPath);

        ShowFolder(animate: !merge);
        if (_selectAfterLoad is { } select && _entries.FirstOrDefault(e => SamePath(e.FullPath, select)) is { } target)
        {
            ActiveList.SelectedItem = target;
            ActiveList.ScrollIntoView(target);
            if (_renameAfterLoad)
                _ = Dispatcher.BeginInvoke(() => StartRename(target), DispatcherPriority.Loaded);
        }
        if (_selectAfterLoad != null || !merge)
        {
            _selectAfterLoad = null;
            _renameAfterLoad = false;
        }
        UpdateStatus();
        FileIcons.Load(toLoad, Settings.Current.FilesGridView && Thumbnails(path), Dispatcher, cts.Token);
        RememberNetwork(path);
    }

    /// <summary>Las carpetas compartidas de un servidor de la red.</summary>
    private static List<FileEntry> ReadServer(string server)
    {
        string type = Loc.T("Carpeta compartida");
        return Places.Shares(server).Select(share => new FileEntry(share, server + "\\" + share, type)).ToList();
    }

    /// <summary>La carpeta de la nube que contiene esta ruta (null si no es de la nube).</summary>
    private Place? CloudPlace(string? path) =>
        path is null ? null
        : _cloud.FirstOrDefault(p => p.Path is { } root && (SamePath(root, path)
                                     || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Sin miniaturas en Google Drive (su unidad descarga lo que se lee). En las demás nubes, los
    /// archivos solo en línea ya se saltan uno por uno (<see cref="FileIcons.IsPicture"/>).
    /// </summary>
    private bool Thumbnails(string path) =>
        CloudPlace(path) is not { } cloud || System.IO.Path.GetPathRoot(cloud.Path!)?.TrimEnd('\\') != cloud.Path!.TrimEnd('\\');

    /// <summary>Una carpeta de red que se pudo abrir queda en la barra lateral (su "\\servidor\recurso").</summary>
    private void RememberNetwork(string path)
    {
        if (Places.ShareRoot(path) is not { } share || Settings.Current.FilesNetworkPlaces.Contains(share, StringComparer.OrdinalIgnoreCase))
            return;
        Settings.Update(s => s.FilesNetworkPlaces = s.FilesNetworkPlaces.Prepend(share).Take(10).ToList());
        _ = LoadDrivesAsync();
    }

    private void ForgetNetwork(Place place) =>
        Settings.Update(s => s.FilesNetworkPlaces = s.FilesNetworkPlaces.Where(p => !SamePath(p, place.Path)).ToList());

    private static List<FileEntry> Read(string path, bool hidden, bool inCloud)
    {
        var folder = new DirectoryInfo(path);
        if (!folder.Exists)
            throw new DirectoryNotFoundException(path);
        var list = new List<FileEntry>();
        foreach (var info in folder.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, ReturnSpecialDirectories = false }))
        {
            var attributes = info.Attributes;
            bool isHidden = (attributes & FileAttributes.Hidden) != 0, isSystem = (attributes & FileAttributes.System) != 0;
            // Los protegidos del sistema (ocultos y de sistema) nunca; los ocultos, si se pidieron.
            if ((isHidden && isSystem) || (!hidden && (isHidden || isSystem)))
                continue;
            bool isFolder = info is DirectoryInfo;
            list.Add(new FileEntry(info, FileIcons.TypeName(isFolder ? "" : info.Extension, isFolder), inCloud));
        }
        return list;
    }

    /// <summary>Saca lo que ya no está o cambió y agrega lo nuevo; devuelve lo agregado (sin ícono aún).</summary>
    private List<FileEntry> Merge(List<FileEntry> fresh)
    {
        static bool Same(FileEntry a, FileEntry b) =>
            a.Modified == b.Modified && a.Size == b.Size && a.Attributes == b.Attributes && a.Name == b.Name;
        var selected = SelectedEntries.Select(e => e.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = _entries.ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);
        var incoming = fresh.ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries.ToList())
        {
            if (!incoming.TryGetValue(entry.FullPath, out var updated) || !Same(entry, updated))
                _entries.Remove(entry);
        }
        var added = new List<FileEntry>();
        foreach (var entry in fresh)
        {
            if (current.TryGetValue(entry.FullPath, out var old) && Same(old, entry))
                continue;
            _entries.Add(entry);
            added.Add(entry);
            if (selected.Contains(entry.FullPath))
                ActiveList.SelectedItems.Add(entry);
        }
        return added;
    }

    private bool Matches(object item) =>
        SearchBox.Text is not { Length: > 0 } query
        || (item is FileEntry entry && entry.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));

    private void Watch(string? path)
    {
        if (SamePath(path, _watchedPath) && (_watcher != null || path is null))
            return;
        _watcher?.Dispose();
        _watcher = null;
        _watchedPath = path;
        if (path is null || Places.IsServer(path))
            return;
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
                               | NotifyFilters.LastWrite | NotifyFilters.Attributes,
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
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Sin aviso de cambios (algunas unidades de red): F5 sigue sirviendo.
        }
    }

    private async Task LoadDrivesAsync()
    {
        _drivesRead = DateTime.UtcNow;
        var remembered = Settings.Current.FilesNetworkPlaces.ToList();
        StorageView storage;
        try
        {
            storage = await FileIcons.RunAsync(() => Places.Storage(remembered));
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: no se pudieron leer las unidades", ex);
            return;
        }
        (_drives, _cloud, _network) = (storage.Drives, storage.Cloud, storage.Network);
        SideDrives.ItemsSource = HomeDrives.ItemsSource = _drives;
        SideCloud.ItemsSource = HomeCloud.ItemsSource = _cloud;
        SideNetwork.ItemsSource = HomeNetwork.ItemsSource = _network;
        var cloudVisible = _cloud.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SideCloudTitle.Visibility = HomeCloudTitle.Visibility = cloudVisible;
        HomeNetworkTitle.Visibility = HomeNetwork.Visibility = _network.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCurrentPlace();
    }

    // ── Lo que se ve ────────────────────────────────────────────────────────

    private ListBox ActiveList => Settings.Current.FilesGridView ? GridList : DetailsList;

    private List<FileEntry> SelectedEntries => ActiveList.SelectedItems.OfType<FileEntry>().ToList();

    /// <summary>Título, ruta, botones y la entrada marcada de la barra lateral.</summary>
    private void UpdateChrome()
    {
        string name = _path is null ? _home.Name : Places.NameOf(_path);
        Title = name;
        WindowTitleBar.Title = name;
        BackButton.IsEnabled = _back.Count > 0;
        ForwardButton.IsEnabled = _forward.Count > 0;
        UpButton.IsEnabled = _path != null;
        SearchBox.IsEnabled = _path != null;
        SearchBox.PlaceholderText = _path is null ? Loc.T("Buscar") : Loc.F("Buscar en {0}", name);

        var crumbs = new List<Crumb> { new(_home.Name, null, true, false) };
        if (_path != null)
        {
            var parts = new List<string>();
            if (Places.IsServer(_path))
            {
                parts.Add(_path);
            }
            else
            {
                for (var dir = new DirectoryInfo(_path); dir != null; dir = dir.Parent)
                    parts.Insert(0, dir.FullName);
                // En la red, el servidor antes de su carpeta compartida.
                if (Places.ShareRoot(_path) is { } share)
                    parts.Insert(0, share[..share.LastIndexOf('\\')]);
            }
            crumbs.AddRange(parts.Select(p => new Crumb(Places.NameOf(p), p, false, true)));
        }
        Crumbs.ItemsSource = crumbs;
        Dispatcher.BeginInvoke(CrumbScroll.ScrollToRightEnd, DispatcherPriority.Loaded);
        UpdateCurrentPlace();
        UpdateCommands();
    }

    private void UpdateCurrentPlace()
    {
        foreach (var place in _folders.Append(_home).Concat(_drives).Concat(_cloud).Concat(_network))
            place.IsCurrent = SamePath(place.Path, _path);
    }

    private void UpdateCommands()
    {
        bool inFolder = _path != null;
        int selected = inFolder ? ActiveList.SelectedItems.Count : 0;
        NewFolderButton.IsEnabled = inFolder;
        PasteButton.IsEnabled = inFolder && FileOperations.ClipboardHasFiles();
        CutButton.IsEnabled = CopyButton.IsEnabled = DeleteButton.IsEnabled = selected > 0;
        RenameButton.IsEnabled = selected == 1;
        SortButton.IsEnabled = inFolder;
        // En Este equipo no hay orden ni vista que cambiar.
        ViewControls.Visibility = inFolder ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowHome(bool animate)
    {
        HomePanel.Visibility = Visibility.Visible;
        DetailsPanel.Visibility = GridList.Visibility = EmptyState.Visibility = Visibility.Collapsed;
        StatusText.Text = Loc.F("{0} unidades", _drives.Count);
        if (animate)
            Motion.In(Body, Body, TaskbarPosition.Bottom, 0.5);
        _ = LoadDrivesAsync();
        _ = LoadRecentAsync();
    }

    private async Task LoadRecentAsync()
    {
        var recent = await Task.Run(() => StartMenuService.GetRecent(12));
        HomeRecent.ItemsSource = recent;
        RecentTitle.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowFolder(bool animate)
    {
        HomePanel.Visibility = Visibility.Collapsed;
        ApplyView();
        UpdateEmptyState();
        if (animate)
            Motion.In(Body, Body, TaskbarPosition.Bottom, 0.5);
    }

    /// <summary>El host de una ruta de red ("10.50.0.200" de "\\\\10.50.0.200\\Compartida"); null si es local.</summary>
    private static string? HostOf(string path) =>
        path.StartsWith(@"\\") ? path[2..].Split('\\')[0] : null;

    private void ShowConnecting(string path)
    {
        HomePanel.Visibility = DetailsPanel.Visibility = GridList.Visibility = Visibility.Collapsed;
        EmptyIcon.Symbol = SymbolRegular.PlugConnected24;
        EmptyTitle.Text = HostOf(path) is { } host ? Loc.F("Conectando con {0}…", host) : Loc.T("Abriendo…");
        EmptyText.Text = "";
        EmptyState.Visibility = Visibility.Visible;
        StatusText.Text = "";
    }

    private void ShowNoResponse(string path)
    {
        ShowError(HostOf(path) is { } host
            ? Loc.F("{0} no responde. Revisa que estés en la misma red (o conectado a la VPN) y vuelve a intentar.", host)
            : Loc.T("La carpeta tarda demasiado en responder."));
        RetryButton.Visibility = Visibility.Visible;
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(merge: false);

    private void ShowError(string message)
    {
        _entries = [];
        _view = null;
        DetailsList.ItemsSource = GridList.ItemsSource = null;
        HomePanel.Visibility = DetailsPanel.Visibility = GridList.Visibility = Visibility.Collapsed;
        EmptyIcon.Symbol = SymbolRegular.FolderProhibited24;
        EmptyTitle.Text = Loc.T("No se puede abrir esta carpeta");
        EmptyText.Text = message;
        EmptyState.Visibility = Visibility.Visible;
        StatusText.Text = "";
    }

    private void ApplyView()
    {
        bool grid = Settings.Current.FilesGridView;
        DetailsToggle.IsChecked = !grid;
        GridToggle.IsChecked = grid;
        if (_path is null || _view is null)
            return;
        DetailsPanel.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        GridList.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEmptyState()
    {
        bool empty = _view is { Count: 0 };
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty)
            return;
        if (SearchBox.Text is { Length: > 0 } query)
        {
            EmptyIcon.Symbol = SymbolRegular.Search24;
            EmptyTitle.Text = Loc.T("Sin resultados");
            EmptyText.Text = Loc.F("Nada en esta carpeta coincide con «{0}».", query.Trim());
        }
        else
        {
            EmptyIcon.Symbol = SymbolRegular.FolderOpen24;
            EmptyTitle.Text = Loc.T("Esta carpeta está vacía");
            EmptyText.Text = Loc.T("Arrastra archivos aquí o crea una carpeta nueva.");
        }
    }

    private void UpdateStatus()
    {
        UpdateCommands();
        if (_path is null || _view is null)
            return;
        int count = _view.Count;
        string text = count == 1 ? Loc.T("1 elemento") : Loc.F("{0} elementos", count);
        var selected = SelectedEntries;
        if (selected.Count > 0)
        {
            text += "   ·   " + (selected.Count == 1 ? Loc.T("1 seleccionado") : Loc.F("{0} seleccionados", selected.Count));
            long bytes = selected.Where(e => !e.IsFolder).Sum(e => e.Size);
            if (selected.Any(e => !e.IsFolder))
                text += "   ·   " + FileEntry.FormatSize(bytes);
        }
        StatusText.Text = text;
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    // ── Cabecera de la lista y orden ────────────────────────────────────────

    private void BuildHeader()
    {
        var columns = new (SortKey Key, string Text)[]
        {
            (SortKey.Name, Loc.T("Nombre")),
            (SortKey.Modified, Loc.T("Fecha de modificación")),
            (SortKey.Type, Loc.T("Tipo")),
            (SortKey.Size, Loc.T("Tamaño")),
        };
        Header.Children.Clear();
        for (int i = 0; i < columns.Length; i++)
        {
            var (key, text) = columns[i];
            var arrow = new SymbolIcon
            {
                Symbol = SymbolRegular.ChevronDown12,
                FontSize = 10,
                Margin = new Thickness(6, 0, 0, 0),
                Foreground = (Brush)FindResource("StrataAccentBrush"),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(_descending ? 0 : 180),
                Visibility = key == _sort ? Visibility.Visible : Visibility.Collapsed,
            };
            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, arrow } };
            var button = new Button
            {
                Style = (Style)FindResource("CommandButton"),
                Height = 28,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(-6, 0, 6, 0),
                Content = content,
                HorizontalAlignment = key == SortKey.Size ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            if (key == SortKey.Size)
                button.Margin = new Thickness(0, 0, -6, 0);
            button.Click += (_, _) => SortBy(key, key == _sort ? !_descending : DefaultDescending(key));
            Grid.SetColumn(button, i);
            Header.Children.Add(button);
        }
    }

    /// <summary>Fecha y tamaño empiezan de mayor a menor (lo último, lo más grande), como en el Explorador.</summary>
    private static bool DefaultDescending(SortKey key) => key is SortKey.Modified or SortKey.Size;

    private void SortBy(SortKey key, bool descending)
    {
        _sort = key;
        _descending = descending;
        if (_view != null)
            _view.CustomSort = new EntryComparer(_sort, _descending);
        BuildHeader();
    }

    /// <summary>Carpetas primero; empates por nombre en orden natural ("2" antes que "10").</summary>
    private sealed class EntryComparer(SortKey key, bool descending) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (FileEntry)x!;
            var b = (FileEntry)y!;
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            int result = key switch
            {
                SortKey.Modified => a.Modified.CompareTo(b.Modified),
                SortKey.Type => string.Compare(a.TypeName, b.TypeName, StringComparison.CurrentCultureIgnoreCase),
                SortKey.Size => a.Size.CompareTo(b.Size),
                _ => 0,
            };
            if (result == 0)
                result = StrCmpLogicalW(a.Name, b.Name);
            return descending ? -result : result;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    // ── Acciones ────────────────────────────────────────────────────────────

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void Open(IReadOnlyList<FileEntry> entries)
    {
        if (entries.Count == 0)
            return;
        // Una carpeta (o un acceso directo a una) se abre aquí mismo; los archivos, con su programa.
        foreach (var entry in entries)
        {
            if (FolderOf(entry) is { } folder)
            {
                Navigate(folder);
                return;
            }
        }
        // Una imagen, un video o un audio: el visor, con los demás de la carpeta en el orden de la vista.
        if (entries is [var single] && MediaKinds.Of(single.FullPath) != MediaKind.None && _view != null)
        {
            var media = _view.Cast<FileEntry>().Where(e => !e.IsFolder && MediaKinds.Of(e.FullPath) != MediaKind.None)
                             .Select(e => e.FullPath).ToList();
            MediaViewerWindow.Open(media, media.IndexOf(single.FullPath));
            return;
        }
        foreach (var entry in entries)
        {
            if (!FileOperations.Open(entry.FullPath))
                ShowMessage(Loc.F("No se pudo abrir {0}", entry.Name));
        }
    }

    private static string? FolderOf(FileEntry entry)
    {
        if (entry.IsFolder)
            return entry.FullPath;
        if (entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            && ExtraApps.ShortcutTarget(entry.FullPath) is { Length: > 0 } target && Directory.Exists(target))
            return target;
        return null;
    }

    private void NewFolder()
    {
        if (_path is null)
            return;
        try
        {
            _selectAfterLoad = FileOperations.NewFolder(_path, Loc.T("Nueva carpeta"));
            _renameAfterLoad = true;
            _reload.Stop();
            _ = LoadAsync(merge: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage(Loc.T("No se pudo crear la carpeta"));
            Log.Error("Archivos: no se pudo crear la carpeta", ex);
        }
    }

    private void CopySelection(bool cut)
    {
        var selected = SelectedEntries;
        if (selected.Count == 0)
            return;
        FileOperations.SetClipboard(selected.Select(e => e.FullPath).ToList(), cut);
        _cutPaths = cut ? selected.Select(e => e.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
            entry.IsCut = _cutPaths.Contains(entry.FullPath);
        ShowMessageInfo(cut
            ? (selected.Count == 1 ? Loc.T("1 elemento cortado") : Loc.F("{0} elementos cortados", selected.Count))
            : (selected.Count == 1 ? Loc.T("1 elemento copiado") : Loc.F("{0} elementos copiados", selected.Count)));
        UpdateCommands();
    }

    private async Task PasteAsync(string? into = null)
    {
        string? target = into ?? _path;
        if (target is null || FileOperations.ReadClipboard() is not { } clip)
            return;
        if (clip.Cut)
        {
            var moving = clip.Paths.Where(p => !SamePath(System.IO.Path.GetDirectoryName(p), target)).ToList();
            if (moving.Count > 0 && await FileOperations.MoveAsync(moving, target, Handle))
            {
                _cutPaths.Clear();
                try
                {
                    Clipboard.Clear();
                }
                catch (COMException)
                {
                }
            }
        }
        else
        {
            await FileOperations.CopyAsync(clip.Paths, target, Handle);
        }
        UpdateCommands();
    }

    private async Task RecycleSelectionAsync()
    {
        var selected = SelectedEntries;
        if (selected.Count == 0)
            return;
        // Después queda marcado el siguiente, para seguir con el teclado.
        var list = ActiveList;
        int index = list.Items.IndexOf(selected[^1]);
        var next = list.Items.Cast<FileEntry>().Skip(index + 1).FirstOrDefault(e => !selected.Contains(e))
                   ?? list.Items.Cast<FileEntry>().Take(index).LastOrDefault(e => !selected.Contains(e));
        if (await FileOperations.RecycleAsync(selected.Select(e => e.FullPath).ToList(), Handle) && next != null)
            _selectAfterLoad = next.FullPath;
        _reload.Stop();
        _ = LoadAsync(merge: true);
    }

    private void StartRename(FileEntry entry)
    {
        foreach (var other in _entries.Where(e => e.IsRenaming && e != entry))
            other.IsRenaming = false;
        ActiveList.SelectedItem = entry;
        ActiveList.ScrollIntoView(entry);
        entry.IsRenaming = true;
    }

    private void CommitRename(TextBox box, bool apply)
    {
        if (box.DataContext is not FileEntry entry || !entry.IsRenaming)
            return;
        entry.IsRenaming = false;
        string name = box.Text.Trim();
        // Con la extensión oculta se escribió el nombre sin ella: se conserva la que tenía.
        if (name.Length > 0 && entry.DisplayName != entry.Name)
            name += entry.Extension;
        // Devuelve el foco a la lista para seguir con el teclado.
        Dispatcher.BeginInvoke(() => (ActiveList.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem ?? (UIElement)ActiveList).Focus(),
                               DispatcherPriority.Input);
        if (!apply || name.Length == 0 || name == entry.Name)
            return;
        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowMessage(Loc.T("El nombre no puede llevar \\ / : * ? \" < > |"));
            return;
        }
        if (FileOperations.Rename(entry.FullPath, name, entry.IsFolder) is { } error)
        {
            ShowMessage(error);
            return;
        }
        _selectAfterLoad = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(entry.FullPath)!, name);
        _reload.Stop();
        _ = LoadAsync(merge: true);
    }

    private void ShowMessageInfo(string message)
    {
        MessageText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        ShowMessage(message);
        // El próximo aviso (un error) vuelve a su color.
        _messageTimer.Tick += Restore;
        void Restore(object? s, EventArgs e)
        {
            _messageTimer.Tick -= Restore;
            MessageText.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        }
    }

    private void EditPath()
    {
        PathBox.Text = _path ?? "";
        PathBox.Visibility = Visibility.Visible;
        CrumbScroll.Visibility = Visibility.Hidden;
        PathBox.Focus();
        PathBox.SelectAll();
    }

    private void EndEditPath()
    {
        PathBox.Visibility = Visibility.Collapsed;
        CrumbScroll.Visibility = Visibility.Visible;
    }

    private void GoToTyped(string text)
    {
        text = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
        EndEditPath();
        if (text.Length == 0)
        {
            Navigate(null);
            return;
        }
        if (Directory.Exists(text))
        {
            Navigate(text);
        }
        else if (File.Exists(text))
        {
            _selectAfterLoad = System.IO.Path.GetFullPath(text);
            Navigate(System.IO.Path.GetDirectoryName(_selectAfterLoad));
        }
        else if (text.Contains(':') && !System.IO.Path.IsPathFullyQualified(text))
        {
            // shell:Startup, ms-settings:, una dirección web…: que lo abra Windows.
            if (!FileOperations.Open(text))
                ShowMessage(Loc.F("No se encontró {0}", text));
        }
        else
        {
            ShowMessage(Loc.F("No se encontró {0}", text));
        }
    }

    private void SetGridView(bool grid)
    {
        if (Settings.Current.FilesGridView != grid)
        {
            var selected = SelectedEntries;
            Settings.Update(s => s.FilesGridView = grid);
            foreach (var entry in selected)
                ActiveList.SelectedItems.Add(entry);
            // Las miniaturas solo se leen en la cuadrícula.
            if (grid && _loading is { } loading)
                FileIcons.Load(_entries.ToList(), true, Dispatcher, loading.Token);
        }
        ApplyView();
        UpdateStatus();
    }

    private void SetShowHidden(bool show)
    {
        HiddenToggle.IsChecked = show;
        Settings.Update(s => s.FilesShowHidden = show);
        _ = LoadAsync(merge: true);
    }

    /// <summary>
    /// Fijar (siempre en este dispositivo) o liberar espacio: los mismos atributos que pone el
    /// Explorador (attrib +P / +U); OneDrive y las demás nubes descargan o liberan solas.
    /// </summary>
    private void SetCloudPin(IReadOnlyList<FileEntry> entries, bool keep)
    {
        const FileAttributes pinned = (FileAttributes)0x80000, unpinned = (FileAttributes)0x100000;
        foreach (var entry in entries)
        {
            try
            {
                var attributes = File.GetAttributes(entry.FullPath) & ~(pinned | unpinned);
                File.SetAttributes(entry.FullPath, attributes | (keep ? pinned : unpinned));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowMessage(Loc.F("No se pudo cambiar {0}", entry.Name));
                Log.Error($"Archivos: no se pudo cambiar el estado en la nube de {entry.FullPath}", ex);
            }
        }
        _reload.Stop();
        _ = LoadAsync(merge: true);
    }

    // ── Menús ───────────────────────────────────────────────────────────────

    private static MenuItem Item(string header, SymbolRegular? icon, Action action, bool enabled = true, bool bold = false)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (icon is { } symbol)
            item.Icon = new SymbolIcon { Symbol = symbol };
        if (bold)
            item.FontWeight = FontWeights.SemiBold;
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"Archivos: \"{header}\" falló", ex);
            }
        };
        return item;
    }

    /// <summary>Criterio y sentido del orden (en "Ordenar por" del menú y en el botón Ordenar).</summary>
    private List<Control> SortItems()
    {
        var items = new List<Control>();
        foreach (var (key, text) in new[] { (SortKey.Name, Loc.T("Nombre")), (SortKey.Modified, Loc.T("Fecha de modificación")),
                                            (SortKey.Type, Loc.T("Tipo")), (SortKey.Size, Loc.T("Tamaño")) })
        {
            var item = Item(text, null, () => SortBy(key, DefaultDescending(key)));
            item.IsChecked = _sort == key;
            items.Add(item);
        }
        items.Add(new Separator());
        var ascending = Item(Loc.T("Ascendente"), null, () => SortBy(_sort, false));
        ascending.IsChecked = !_descending;
        var descending = Item(Loc.T("Descendente"), null, () => SortBy(_sort, true));
        descending.IsChecked = _descending;
        items.Add(ascending);
        items.Add(descending);
        return items;
    }

    private MenuItem SortMenu()
    {
        var menu = new MenuItem { Header = Loc.T("Ordenar por"), Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSort24 } };
        foreach (var item in SortItems())
            menu.Items.Add(item);
        return menu;
    }

    private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var list = (ListBox)sender;
        var menu = list.ContextMenu!;
        menu.Items.Clear();
        if (_path is not { } folder)
        {
            e.Handled = true;
            return;
        }
        bool onItem = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject) != null;
        // El menú de Windows ("Mostrar más opciones") sale donde se abrió este.
        var (x, y) = ShellMenu.Cursor();
        if (!onItem)
            list.UnselectAll();
        var selected = SelectedEntries;
        if (onItem && selected.Count > 0)
        {
            var first = selected[0];
            menu.Items.Add(Item(Loc.T("Abrir"), SymbolRegular.Open24, () => Open(selected), bold: true));
            if (selected.Count == 1 && !first.IsFolder)
                menu.Items.Add(Item(Loc.T("Abrir con…"), SymbolRegular.AppGeneric24, () => FileOperations.OpenWith(first.FullPath)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Cortar"), SymbolRegular.Cut24, () => CopySelection(cut: true)));
            menu.Items.Add(Item(Loc.T("Copiar"), SymbolRegular.Copy24, () => CopySelection(cut: false)));
            if (selected.Count == 1 && first.IsFolder && FileOperations.ClipboardHasFiles())
                menu.Items.Add(Item(Loc.T("Pegar en esta carpeta"), SymbolRegular.ClipboardPaste24, () => _ = PasteAsync(first.FullPath)));
            menu.Items.Add(Item(Loc.T("Copiar ruta"), SymbolRegular.DocumentCopy24, () =>
                Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(s => $"\"{s.FullPath}\"")))));
            if (CloudPlace(folder) != null && selected.Any(s => s.HasCloudState))
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(Item(Loc.T("Mantener siempre en este dispositivo"), SymbolRegular.CloudCheckmark24, () => SetCloudPin(selected, true)));
                menu.Items.Add(Item(Loc.T("Liberar espacio"), SymbolRegular.Cloud24, () => SetCloudPin(selected, false)));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Cambiar nombre"), SymbolRegular.Rename24, () => StartRename(first), enabled: selected.Count == 1));
            menu.Items.Add(Item(Loc.T("Enviar a la Papelera"), SymbolRegular.Delete24, () => _ = RecycleSelectionAsync()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Mostrar en el Explorador de Windows"), SymbolRegular.FolderOpen24,
                                () => FileOperations.ShowInExplorer(first.FullPath, select: true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Mostrar más opciones"), SymbolRegular.MoreHorizontal24,
                                () => ShellMenu.Show(selected.Select(s => s.FullPath).ToList(), background: false, x, y)));
        }
        else
        {
            menu.Items.Add(Item(Loc.T("Nueva carpeta"), SymbolRegular.FolderAdd24, NewFolder));
            menu.Items.Add(Item(Loc.T("Pegar"), SymbolRegular.ClipboardPaste24, () => _ = PasteAsync(), enabled: FileOperations.ClipboardHasFiles()));
            menu.Items.Add(new Separator());
            menu.Items.Add(SortMenu());
            var view = new MenuItem { Header = Loc.T("Vista"), Icon = new SymbolIcon { Symbol = SymbolRegular.Grid24 } };
            var details = Item(Loc.T("Lista"), SymbolRegular.TextBulletListLtr24, () => SetGridView(false));
            details.IsChecked = !Settings.Current.FilesGridView;
            var grid = Item(Loc.T("Cuadrícula"), SymbolRegular.Grid24, () => SetGridView(true));
            grid.IsChecked = Settings.Current.FilesGridView;
            view.Items.Add(details);
            view.Items.Add(grid);
            menu.Items.Add(view);
            var hidden = Item(Loc.T("Mostrar archivos ocultos"), SymbolRegular.Eye24, () => SetShowHidden(!Settings.Current.FilesShowHidden));
            hidden.IsChecked = Settings.Current.FilesShowHidden;
            menu.Items.Add(hidden);
            var extensions = Item(Loc.T("Mostrar las extensiones de los archivos"), null, () => SetShowExtensions(!FileEntry.ShowExtensions));
            extensions.IsChecked = FileEntry.ShowExtensions;
            menu.Items.Add(extensions);
            menu.Items.Add(Item(Loc.T("Actualizar"), SymbolRegular.ArrowClockwise24, () => _ = LoadAsync(merge: true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Abrir en el Explorador de Windows"), SymbolRegular.FolderOpen24,
                                () => FileOperations.ShowInExplorer(folder, select: false)));
            menu.Items.Add(Item(Loc.T("Abrir terminal aquí"), SymbolRegular.WindowConsole20, () => FileOperations.OpenTerminal(folder)));
            if (!Places.IsServer(folder))
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(Item(Loc.T("Mostrar más opciones"), SymbolRegular.MoreHorizontal24,
                                    () => ShellMenu.Show([folder], background: true, x, y)));
            }
        }
    }

    // ── Mouse ───────────────────────────────────────────────────────────────

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null && node is not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is FileEntry entry
            && !entry.IsRenaming)
            Open([entry]);
    }

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        var source = e.OriginalSource as DependencyObject;
        _clickedSelected = null;
        if (Ancestor<ScrollBar>(source) != null || Ancestor<TextBox>(source) != null)
            return;
        var item = Ancestor<ListBoxItem>(source);
        if (item is null)
        {
            // Clic en el espacio libre: nada seleccionado.
            list.UnselectAll();
            list.Focus();
            _dragStart = null;
            return;
        }
        _dragStart = e.GetPosition(this);
        // Sobre algo ya seleccionado entre varios: se espera a soltar (o a arrastrar todo el grupo).
        if (item.IsSelected && list.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _clickedSelected = item.DataContext as FileEntry;
            item.Focus();
            e.Handled = true;
        }
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_clickedSelected is { } entry)
            ((ListBox)sender).SelectedItem = entry;
        _clickedSelected = null;
        _dragStart = null;
    }

    /// <summary>Arrastrar la selección afuera (a otra app, al escritorio, al Explorador) o a una carpeta de aquí.</summary>
    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStart = null;
        _clickedSelected = null;
        var selected = SelectedEntries;
        if (selected.Count == 0 || selected.Any(s => s.IsRenaming))
            return;
        var data = new DataObject(DataFormats.FileDrop, selected.Select(s => s.FullPath).ToArray());
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: falló el arrastre", ex);
        }
    }

    /// <summary>Dónde cae lo que se suelta: la carpeta bajo el puntero o la actual.</summary>
    private string? DropTarget(DragEventArgs e) =>
        Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is FileEntry { IsFolder: true } folder
            ? folder.FullPath
            : _path;

    /// <summary>Como el Explorador: misma unidad mueve, otra copia; Ctrl copia y Mayús mueve.</summary>
    private DragDropEffects DropEffect(DragEventArgs e, string target, string[] files)
    {
        if (files.Any(f => SamePath(f, target) || SamePath(System.IO.Path.GetDirectoryName(f), target)))
            return DragDropEffects.None;
        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
            return DragDropEffects.Copy;
        if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0)
            return DragDropEffects.Move;
        bool sameDrive = files.All(f => string.Equals(System.IO.Path.GetPathRoot(f), System.IO.Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase));
        return sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        try
        {
            e.Effects = DropTarget(e) is { } target && e.Data.GetData(DataFormats.FileDrop) is string[] files
                ? DropEffect(e, target, files) & e.AllowedEffects
                : DragDropEffects.None;
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: falló el arrastre", ex);
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Card_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (DropTarget(e) is not { } target || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
                return;
            var effect = DropEffect(e, target, files) & e.AllowedEffects;
            e.Handled = true;
            if (effect == DragDropEffects.Move)
                await FileOperations.MoveAsync(files, target, Handle);
            else if (effect == DragDropEffects.Copy)
                await FileOperations.CopyAsync(files, target, Handle);
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: falló al soltar", ex);
        }
    }

    // ── Teclado ─────────────────────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool typing = Keyboard.FocusedElement is TextBox;
        bool handled = true;
        switch (key)
        {
            case Key.Left when mods == ModifierKeys.Alt:
            case Key.BrowserBack:
                GoBack();
                break;
            case Key.Right when mods == ModifierKeys.Alt:
            case Key.BrowserForward:
                GoForward();
                break;
            case Key.Up when mods == ModifierKeys.Alt:
                GoUp();
                break;
            case Key.F5:
                _ = LoadAsync(merge: true);
                break;
            case Key.L when mods == ModifierKeys.Control:
            case Key.D when mods == ModifierKeys.Alt:
            case Key.F4 when mods == ModifierKeys.None:
                EditPath();
                break;
            case Key.F when mods == ModifierKeys.Control:
            case Key.F3:
                SearchBox.Focus();
                break;
            case Key.W when mods == ModifierKeys.Control:
                Close();
                break;
            default:
                handled = false;
                break;
        }
        if (handled || typing)
        {
            e.Handled = handled;
            return;
        }

        handled = true;
        switch (key)
        {
            case Key.Back:
                GoBack();
                break;
            case Key.Enter:
                Open(SelectedEntries);
                break;
            case Key.F2 when SelectedEntries is [var only]:
                StartRename(only);
                break;
            case Key.Delete:
                _ = RecycleSelectionAsync();
                break;
            case Key.C when mods == ModifierKeys.Control:
                CopySelection(cut: false);
                break;
            case Key.X when mods == ModifierKeys.Control:
                CopySelection(cut: true);
                break;
            case Key.V when mods == ModifierKeys.Control:
                _ = PasteAsync();
                break;
            case Key.N when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                NewFolder();
                break;
            case Key.A when mods == ModifierKeys.Control:
                ActiveList.SelectAll();
                break;
            case Key.Escape when SearchBox.Text.Length > 0:
                SearchBox.Text = "";
                break;
            default:
                handled = false;
                break;
        }
        e.Handled = handled;
    }

    // ── Controles ───────────────────────────────────────────────────────────

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => GoForward();
    private void Up_Click(object sender, RoutedEventArgs e) => GoUp();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(merge: true);
    private void NewFolder_Click(object sender, RoutedEventArgs e) => NewFolder();
    private void Cut_Click(object sender, RoutedEventArgs e) => CopySelection(cut: true);
    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelection(cut: false);
    private void Paste_Click(object sender, RoutedEventArgs e) => _ = PasteAsync();
    private void Delete_Click(object sender, RoutedEventArgs e) => _ = RecycleSelectionAsync();
    private void DetailsView_Click(object sender, RoutedEventArgs e) => SetGridView(false);
    private void GridView_Click(object sender, RoutedEventArgs e) => SetGridView(true);
    private void Hidden_Click(object sender, RoutedEventArgs e) => SetShowHidden(HiddenToggle.IsChecked == true);
    private void Extensions_Click(object sender, RoutedEventArgs e) => SetShowExtensions(ExtensionsToggle.IsChecked == true);

    /// <summary>Muestra u oculta las extensiones en todas las ventanas abiertas de Archivos.</summary>
    private static void SetShowExtensions(bool show)
    {
        FileEntry.ShowExtensions = show;
        Settings.Update(s => s.FilesShowExtensions = show);
        foreach (var window in Application.Current.Windows.OfType<FilesWindow>())
        {
            window.ExtensionsToggle.IsChecked = show;
            foreach (var entry in window._entries)
                entry.RefreshName();
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntries is [var only])
            StartRename(only);
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = PlacementMode.Bottom };
        foreach (var item in SortItems())
            menu.Items.Add(item);
        menu.IsOpen = true;
    }

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Place place)
            Navigate(place.Path);
    }

    /// <summary>Clic derecho en una ubicación de red recordada: quitarla de la lista.</summary>
    private void Place_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.DataContext is not Place { CanForget: true } place)
        {
            e.Handled = true;
            return;
        }
        button.ContextMenu!.Items.Clear();
        button.ContextMenu.Items.Add(Item(Loc.T("Quitar de la lista"), SymbolRegular.Dismiss24, () =>
        {
            ForgetNetwork(place);
            _ = LoadDrivesAsync();
        }));
    }

    /// <summary>"Conectar unidad de red" de Windows, en su propio proceso.</summary>
    private void ConnectDrive_Click(object sender, RoutedEventArgs e) =>
        FileOperations.ConnectNetworkDrive();

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not RecentItem recent)
            return;
        if (ExtraApps.ShortcutTarget(recent.Path) is { Length: > 0 } target && Directory.Exists(target))
            Navigate(target);
        else if (!FileOperations.Open(recent.Path))
            ShowMessage(Loc.F("No se pudo abrir {0}", recent.Name));
    }

    private void Crumb_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Crumb crumb && !SamePath(crumb.Path, _path))
            Navigate(crumb.Path);
    }

    private void AddressBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (PathBox.IsVisible || Ancestor<Button>(e.OriginalSource as DependencyObject) != null)
            return;
        EditPath();
    }

    private void PathBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GoToTyped(PathBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndEditPath();
            ActiveList.Focus();
            e.Handled = true;
        }
    }

    private void PathBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndEditPath();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateEmptyState();
        UpdateStatus();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            ActiveList.Focus();
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Enter && _view is { Count: > 0 })
        {
            // Bajar a los resultados con el teclado.
            ActiveList.SelectedIndex = 0;
            (ActiveList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsVisible || box.DataContext is not FileEntry entry)
            return;
        box.Text = entry.DisplayName;
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            // Sin la extensión, para no cambiarla sin querer.
            int length = entry.IsFolder || entry.DisplayName != entry.Name || entry.Extension.Length == 0
                ? entry.DisplayName.Length
                : entry.Name.Length - entry.Extension.Length;
            box.Select(0, length);
        }, DispatcherPriority.Input);
    }

    private void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            CommitRename((TextBox)sender, apply: e.Key == Key.Enter);
            e.Handled = true;
        }
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        CommitRename((TextBox)sender, apply: true);
}
