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

/// <summary>Lo que la ventana le pasa a cada panel para su portada (Este equipo).</summary>
public sealed record PlacesData(Place Home, List<Place> Folders, List<Place> Drives, List<Place> Cloud, List<Place> Network);

/// <summary>
/// Una carpeta abierta: ruta, búsqueda, lista o cuadrícula y estado. Cada pestaña de Archivos tiene
/// uno o dos (vista doble); los comandos de la ventana actúan sobre el activo. Lee con System.IO y
/// opera con el motor de Windows (<see cref="FileOperations"/>); no carga extensiones del Shell
/// (ver <see cref="FileIcons"/>).
/// </summary>
public partial class FolderPane : UserControl
{
    private enum SortKey { Name, Modified, Type, Size }

    /// <summary>Un tramo de la ruta de navegación; <see cref="Path"/> null es Este equipo.</summary>
    private sealed record Crumb(string Name, string? Path, bool IsHome, bool HasChevron);

    public static readonly DependencyProperty DateColumnProperty =
        DependencyProperty.Register(nameof(DateColumn), typeof(GridLength), typeof(FolderPane), new PropertyMetadata(new GridLength(170)));

    public static readonly DependencyProperty TypeColumnProperty =
        DependencyProperty.Register(nameof(TypeColumn), typeof(GridLength), typeof(FolderPane), new PropertyMetadata(new GridLength(170)));

    /// <summary>Ancho de la columna Fecha (cero en un panel angosto).</summary>
    public GridLength DateColumn
    {
        get => (GridLength)GetValue(DateColumnProperty);
        set => SetValue(DateColumnProperty, value);
    }

    /// <summary>Ancho de la columna Tipo (cero en un panel angosto).</summary>
    public GridLength TypeColumn
    {
        get => (GridLength)GetValue(TypeColumnProperty);
        set => SetValue(TypeColumnProperty, value);
    }

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
    private PlacesData? _places;
    private HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectAfterLoad;
    private bool _renameAfterLoad;
    private Point? _dragStart;
    private FileEntry? _clickedSelected;
    private bool _isActive;

    /// <summary>Cambió la carpeta, el historial o la selección (la ventana actualiza comandos y título).</summary>
    public event Action? StateChanged;

    /// <summary>Se hizo clic o se enfocó (en la vista doble pasa a ser el panel activo).</summary>
    public event Action? Activated;

    /// <summary>Hay que volver a leer unidades, nube y red (una carpeta de red nueva, la portada).</summary>
    public event Action? PlacesRefreshRequested;

    /// <summary>Se anclaron o desanclaron carpetas (la barra lateral se vuelve a armar).</summary>
    public event Action? PinsChanged;

    /// <summary>Abrir una carpeta en una pestaña nueva (clic con la rueda, menú).</summary>
    public event Action<string>? OpenInNewTabRequested;

    public FolderPane()
    {
        InitializeComponent();
        _reload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _reload.Tick += (_, _) =>
        {
            _reload.Stop();
            _ = LoadAsync(merge: true);
        };
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            _ = UpdatePreviewAsync();
        };
        _deepSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _deepSearchTimer.Tick += (_, _) =>
        {
            _deepSearchTimer.Stop();
            StartDeepSearch();
        };
        _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            MessageText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        };
        BuildHeader();
        foreach (var list in new[] { DetailsList, GridList })
        {
            list.MouseDoubleClick += List_MouseDoubleClick;
            list.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
            list.PreviewMouseLeftButtonUp += List_PreviewMouseLeftButtonUp;
            list.PreviewMouseMove += List_PreviewMouseMove;
            list.PreviewMouseDown += List_PreviewMouseDown;
            list.SelectionChanged += (_, _) => UpdateStatus();
            // Un menú vacío para que llegue ContextMenuOpening; se arma al abrirse.
            list.ContextMenu = new ContextMenu();
            list.ContextMenuOpening += List_ContextMenuOpening;
        }
        PreviewMouseDown += (_, _) => Activated?.Invoke();
        GotKeyboardFocus += (_, _) => Activated?.Invoke();
        SizeChanged += (_, e) => FitColumns(e.NewSize.Width);
    }

    // ── Lo que usa la ventana ───────────────────────────────────────────────

    public string? CurrentPath => _path;

    public string Title => _path is null ? _places?.Home.Name ?? Loc.T("Este equipo")
        : InRecycleBin ? Loc.T("Papelera de reciclaje")
        : Places.NameOf(_path);

    public bool InFolder => _path != null;

    /// <summary>Se ve la Papelera (no se crean carpetas ni se pega; se restaura o se borra para siempre).</summary>
    public bool InRecycleBin => _path == Places.RecycleBin;

    /// <summary>Una carpeta de verdad, donde se puede crear, pegar, cortar y cambiar nombres.</summary>
    public bool CanModify => _path != null && !InRecycleBin;

    public int SelectedCount => _path is null ? 0 : ActiveList.SelectedItems.Count;

    /// <summary>En la vista doble, el panel activo lleva el borde de acento.</summary>
    public bool IsActivePane
    {
        get => _isActive;
        set
        {
            _isActive = value;
            Card.SetResourceReference(Border.BorderBrushProperty, value ? "StrataAccentBrush" : "CardStrokeColorDefaultBrush");
        }
    }

    /// <summary>Unidades, nube y red para la portada; las de la nube también para el estado de los archivos.</summary>
    public void SetPlaces(PlacesData places)
    {
        _places = places;
        HomeFolders.ItemsSource = places.Folders;
        HomeDrives.ItemsSource = places.Drives;
        HomeCloud.ItemsSource = places.Cloud;
        HomeNetwork.ItemsSource = places.Network;
        HomeCloudTitle.Visibility = places.Cloud.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeNetworkTitle.Visibility = HomeNetwork.Visibility = places.Network.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_path is null)
            StatusText.Text = Loc.F("{0} unidades", places.Drives.Count);
    }

    /// <summary>Cambió la vista (lista o cuadrícula) para todos: se aplica y, en la cuadrícula, se leen las carátulas.</summary>
    public void ViewChanged()
    {
        ApplyView();
        if (Settings.Current.FilesGridView && _loading is { } loading && _path != null)
            FileIcons.Load(_entries.ToList(), Thumbnails(_path), Dispatcher, loading.Token);
        UpdateStatus();
    }

    /// <summary>Vuelve a leer la carpeta sin perder el lugar (F5, ocultos).</summary>
    public void Reload() => _ = LoadAsync(merge: true);

    /// <summary>Cambió mostrar u ocultar las extensiones.</summary>
    public void RefreshNames()
    {
        foreach (var entry in _entries)
            entry.RefreshName();
    }

    public void FocusList() => ActiveList.Focus();

    /// <summary>Deja de leer y de vigilar (al cerrar su pestaña o la ventana).</summary>
    public void Close()
    {
        _loading?.Cancel();
        _watcher?.Dispose();
        _watcher = null;
        _reload.Stop();
    }

    /// <summary>Ajusta las columnas al ancho: en un panel angosto (vista doble) se ocultan el tipo y luego la fecha.</summary>
    private void FitColumns(double width)
    {
        TypeColumn = width < 760 ? new GridLength(0) : new GridLength(170);
        DateColumn = width < 520 ? new GridLength(0) : new GridLength(170);
        HeaderType.Width = TypeColumn;
        HeaderDate.Width = DateColumn;
        foreach (var child in Header.Children.OfType<FrameworkElement>())
        {
            int column = Grid.GetColumn(child);
            child.Visibility = (column == 1 && DateColumn.Value == 0) || (column == 2 && TypeColumn.Value == 0)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    // ── Navegación ──────────────────────────────────────────────────────────

    public void Navigate(string? path, bool record = true)
    {
        // Un archivo: su carpeta, con él seleccionado (como "Mostrar en la carpeta").
        if (path != null && File.Exists(path))
        {
            _selectAfterLoad = System.IO.Path.GetFullPath(path);
            path = System.IO.Path.GetDirectoryName(_selectAfterLoad);
        }
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

    public void GoBack()
    {
        if (_back.Count == 0)
            return;
        _forward.Add(_path);
        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Navigate(target, record: false);
    }

    public void GoForward()
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
        if (InRecycleBin)
        {
            Navigate(null);
            return;
        }
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

    public static string? Normalize(string? path) => Places.Normalize(path);

    internal static bool SamePath(string? a, string? b) => Places.SamePath(a, b);

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
        if (_headerForRecycleBin != InRecycleBin)
            BuildHeader();
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
            var read = Task.Run(() => path == Places.RecycleBin ? RecycleBin.Read().Select(i => new FileEntry(i)).ToList()
                                      : Places.IsServer(path) ? ReadServer(path)
                                      : Read(path, hidden, inCloud), cts.Token);
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
            // Lo nuevo y lo que una carga anterior (cancelada por esta) dejó sin ícono o sin carátula.
            bool grid = Settings.Current.FilesGridView;
            toLoad = Merge(fresh).Concat(_entries.Where(e => e.Icon is null || (grid && !e.ThumbnailTried))).Distinct().ToList();
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
        path is null || _places is null ? null
        : _places.Cloud.FirstOrDefault(p => p.Path is { } root && (SamePath(root, path)
                                           || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Sin carátulas en Google Drive (su unidad descarga lo que se lee). En las demás nubes, los
    /// archivos solo en línea ya se saltan uno por uno (<see cref="FileIcons.IsPicture"/>).
    /// </summary>
    private bool Thumbnails(string path) =>
        CloudPlace(path) is not { } cloud || System.IO.Path.GetPathRoot(cloud.Path!)?.TrimEnd('\\') != cloud.Path!.TrimEnd('\\');

    /// <summary>Una carpeta de red que se pudo abrir queda en la barra lateral (su "\\servidor\recurso").</summary>
    private void RememberNetwork(string path)
    {
        if (Places.ShareRoot(path) is not { } share || Settings.Current.FilesNetworkPlaces.Contains(share, StringComparer.OrdinalIgnoreCase)
            || _places?.Network.Any(p => SamePath(p.Path, share)) == true)
            return;
        Settings.Update(s => s.FilesNetworkPlaces = s.FilesNetworkPlaces.Prepend(share).Take(10).ToList());
        PlacesRefreshRequested?.Invoke();
    }

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
            if (!entry.FromSearch && (!incoming.TryGetValue(entry.FullPath, out var updated) || !Same(entry, updated)))
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
        if (path is null || Places.IsServer(path) || path == Places.RecycleBin)
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

    // ── Lo que se ve ────────────────────────────────────────────────────────

    private ListBox ActiveList => Settings.Current.FilesGridView ? GridList : DetailsList;

    public List<FileEntry> SelectedEntries => ActiveList.SelectedItems.OfType<FileEntry>().ToList();

    /// <summary>Ruta, botones y búsqueda.</summary>
    private void UpdateChrome()
    {
        string name = Title;
        BackButton.IsEnabled = _back.Count > 0;
        ForwardButton.IsEnabled = _forward.Count > 0;
        UpButton.IsEnabled = _path != null;
        SearchBox.IsEnabled = _path != null;
        SearchBox.PlaceholderText = _path is null ? Loc.T("Buscar") : Loc.F("Buscar en {0}", name);

        var crumbs = new List<Crumb> { new(_places?.Home.Name ?? Loc.T("Este equipo"), null, true, false) };
        if (_path != null)
        {
            var parts = new List<string>();
            if (InRecycleBin)
            {
                crumbs.Add(new Crumb(Loc.T("Papelera de reciclaje"), _path, false, true));
            }
            else if (Places.IsServer(_path))
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
        StateChanged?.Invoke();
    }

    private void ShowHome(bool animate)
    {
        HomePanel.Visibility = Visibility.Visible;
        DetailsPanel.Visibility = GridList.Visibility = EmptyState.Visibility = RetryButton.Visibility = Visibility.Collapsed;
        StatusText.Text = _places is null ? "" : Loc.F("{0} unidades", _places.Drives.Count);
        if (animate)
            Motion.In(Body, Body, TaskbarPosition.Bottom, 0.5);
        PlacesRefreshRequested?.Invoke();
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

    /// <summary>El host de una ruta de red ("10.50.0.200" de "\\10.50.0.200\Compartida"); null si es local.</summary>
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
        StateChanged?.Invoke();
    }

    private void ApplyView()
    {
        if (_path is null || _view is null)
            return;
        bool grid = Settings.Current.FilesGridView;
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
            EmptyText.Text = _deepSearching ? Loc.T("Buscando también en las subcarpetas…")
                : CanDeepSearch && query.Trim().Length >= 2 ? Loc.F("Nada en esta carpeta ni en sus subcarpetas coincide con «{0}».", query.Trim())
                : Loc.F("Nada en esta carpeta coincide con «{0}».", query.Trim());
        }
        else if (InRecycleBin)
        {
            EmptyIcon.Symbol = SymbolRegular.Delete24;
            EmptyTitle.Text = Loc.T("La Papelera está vacía");
            EmptyText.Text = Loc.T("Lo que borres queda aquí hasta que la vacíes.");
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
        StateChanged?.Invoke();
        SchedulePreview(); // la selección o la carpeta cambiaron
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
        if (_deepSearching)
            text += "   ·   " + Loc.T("buscando en subcarpetas…");
        StatusText.Text = text;
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        _messageTimer.Stop();
        _messageTimer.Start();
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

    // ── Cabecera de la lista y orden ────────────────────────────────────────

    private bool _headerForRecycleBin;

    private void BuildHeader()
    {
        _headerForRecycleBin = InRecycleBin;
        var columns = new (SortKey Key, string Text)[]
        {
            (SortKey.Name, Loc.T("Nombre")),
            (SortKey.Modified, InRecycleBin ? Loc.T("Fecha de eliminación") : Loc.T("Fecha de modificación")),
            (SortKey.Type, InRecycleBin ? Loc.T("Ubicación original") : Loc.T("Tipo")),
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
        FitColumns(ActualWidth);
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

    /// <summary>El menú "Ordenar" bajo el botón de la ventana.</summary>
    public void ShowSortMenu(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        foreach (var item in SortItems())
            menu.Items.Add(item);
        menu.IsOpen = true;
    }

    // ── Acciones ────────────────────────────────────────────────────────────

    private IntPtr Handle => Window.GetWindow(this) is { } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;

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
            // Un comprimido, con el compresor instalado (NanaZip, 7-Zip…) y no con la carpeta comprimida de Windows.
            if (Archivers.IsArchive(entry.FullPath) && Archivers.Open(entry.FullPath))
                continue;
            if (!FileOperations.Open(entry.FullPath))
                ShowMessage(Loc.F("No se pudo abrir {0}", entry.Name));
        }
    }

    private async Task ExtractAsync(FileEntry archive)
    {
        try
        {
            if (await Archivers.ExtractAsync(archive.FullPath) is { } folder)
                _selectAfterLoad = folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowMessage(Loc.F("No se pudo extraer {0}", archive.Name));
            Log.Error($"Archivos: no se pudo extraer {archive.FullPath}", ex);
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

    public void NewFolder()
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

    public void CopySelection(bool cut)
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
        StateChanged?.Invoke();
    }

    public async Task PasteAsync(string? into = null)
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
        StateChanged?.Invoke();
    }

    public async Task RecycleSelectionAsync()
    {
        var selected = SelectedEntries;
        if (selected.Count == 0)
            return;
        if (InRecycleBin)
        {
            await DeleteForeverAsync(selected);
            return;
        }
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

    /// <summary>Lo devuelve a su carpeta.</summary>
    private void Restore(IReadOnlyList<FileEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Recycled is not { } item)
                continue;
            try
            {
                RecycleBin.Restore(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowMessage(Loc.F("No se pudo restaurar {0}", entry.Name));
                Log.Error($"Archivos: no se pudo restaurar {item.OriginalPath}", ex);
            }
        }
        ShowMessageInfo(entries.Count == 1 ? Loc.T("1 elemento restaurado") : Loc.F("{0} elementos restaurados", entries.Count));
        _ = LoadAsync(merge: true);
    }

    /// <summary>Borra para siempre, sin pasar por la Papelera (después de preguntar).</summary>
    private async Task DeleteSelectionForeverAsync(IReadOnlyList<FileEntry> entries)
    {
        await FileOperations.DeleteForeverAsync(entries.Select(e => e.FullPath).ToList(), Handle);
        _reload.Stop();
        _ = LoadAsync(merge: true);
    }

    /// <summary>Borra para siempre lo elegido de la Papelera (después de preguntar).</summary>
    private async Task DeleteForeverAsync(IReadOnlyList<FileEntry> entries)
    {
        var items = entries.Select(e => e.Recycled).OfType<RecycledItem>().ToList();
        if (items.Count == 0)
            return;
        if (!await FileOperations.DeleteForeverAsync(items.Select(i => i.DataPath).ToList(), Handle, items.Select(i => i.Name).ToList()))
            return;
        foreach (var item in items)
        {
            try
            {
                RecycleBin.Forget(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Archivos: no se pudo quitar la ficha de {item.OriginalPath} de la Papelera", ex);
            }
        }
        _ = LoadAsync(merge: true);
    }

    private async void EmptyRecycleBin()
    {
        if (await FileOperations.EmptyRecycleBinAsync(Handle))
            _ = LoadAsync(merge: true);
    }

    public void RenameSelected()
    {
        if (SelectedEntries is [var only])
            StartRename(only);
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

    public void EditPath()
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
        if (Directory.Exists(text) || File.Exists(text) || Places.IsServer(text.TrimEnd('\\')))
        {
            Navigate(text);
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

    internal static MenuItem Item(string header, SymbolRegular? icon, Action action, bool enabled = true, bool bold = false) =>
        FileMenus.Item(header, icon, action, enabled, bold);

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
        if (!onItem)
            list.UnselectAll();
        var selected = SelectedEntries;
        if (InRecycleBin)
        {
            if (onItem && selected.Count > 0)
            {
                menu.Items.Add(Item(Loc.T("Restaurar"), SymbolRegular.ArrowUndo24, () => Restore(selected), bold: true));
                menu.Items.Add(Item(Loc.T("Eliminar definitivamente"), SymbolRegular.Delete24, () => _ = DeleteForeverAsync(selected)));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item(Loc.T("Vaciar la Papelera"), SymbolRegular.DeleteDismiss24, EmptyRecycleBin, enabled: _entries.Count > 0));
            menu.Items.Add(Item(Loc.T("Actualizar"), SymbolRegular.ArrowClockwise24, () => _ = LoadAsync(merge: true)));
            return;
        }
        if (onItem && selected.Count > 0)
        {
            var first = selected[0];
            menu.Items.Add(Item(Loc.T("Abrir"), SymbolRegular.Open24, () => Open(selected), bold: true));
            if (selected.Count == 1 && FolderOf(first) is { } subfolder)
            {
                menu.Items.Add(Item(Loc.T("Abrir en una pestaña nueva"), SymbolRegular.TabAdd24, () => OpenInNewTabRequested?.Invoke(subfolder)));
                bool pinned = Places.IsPinned(subfolder);
                menu.Items.Add(Item(pinned ? Loc.T("Desanclar de la barra lateral") : Loc.T("Anclar a la barra lateral"),
                                    pinned ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, () =>
                                    {
                                        Places.SetPinned(subfolder, !pinned);
                                        PinsChanged?.Invoke();
                                    }));
            }
            if (selected.Count == 1 && !first.IsFolder && Archivers.IsArchive(first.FullPath))
                menu.Items.Add(Item(Loc.T("Extraer en una carpeta"), SymbolRegular.FolderZip24, () => _ = ExtractAsync(first)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Cortar"), SymbolRegular.Cut24, () => CopySelection(cut: true)));
            menu.Items.Add(Item(Loc.T("Copiar"), SymbolRegular.Copy24, () => CopySelection(cut: false)));
            if (selected.Count == 1 && first.IsFolder && FileOperations.ClipboardHasFiles())
                menu.Items.Add(Item(Loc.T("Pegar en esta carpeta"), SymbolRegular.ClipboardPaste24, () => _ = PasteAsync(first.FullPath)));
            menu.Items.Add(Item(Loc.T("Copiar ruta"), SymbolRegular.DocumentCopy24, () =>
                Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(s => $"\"{s.FullPath}\"")))));
            if (selected.Count == 1)
            {
                bool inStart = StartMenuService.IsPinned(first.FullPath);
                menu.Items.Add(Item(inStart ? Loc.T("Desanclar de Inicio") : Loc.T("Anclar a Inicio"),
                                    inStart ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, () =>
                                    {
                                        if (inStart)
                                            StartMenuService.UnpinPath(first.FullPath);
                                        else
                                            StartMenuService.PinPaths([first.FullPath]);
                                    }));
            }
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
            AddWindowsItems(menu, selected.Select(s => s.FullPath).ToList(), background: false);
        }
        else
        {
            menu.Items.Add(Item(Loc.T("Nueva carpeta"), SymbolRegular.FolderAdd24, NewFolder));
            menu.Items.Add(Item(Loc.T("Pegar"), SymbolRegular.ClipboardPaste24, () => _ = PasteAsync(), enabled: FileOperations.ClipboardHasFiles()));
            menu.Items.Add(new Separator());
            menu.Items.Add(SortMenu());
            var view = new MenuItem { Header = Loc.T("Vista"), Icon = new SymbolIcon { Symbol = SymbolRegular.Grid24 } };
            var details = Item(Loc.T("Lista"), SymbolRegular.TextBulletListLtr24, () => FilesWindow.SetGridView(false));
            details.IsChecked = !Settings.Current.FilesGridView;
            var grid = Item(Loc.T("Cuadrícula"), SymbolRegular.Grid24, () => FilesWindow.SetGridView(true));
            grid.IsChecked = Settings.Current.FilesGridView;
            view.Items.Add(details);
            view.Items.Add(grid);
            menu.Items.Add(view);
            var hidden = Item(Loc.T("Mostrar archivos ocultos"), SymbolRegular.Eye24, () => FilesWindow.SetShowHidden(!Settings.Current.FilesShowHidden));
            hidden.IsChecked = Settings.Current.FilesShowHidden;
            menu.Items.Add(hidden);
            var extensions = Item(Loc.T("Mostrar las extensiones de los archivos"), null, () => FilesWindow.SetShowExtensions(!FileEntry.ShowExtensions));
            extensions.IsChecked = FileEntry.ShowExtensions;
            menu.Items.Add(extensions);
            menu.Items.Add(Item(Loc.T("Actualizar"), SymbolRegular.ArrowClockwise24, () => _ = LoadAsync(merge: true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Abrir en el Explorador de Windows"), SymbolRegular.FolderOpen24,
                                () => FileOperations.ShowInExplorer(folder, select: false)));
            menu.Items.Add(Item(Loc.T("Abrir terminal aquí"), SymbolRegular.WindowConsole20, () => FileOperations.OpenTerminal(folder)));
            if (!Places.IsServer(folder))
                AddWindowsItems(menu, [folder], background: true);
        }
    }

    internal static void AddWindowsItems(ContextMenu menu, IReadOnlyList<string> paths, bool background, bool desktop = false) =>
        FileMenus.AddWindowsItems(menu, paths, background, desktop);

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

    /// <summary>Clic con la rueda en una carpeta: se abre en una pestaña nueva, como en un navegador.</summary>
    private void List_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is FileEntry entry
            && FolderOf(entry) is { } folder)
        {
            OpenInNewTabRequested?.Invoke(folder);
            e.Handled = true;
        }
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
            // Clic en el espacio libre: nada seleccionado (con Ctrl se suma a lo que ya había) y empieza el recuadro.
            _dragStart = null;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                list.UnselectAll();
            list.Focus();
            BeginMarquee(list, e);
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
        EndMarquee();
        if (_clickedSelected is { } entry)
            ((ListBox)sender).SelectedItem = entry;
        _clickedSelected = null;
        _dragStart = null;
    }

    /// <summary>Arrastrar la selección afuera (a otra app, al escritorio, al otro panel) o a una carpeta de aquí.</summary>
    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_marquee is { } marquee)
        {
            UpdateMarquee(e.GetPosition(marquee.Scroll));
            return;
        }
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

    // ── Recuadro de selección ───────────────────────────────────────────────

    /// <summary>
    /// Un recuadro en curso. El ancla va en coordenadas del contenido (con el desplazamiento) para que
    /// siga en su lugar si la lista se desplaza. Con la virtualización solo existen los elementos a la
    /// vista: los que salen conservan lo que tenían la última vez que se vieron.
    /// </summary>
    private sealed class MarqueeState(ListBox list, ScrollViewer scroll, Point anchor, List<FileEntry> initial)
    {
        public ListBox List { get; } = list;
        public ScrollViewer Scroll { get; } = scroll;
        public Point Anchor { get; } = anchor;
        public List<FileEntry> Initial { get; } = initial;
        public HashSet<FileEntry> Hits { get; } = [];
        public Point Last { get; set; }
    }

    private MarqueeState? _marquee;
    private DispatcherTimer? _marqueeScroll;

    private static Vector Offset(ScrollViewer scroll) => new(scroll.HorizontalOffset, scroll.VerticalOffset);

    private void BeginMarquee(ListBox list, MouseButtonEventArgs e)
    {
        if (Descendant<ScrollViewer>(list) is not { } scroll)
            return;
        var point = e.GetPosition(scroll);
        _marquee = new MarqueeState(list, scroll, point + Offset(scroll), list.SelectedItems.OfType<FileEntry>().ToList()) { Last = point };
        list.CaptureMouse();
        list.LostMouseCapture += Marquee_LostMouseCapture;
        e.Handled = true;
    }

    private void Marquee_LostMouseCapture(object sender, MouseEventArgs e) => EndMarquee();

    private void EndMarquee()
    {
        if (_marquee is not { } marquee)
            return;
        _marquee = null;
        _marqueeScroll?.Stop();
        marquee.List.LostMouseCapture -= Marquee_LostMouseCapture;
        marquee.List.ReleaseMouseCapture();
        Marquee.Visibility = Visibility.Collapsed;
    }

    private void UpdateMarquee(Point point)
    {
        if (_marquee is not { } marquee)
            return;
        marquee.Last = point;
        var scroll = marquee.Scroll;
        var offset = Offset(scroll);
        var area = new Rect(marquee.Anchor, point + offset);

        // El dibujo, en la capa de encima de la lista.
        var topLeft = scroll.TranslatePoint(area.TopLeft - offset, MarqueeLayer);
        Canvas.SetLeft(Marquee, topLeft.X);
        Canvas.SetTop(Marquee, topLeft.Y);
        Marquee.Width = area.Width;
        Marquee.Height = area.Height;
        Marquee.Visibility = area.Width > 2 || area.Height > 2 ? Visibility.Visible : Visibility.Collapsed;

        // Los elementos a la vista que toca.
        if (Descendant<VirtualizingPanel>(marquee.List) is { } panel)
        {
            foreach (var container in panel.Children.OfType<ListBoxItem>())
            {
                if (container.DataContext is not FileEntry entry || !container.IsVisible)
                    continue;
                var bounds = container.TransformToAncestor(scroll).TransformBounds(new Rect(container.RenderSize));
                bounds.Offset(offset);
                if (bounds.IntersectsWith(area))
                    marquee.Hits.Add(entry);
                else
                    marquee.Hits.Remove(entry);
            }
        }
        var wanted = new HashSet<FileEntry>(marquee.Initial);
        wanted.UnionWith(marquee.Hits);
        var selected = marquee.List.SelectedItems;
        foreach (var entry in selected.OfType<FileEntry>().Where(s => !wanted.Contains(s)).ToList())
            selected.Remove(entry);
        foreach (var entry in wanted.Where(w => !selected.Contains(w)))
            selected.Add(entry);

        // Fuera de la lista (arriba o abajo), se desplaza sola. ActualHeight: el panel en cuadrícula no da su
        // ViewportHeight en píxeles de pantalla.
        bool outside = point.Y < 0 || point.Y > scroll.ActualHeight;
        if (outside)
        {
            _marqueeScroll ??= CreateMarqueeScroll();
            _marqueeScroll.Start();
        }
        else
        {
            _marqueeScroll?.Stop();
        }
    }

    private DispatcherTimer CreateMarqueeScroll()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (_marquee is not { } marquee)
            {
                timer.Stop();
                return;
            }
            var scroll = marquee.Scroll;
            double y = marquee.Last.Y;
            double distance = y < 0 ? y : y > scroll.ActualHeight ? y - scroll.ActualHeight : 0;
            if (distance == 0)
                return;
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + Math.Clamp(distance / 2, -40, 40));
            scroll.UpdateLayout();
            UpdateMarquee(marquee.Last);
        };
        return timer;
    }

    private static T? Descendant<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T found)
                return found;
            if (Descendant<T>(child) is { } deeper)
                return deeper;
        }
        return null;
    }

    /// <summary>Dónde cae lo que se suelta: la carpeta bajo el puntero o la actual.</summary>
    private string? DropTarget(DragEventArgs e) =>
        Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is FileEntry { IsFolder: true } folder
            ? folder.FullPath
            : _path;

    /// <summary>Como el Explorador: misma unidad mueve, otra copia; Ctrl copia y Mayús mueve.</summary>
    internal static DragDropEffects DropEffect(DragEventArgs e, string target, string[] files)
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
            e.Effects = InRecycleBin
                ? (e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move & e.AllowedEffects : DragDropEffects.None)
                : DropTarget(e) is { } target && e.Data.GetData(DataFormats.FileDrop) is string[] files
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
            if (InRecycleBin && e.Data.GetData(DataFormats.FileDrop) is string[] recycled)
            {
                e.Handled = true;
                await FileOperations.RecycleAsync(recycled, Handle);
                _ = LoadAsync(merge: true);
                return;
            }
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

    /// <summary>Los atajos de una carpeta; true si lo atendió (la ventana atiende antes los de pestañas).</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool typing = Keyboard.FocusedElement is TextBox;
        switch (key)
        {
            case Key.Left when mods == ModifierKeys.Alt:
            case Key.BrowserBack:
                GoBack();
                return true;
            case Key.Right when mods == ModifierKeys.Alt:
            case Key.BrowserForward:
                GoForward();
                return true;
            case Key.Up when mods == ModifierKeys.Alt:
                GoUp();
                return true;
            case Key.F5:
                _ = LoadAsync(merge: true);
                return true;
            case Key.L when mods == ModifierKeys.Control:
            case Key.D when mods == ModifierKeys.Alt:
            case Key.F4 when mods == ModifierKeys.None:
                EditPath();
                return true;
            case Key.F when mods == ModifierKeys.Control:
            case Key.F3:
                SearchBox.Focus();
                return true;
        }
        if (typing)
            return false;
        switch (key)
        {
            case Key.Back:
                GoBack();
                return true;
            case Key.Enter:
                Open(SelectedEntries);
                return true;
            case Key.F2 when CanModify && SelectedEntries is [var only]:
                StartRename(only);
                return true;
            // Como en el Explorador: Mayús+Supr borra para siempre (Windows pregunta antes).
            case Key.Delete when mods == ModifierKeys.Shift && CanModify && SelectedEntries is { Count: > 0 } selected:
                _ = DeleteSelectionForeverAsync(selected);
                return true;
            case Key.Delete:
                _ = RecycleSelectionAsync();
                return true;
            case Key.C when mods == ModifierKeys.Control:
                CopySelection(cut: false);
                return true;
            case Key.X when mods == ModifierKeys.Control && CanModify:
                CopySelection(cut: true);
                return true;
            case Key.V when mods == ModifierKeys.Control && CanModify:
                _ = PasteAsync();
                return true;
            case Key.N when mods == (ModifierKeys.Control | ModifierKeys.Shift) && CanModify:
                NewFolder();
                return true;
            case Key.A when mods == ModifierKeys.Control:
                ActiveList.SelectAll();
                return true;
            case Key.Escape when SearchBox.Text.Length > 0:
                SearchBox.Text = "";
                return true;
        }
        return false;
    }

    // ── Controles ───────────────────────────────────────────────────────────

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => GoForward();
    private void Up_Click(object sender, RoutedEventArgs e) => GoUp();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(merge: true);

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Place place)
            Navigate(place.Path);
    }

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
        ScheduleDeepSearch();
        _view?.Refresh();
        UpdateEmptyState();
        UpdateStatus();
    }

    // ── Vista previa ────────────────────────────────────────────────────────
    // A la derecha (Alt+P): imagen, primera página de un PDF, el comienzo de un texto o la carátula
    // de Windows (video, música, Office), con su tipo, tamaño y fecha. Nada se lee de un archivo
    // que solo está en la nube (lo descargaría).

    private readonly DispatcherTimer _previewTimer;
    private int _previewVersion;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".json", ".xml", ".csv", ".ini", ".cfg", ".conf", ".yaml", ".yml", ".toml", ".cs", ".xaml",
        ".js", ".ts", ".tsx", ".jsx", ".css", ".html", ".htm", ".py", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".c", ".h",
        ".cpp", ".hpp", ".java", ".kt", ".go", ".rs", ".lua", ".sql", ".reg", ".gitignore", ".editorconfig", ".csproj", ".props",
    };

    /// <summary>Se mostró u ocultó el panel en la ventana (para todas las carpetas).</summary>
    public void PreviewChanged() => SchedulePreview();

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task UpdatePreviewAsync()
    {
        int version = ++_previewVersion;
        bool show = Settings.Current.FilesPreviewPane && _path != null;
        PreviewPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;
        var selected = SelectedEntries;
        PreviewImage.Source = null;
        PreviewText.Text = "";
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewEmpty.Visibility = Visibility.Collapsed;
        PreviewInfo.Visibility = Visibility.Visible;
        if (selected.Count != 1)
        {
            PreviewInfo.Visibility = selected.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            PreviewEmpty.Visibility = Visibility.Visible;
            PreviewEmptyText.Text = selected.Count > 1 ? "" : Loc.T("Elige un archivo para verlo aquí.");
            PreviewName.Text = Loc.F("{0} seleccionados", selected.Count);
            long bytes = selected.Where(e => !e.IsFolder).Sum(e => e.Size);
            PreviewDetails.Text = selected.Any(e => !e.IsFolder) ? FileEntry.FormatSize(bytes) : "";
            return;
        }

        var entry = selected[0];
        PreviewName.Text = entry.Name;
        var lines = new List<string> { entry.TypeName };
        if (!entry.IsFolder && entry.Size >= 0)
            lines.Add(entry.SizeText);
        if (entry.ModifiedText.Length > 0)
            lines.Add(Loc.F("Modificado: {0}", entry.ModifiedText));
        PreviewDetails.Text = string.Join("\n", lines);
        PreviewImage.Source = entry.Thumbnail ?? entry.Icon;

        string path = entry.FullPath;
        try
        {
            if (entry.IsFolder)
            {
                int? count = await Task.Run(() => CountItems(path));
                if (version == _previewVersion && count is { } n)
                    PreviewDetails.Text = string.Join("\n", lines.Prepend(n == 1 ? Loc.T("1 elemento") : Loc.F("{0} elementos", n)));
                return;
            }
            if (entry.IsOnlineOnly)
                return;
            if (TextExtensions.Contains(entry.Extension) && entry.Size <= 4 * 1024 * 1024)
            {
                string text = await Task.Run(() => ReadStart(path));
                if (version != _previewVersion)
                    return;
                PreviewImage.Source = null;
                PreviewText.Text = text;
                PreviewText.Visibility = Visibility.Visible;
                return;
            }
            ImageSource? image = FileIcons.IsPicture(entry)
                ? await FileIcons.RunAsync(() => AppIcons.Load(path, 560))
                : entry.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? await PdfFirstPageAsync(path)
                    : await Task.Run(() => ThumbnailHost.Get(path, 512, cachedOnly: false));
            if (version != _previewVersion || image is null)
                return;
            PreviewImage.Source = image;
            // El tamaño real (la vista previa se decodifica más chica).
            if (FileIcons.IsPicture(entry) && await Task.Run(() => PixelSize(path)) is { } size && version == _previewVersion)
                PreviewDetails.Text = string.Join("\n", lines.Append($"{size.Width} × {size.Height}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            // Sin vista previa: quedan el ícono y los datos.
        }
    }

    private static async Task<ImageSource?> PdfFirstPageAsync(string path)
    {
        var document = await Previews.OpenPdfAsync(path);
        return document is { PageCount: > 0 } ? await Previews.PdfPageAsync(document, 0, 560) : null;
    }

    /// <summary>Lo primero de un texto (200 líneas): lo bastante para reconocerlo sin leerlo entero.</summary>
    private static string ReadStart(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), detectEncodingFromByteOrderMarks: true);
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < 200 && reader.ReadLine() is { } line; i++)
            text.AppendLine(line.Length > 400 ? line[..400] + "…" : line);
        return text.ToString();
    }

    /// <summary>Ancho y alto de una imagen sin decodificarla entera (solo su encabezado).</summary>
    private static (int Width, int Height)? PixelSize(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                      or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static int? CountItems(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFileSystemInfos().Take(10000).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    // ── Búsqueda en las subcarpetas ─────────────────────────────────────────
    // Lo de esta carpeta se filtra al escribir; con una pausa, se busca también más abajo y lo que
    // aparece se agrega a la lista (con la carpeta donde está en lugar del tipo).

    private readonly DispatcherTimer _deepSearchTimer;
    private CancellationTokenSource? _deepSearch;
    private bool _deepSearching;
    private const int DeepSearchLimit = 1000;

    private bool CanDeepSearch => _path != null && !InRecycleBin && !Places.IsServer(_path);

    /// <summary>Cambió el texto: se descarta lo encontrado antes y, si hay algo que buscar, se espera una pausa.</summary>
    private void ScheduleDeepSearch()
    {
        _deepSearchTimer.Stop();
        StopDeepSearch();
        foreach (var found in _entries.Where(entry => entry.FromSearch).ToList())
            _entries.Remove(found);
        if (SearchBox.Text.Trim().Length >= 2 && CanDeepSearch)
            _deepSearchTimer.Start();
    }

    private void StopDeepSearch()
    {
        _deepSearch?.Cancel();
        _deepSearch = null;
        _deepSearching = false;
    }

    private void StartDeepSearch()
    {
        string query = SearchBox.Text.Trim();
        if (query.Length < 2 || !CanDeepSearch || _path is not { } root)
            return;
        var cts = _deepSearch = new CancellationTokenSource();
        var token = cts.Token;
        bool hidden = Settings.Current.FilesShowHidden, inCloud = CloudPlace(root) != null;
        bool thumbnails = Thumbnails(root);
        _deepSearching = true;
        UpdateEmptyState();
        UpdateStatus();
        void Deliver(List<FileEntry> batch) => Dispatcher.BeginInvoke(() =>
        {
            if (token.IsCancellationRequested || !SamePath(_path, root))
                return;
            foreach (var entry in batch)
                _entries.Add(entry);
            FileIcons.Load(batch, thumbnails, Dispatcher, token);
            UpdateEmptyState();
            UpdateStatus();
        });
        _ = Task.Run(() =>
        {
            var batch = new List<FileEntry>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int count = 0;
            try
            {
                var skip = FileAttributes.System | FileAttributes.ReparsePoint | (hidden ? 0 : FileAttributes.Hidden);
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = skip };
                foreach (var folder in new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = skip }))
                {
                    // Lo de la carpeta misma ya está en la lista: se busca dentro de cada subcarpeta.
                    foreach (var info in folder.EnumerateFileSystemInfos("*", options))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!info.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                            continue;
                        bool isFolder = info is DirectoryInfo;
                        string where = System.IO.Path.GetRelativePath(root, System.IO.Path.GetDirectoryName(info.FullName)!);
                        string type = FileIcons.TypeName(isFolder ? "" : info.Extension, isFolder);
                        batch.Add(new FileEntry(info, Loc.F("{0} · en {1}", type, where), inCloud) { FromSearch = true });
                        if (++count >= DeepSearchLimit)
                            break;
                        // De a poco: lo primero que aparece se ve enseguida.
                        if (watch.ElapsedMilliseconds > 150)
                        {
                            Deliver(batch);
                            batch = [];
                            watch.Restart();
                        }
                    }
                    if (count >= DeepSearchLimit)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Una carpeta que no se deja leer: lo encontrado hasta ahí se muestra igual.
            }
            Deliver(batch);
            Dispatcher.BeginInvoke(() =>
            {
                if (_deepSearch != cts)
                    return;
                _deepSearching = false;
                UpdateEmptyState();
                UpdateStatus();
            });
        }, token);
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
