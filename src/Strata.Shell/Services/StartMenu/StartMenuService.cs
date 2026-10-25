using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Strata.Shell.Services.Files;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using ManagedShell.Common.Helpers;
using ManagedShell.ShellFolders;

namespace Strata.Shell.Services.StartMenu;

/// <summary>Una app del menú Inicio (entrada de shell:AppsFolder) o un archivo o carpeta anclado.</summary>
public sealed class StartApp : INotifyPropertyChanged
{
    private readonly ShellItem _item;
    private readonly string? _path;
    private readonly ExtraApp? _extra;

    /// <summary>App de fuera de shell:AppsFolder (juego de Steam, Epic, GOG o acceso directo del escritorio).</summary>
    internal StartApp(ShellItem iconItem, ExtraApp extra) : this(iconItem)
    {
        _extra = extra;
    }

    /// <param name="path">Ruta del archivo, acceso directo o carpeta anclado (null en las apps del catálogo).</param>
    internal StartApp(ShellItem item, string? path = null)
    {
        _item = item;
        _path = path;
        if (item is INotifyPropertyChanged notifier)
            notifier.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ShellItem.LargeIcon) or nameof(ShellItem.DisplayName))
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(e.PropertyName == nameof(ShellItem.LargeIcon) ? nameof(Icon) : nameof(Name)));
            };
    }

    /// <summary>Identificador estable: AppUserModelID, ruta del acceso directo o del archivo anclado.</summary>
    public string Id => _extra?.Id ?? _path ?? _item.FileName ?? _item.Path;
    public string Name => _extra?.Name ?? _item.DisplayName ?? Id;
    /// <summary>El ícono elegido con "Cambiar ícono" o, si no hay, el de Windows.</summary>
    public ImageSource? Icon => CustomIcons.Get(Id) ?? _item.LargeIcon;
    public string Group => GroupOf(Name);

    /// <summary>Archivo, acceso directo o carpeta anclado (no está en el catálogo de apps).</summary>
    public bool IsFile => _path != null;

    public bool IsPinned => StartMenuService.IsPinned(Id);

    // Para el menú contextual (se consultan al abrirlo).
    public bool CanOpenLocation => AppActions.CanOpenLocation(this);
    public bool CanRunAsAdmin => AppActions.CanRunAsAdmin(this);
    public bool CanUninstall => !IsFile && _extra is null && AppActions.CanUninstall(this);

    /// <summary>Herramienta de Windows (Servicios, Desfragmentar…): no va en "Todas las apps", sí en la búsqueda.</summary>
    public bool IsSystemTool => _systemTool ??= !IsFile && _extra is null && SystemTools.IsTool(Id);
    private bool? _systemTool;

    /// <summary>No viene de shell:AppsFolder (ver <see cref="ExtraApps"/>).</summary>
    public bool IsExtra => _extra != null;
    internal ExtraApp? ExtraData => _extra;
    public bool HasCustomIcon => CustomIcons.Has(Id);

    internal void NotifyPinned() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));

    public bool IsPinnedToTaskbar => TaskbarPins.IsPinned(Id);

    internal void NotifyTaskbarPinned() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinnedToTaskbar)));

    internal void NotifyIcon()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCustomIcon)));
    }

    /// <summary>ShellItem del archivo anclado (para liberarlo al desanclarlo).</summary>
    internal ShellItem Item => _item;

    public void Launch()
    {
        try
        {
            Process.Start(_extra is { } extra
                ? new ProcessStartInfo(extra.Launch) { UseShellExecute = true, WorkingDirectory = extra.WorkingDirectory ?? "" }
                : IsFile
                ? new ProcessStartInfo(Id) { UseShellExecute = true }
                : new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{Id}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir {Name}", ex);
        }
    }

    private static string GroupOf(string name)
    {
        char c = string.IsNullOrEmpty(name) ? '#' : char.ToUpperInvariant(name.Normalize(System.Text.NormalizationForm.FormD)[0]);
        return char.IsLetter(c) ? c.ToString() : "#";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Archivo reciente (acceso directo de la carpeta Recent).</summary>
public sealed record RecentItem(string Name, string Path, DateTime When, ImageSource? Icon)
{
    public string WhenText => (DateTime.Now - When).TotalHours < 24 ? When.ToString("t") : When.ToString("d");

    public void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir {Name}", ex);
        }
    }
}

/// <summary>
/// Datos del menú Inicio: catálogo de apps (el mismo que usa Windows), anclados, recientes,
/// usuario y acciones de energía. Es único para toda la app y se carga en segundo plano.
/// </summary>
public static class StartMenuService
{
    private static ShellFolder? _appsFolder;

    // Apps comunes que se anclan la primera vez si están instaladas (por fragmento del ID o nombre).
    private static readonly string[] DefaultPins =
    [
        "Microsoft.Windows.Explorer", "windows.immersivecontrolpanel", "Brave", "Chrome", "MSEdge", "Firefox",
        "Microsoft.WindowsStore", "Microsoft.WindowsTerminal", "Microsoft.WindowsNotepad", "Microsoft.WindowsCalculator",
        "Microsoft.Windows.Photos", "Microsoft.Paint", "Steam", "Discord", "Spotify", "Code",
    ];

    public static ObservableCollection<StartApp> Apps { get; } = new();
    public static ObservableCollection<StartApp> Pinned { get; } = new();

    public static string UserName { get; } = GetUserDisplayName();

    private static readonly Lazy<ImageSource?> Picture = new(LoadUserPicture);

    /// <summary>Foto de la cuenta de Windows (null si no tiene).</summary>
    public static ImageSource? UserPicture => Picture.Value;

    /// <summary>Windows guarda la ruta de la foto, en varios tamaños, por SID del usuario.</summary>
    private static ImageSource? LoadUserPicture()
    {
        try
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}");
            var path = new[] { "Image192", "Image240", "Image96", "Image448" }
                .Select(name => key?.GetValue(name) as string)
                .FirstOrDefault(p => p != null && File.Exists(p));
            if (path is null)
                return null;
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 96;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cargar la foto de la cuenta", ex);
            return null;
        }
    }
    public static string UserInitials => string.Concat(UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                                              .Take(2).Select(w => char.ToUpperInvariant(w[0])));

    private static System.Windows.Threading.DispatcherTimer? _rebuildTimer;

    public static void Initialize()
    {
        ApplyIconSizes();
        Settings.Changed += ApplyIconSizes;
        // La carpeta se llena de forma asíncrona (cientos de eventos): se reconstruye la lista
        // cuando deja de cambiar durante un momento.
        _rebuildTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            Rebuild(onlyIfChanged: true);
        };
        _appsFolder = OpenAppsFolder(() =>
        {
            _rebuildTimer.Stop();
            _rebuildTimer.Start();
        });
        _rebuildTimer.Start();
        CustomIcons.Changed += id =>
        {
            foreach (var app in Apps.Concat(_files.Values).Where(a => a.Id == id))
                app.NotifyIcon();
        };
        WatchStartFolders();
        WatchUninstallKeys();
        ScanExtras();
    }

    public static void Shutdown()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _appsFolder?.Dispose();
        _pendingFolder?.Dispose();
    }

    private static ShellFolder OpenAppsFolder(Action changed)
    {
        var folder = new ShellFolder("shell:AppsFolder", IntPtr.Zero, loadAsync: true, watchChanges: true);
        folder.Files.CollectionChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(changed);
        return folder;
    }

    // ── Volver a leer el catálogo ───────────────────────────────────────────
    // El aviso de cambios de shell:AppsFolder no siempre llega al desinstalar (ni al instalar una
    // app web): se vigilan las carpetas del menú Inicio y, si cambian, se lee el catálogo de nuevo.

    private static readonly List<FileSystemWatcher> _watchers = new();
    private static ShellFolder? _pendingFolder;
    private static System.Windows.Threading.DispatcherTimer? _refreshTimer, _swapTimer;
    private static bool _secondPass;

    private static void WatchStartFolders()
    {
        // También las bibliotecas de Steam y Epic y el escritorio: instalar o quitar un juego, o un
        // acceso directo de programa, cambia la lista.
        var extra = ExtraApps.WatchedFolders()
            .Concat(new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory }
                        .Select(Environment.GetFolderPath))
            .Where(Directory.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms }
                     .Select(Environment.GetFolderPath).Where(Directory.Exists).Concat(extra))
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = !extra.Contains(root),
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                };
                FileSystemEventHandler changed = (_, _) => Application.Current?.Dispatcher.BeginInvoke(Refresh);
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Renamed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Refresh);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Log.Error($"No se pudo vigilar {root}", ex);
            }
        }
    }

    /// <summary>
    /// Vuelve a leer el catálogo de apps (después de instalar o desinstalar). La lista nueva se
    /// carga aparte y reemplaza a la actual cuando termina, así el menú no queda vacío a medias.
    /// </summary>
    public static void Refresh()
    {
        if (_appsFolder is null)
            return;
        // Un desinstalador borra varios accesos directos seguidos, y Windows tarda unos 3 s en
        // reflejarlos en shell:AppsFolder: se lee 4 s después del último cambio y otra vez por si acaso.
        if (_refreshTimer is null)
        {
            _refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _refreshTimer.Tick += OnRefreshTick;
        }
        _secondPass = true;
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private static DateTime _lastRead = DateTime.UtcNow;

    /// <summary>
    /// Al abrir el menú: si el catálogo se leyó hace más de medio minuto, se vuelve a leer ya (las
    /// apps de la Tienda se instalan sin tocar las carpetas que se vigilan).
    /// </summary>
    public static void RefreshIfStale()
    {
        if (_appsFolder is null || DateTime.UtcNow - _lastRead < TimeSpan.FromSeconds(30) || _refreshTimer?.IsEnabled == true)
            return;
        _secondPass = false;
        OnRefreshTick(null, EventArgs.Empty);
    }

    /// <summary>
    /// La lista de programas instalados del registro (la de "Aplicaciones instaladas"): la escribe
    /// cualquier instalador o desinstalador, también los que no ponen accesos directos en Inicio.
    /// </summary>
    private static void WatchUninstallKeys()
    {
        var thread = new Thread(() =>
        {
            const string path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            var keys = new List<Microsoft.Win32.RegistryKey>();
            foreach (var (hive, view) in new[] { (Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Default),
                                                 (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64),
                                                 (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32) })
            {
                try
                {
                    if (Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path) is { } key)
                        keys.Add(key);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                }
            }
            if (keys.Count == 0)
                return;
            var events = keys.Select(_ => new AutoResetEvent(false)).ToArray();
            while (true)
            {
                for (int i = 0; i < keys.Count; i++)
                    RegNotifyChangeKeyValue(keys[i].Handle.DangerousGetHandle(), true, RegNotifyName | RegNotifyLastSet,
                                            events[i].SafeWaitHandle.DangerousGetHandle(), true);
                WaitHandle.WaitAny(events);
                Application.Current?.Dispatcher.BeginInvoke(Refresh);
            }
        }) { IsBackground = true, Name = "Strata: programas instalados" };
        thread.Start();
    }

    private const int RegNotifyName = 0x1, RegNotifyLastSet = 0x4;

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(IntPtr key, bool watchSubtree, int filter, IntPtr eventHandle, bool asynchronous);

    private static void OnRefreshTick(object? sender, EventArgs e)
    {
        _refreshTimer?.Stop();
        _lastRead = DateTime.UtcNow;
        ScanExtras();
        _pendingFolder?.Dispose();
        _swapTimer ??= CreateSwapTimer();
        _pendingFolder = OpenAppsFolder(() =>
        {
            _swapTimer.Stop();
            _swapTimer.Start();
        });
        // Si la carpeta nueva carga sin avisar cambios, igual se cambia (hasta ~5 s de espera).
        _swapTries = 0;
        _swapTimer.Start();
    }

    private static int _swapTries;

    private static System.Windows.Threading.DispatcherTimer CreateSwapTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_pendingFolder is not { } fresh)
                return;
            if (fresh.Files.Count == 0)
            {
                if (++_swapTries < 10)
                    timer.Start(); // todavía cargando
                return;
            }
            var old = _appsFolder;
            _pendingFolder = null;
            _appsFolder = fresh;
            // Los cambios posteriores de la carpeta nueva van al reconstruir de siempre.
            fresh.Files.CollectionChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _rebuildTimer!.Stop();
                _rebuildTimer.Start();
            });
            Rebuild();
            old?.Dispose();
            if (_secondPass)
            {
                _secondPass = false;
                _refreshTimer!.Start();
            }
        };
        return timer;
    }

    // ── Archivos y carpetas anclados ────────────────────────────────────────

    private static readonly Dictionary<string, StartApp> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Anclado que es una ruta (archivo, acceso directo o carpeta) en lugar de una app.</summary>
    internal static bool IsPathId(string id) => Path.IsPathFullyQualified(id);

    /// <summary>El archivo o carpeta anclado como app del menú (null si ya no existe).</summary>
    internal static StartApp? FileApp(string path)
    {
        if (_files.TryGetValue(path, out var app))
            return app;
        if (!File.Exists(path) && !Directory.Exists(path))
            return null;
        try
        {
            return _files[path] = new StartApp(new ShellItem(path), path);
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo leer {path}", ex);
            return null;
        }
    }

    /// <summary>Libera los archivos que ya no están anclados.</summary>
    internal static void ForgetFiles(ICollection<string> pinned)
    {
        foreach (var path in _files.Keys.Where(p => !pinned.Contains(p)).ToList())
        {
            _files[path].Item.Dispose();
            _files.Remove(path);
        }
    }

    /// <summary>Desancla de Inicio un archivo, acceso directo o carpeta.</summary>
    public static void UnpinPath(string path)
    {
        if (IsPathId(path) && FileApp(path) is { } app)
            SetPinned(app, false);
    }

    /// <summary>Ancla archivos, accesos directos o carpetas en Inicio.</summary>
    public static void PinPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(IsPathId))
            if (FileApp(path) is { } app)
                SetPinned(app, true);
    }

    /// <summary>
    /// Tamaños de íconos del menú Inicio como recursos dinámicos: las plantillas los leen con
    /// DynamicResource, así que cambiar la opción en el Hub se refleja sin reabrir nada.
    /// </summary>
    public static void ApplyIconSizes() => ApplyIconSizes(IconPixels(Settings.Current));

    /// <summary>Límites del tamaño de los íconos (clic derecho en Inicio › Ver).</summary>
    public const double MinIconPixels = 20, MaxIconPixels = 96;

    /// <summary>Lo que medían chico, mediano y grande (antes de poder elegir cualquier tamaño).</summary>
    internal static double PixelsOf(StartIconSize size) => size switch
    {
        StartIconSize.Small => 26,
        StartIconSize.Large => 44,
        _ => 32,
    };

    public static double IconPixels(StrataSettings s) =>
        s.StartIconPixels > 0 ? Math.Clamp(s.StartIconPixels, MinIconPixels, MaxIconPixels) : PixelsOf(s.StartIconSize);

    /// <summary>Aplica un tamaño sin guardarlo (mientras se mueve el deslizador).</summary>
    public static void ApplyIconSizes(double tile)
    {
        // Las listas y cuadrículas crecen menos que los anclados, como con los tamaños fijos de antes.
        double row = Math.Round(Math.Clamp(tile * 0.75, 16, 48));
        double grid = Math.Round(tile * 1.18);
        double large = Math.Round(tile * 1.5);
        bool labels = Settings.Current.StartShowPinnedLabels;
        var resources = Application.Current.Resources;
        resources["StartTileIconSize"] = tile;
        // Sin nombres, el mosaico solo necesita el ícono y su margen.
        resources["StartTileWidth"] = labels ? Math.Max(tile + 64, Math.Round(tile * 1.6)) : tile + 32;
        resources["StartTileLabelVisibility"] = labels ? Visibility.Visible : Visibility.Collapsed;
        resources["StartTileIconMargin"] = new Thickness(0, 0, 0, labels ? 6 : 0);
        resources["StartRowIconSize"] = row;
        resources["StartGridIconSize"] = grid;
        resources["StartGridButtonSize"] = grid + 26;
        resources["StartLargeIconSize"] = large;
        resources["StartLargeTileWidth"] = large * 2.6;
    }

    // ── Apps de fuera del catálogo de Windows (ver ExtraApps) ───────────────

    private static List<ExtraApp> _extraFound = new();
    private static readonly Dictionary<string, StartApp> _extraApps = new(StringComparer.OrdinalIgnoreCase);
    private static bool _scanning;

    /// <summary>Busca juegos y accesos directos en segundo plano y vuelve a armar la lista con ellos.</summary>
    private static async void ScanExtras()
    {
        if (_scanning)
            return;
        _scanning = true;
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _extraFound = await Task.Run(ExtraApps.Scan);
            Log.Info($"Apps de fuera del catálogo ({watch.ElapsedMilliseconds} ms): " + string.Join(" | ", _extraFound.Select(a => a.Name)));
            // Con una lectura nueva del catálogo en camino, ella arma la lista (ya con estos juegos).
            if (_pendingFolder is null)
                Rebuild();
        }
        finally
        {
            _scanning = false;
        }
    }

    /// <summary>La misma entrada mientras no cambie (así no se vuelve a leer su ícono).</summary>
    private static StartApp? ExtraStartApp(ExtraApp extra)
    {
        if (_extraApps.TryGetValue(extra.Id, out var known) && known.ExtraData == extra)
            return known;
        try
        {
            return _extraApps[extra.Id] = new StartApp(new ShellItem(extra.IconSource), extra);
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo leer el ícono de {extra.Name}", ex);
            return null;
        }
    }

    /// <param name="onlyIfChanged">Por avisos de la carpeta: si quedan las mismas apps no se toca la lista
    /// (la carpeta avisa en tandas y cada una volvía a armar el menú entero).</param>
    private static void Rebuild(bool onlyIfChanged = false)
    {
        if (_appsFolder is null)
            return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (!RebuildCore(onlyIfChanged))
            return;
        Log.Info($"Catálogo de apps: {Apps.Count} apps en {watch.ElapsedMilliseconds} ms");
    }

    private static bool RebuildCore(bool onlyIfChanged)
    {
        if (_appsFolder is null)
            return false;
        var catalog = _appsFolder.Files.ToList()
            .Select(f => new StartApp(f))
            .Where(a => !IsNoise(a.Name))
            .ToList();
        // Lo que no está en el catálogo de Windows (juegos de Steam, Epic y GOG, accesos directos del
        // escritorio), salvo que ya haya una app con ese nombre (sin contar la puntuación).
        var names = catalog.Select(a => ExtraApps.NameKey(a.Name)).ToHashSet();
        foreach (var extra in _extraFound)
            if (names.Add(ExtraApps.NameKey(extra.Name)) && ExtraStartApp(extra) is { } app)
                catalog.Add(app);
        var items = catalog.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (onlyIfChanged && items.Count == Apps.Count
            && items.Zip(Apps).All(pair => pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name))
            return false;

        Apps.Clear();
        foreach (var app in items)
            Apps.Add(app);

        // Con pocas apps el catálogo todavía se está cargando: no decidir los anclados por defecto aún.
        if (items.Count > 20)
            PinLayout.EnsureLayout(() => DefaultPinIds(items));
        PinLayout.RebuildViews();
        if (items.Count > 20)
            TaskbarPins.OnCatalogLoaded();
        return true;
    }

    /// <summary>
    /// Apps comunes instaladas, en el orden de la lista. Se ignoran los accesos tipo URL
    /// (steam://rungameid/…), que son juegos y no la app que se busca.
    /// </summary>
    private static List<string> DefaultPinIds(IReadOnlyList<StartApp> items)
    {
        var candidates = items.Where(a => !a.Id.Contains("://", StringComparison.Ordinal)).ToList();
        return DefaultPins
            .Select(pin => candidates.FirstOrDefault(a => a.Name.Equals(pin, StringComparison.OrdinalIgnoreCase))
                           ?? candidates.FirstOrDefault(a => a.Id.Contains(pin, StringComparison.OrdinalIgnoreCase)))
            .Where(a => a != null)
            .Select(a => a!.Id)
            .Distinct()
            .Take(18)
            .ToList();
    }

    // Desinstaladores, documentación y enlaces web que ensucian la lista.
    private static bool IsNoise(string name) =>
        name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Desinstalar", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(" Uninstaller", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Readme", StringComparison.OrdinalIgnoreCase);

    public static bool IsPinned(string id) => PinLayout.Contains(id);

    public static void SetPinned(StartApp app, bool pinned)
    {
        if (pinned)
            PinLayout.Pin(app.Id);
        else
            PinLayout.Unpin(app.Id);
        app.NotifyPinned();
    }

    /// <summary>Vuelve a los anclados por defecto (borra secciones y carpetas).</summary>
    public static void ResetPinned()
    {
        Settings.Current.StartLayout = null;
        Settings.Current.PinnedStartApps = null;
        Rebuild();
    }

    public static IReadOnlyList<StartApp> Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
            return [];
        return Apps
            .Select(a => (App: a, Score: Score(a.Name, query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.App)
            .Take(30)
            .ToList();
    }

    // Prioriza: empieza con la búsqueda > alguna palabra empieza con ella > la contiene.
    private static int Score(string name, string query)
    {
        if (name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            return 3;
        if (name.Split(' ', '-', '.').Any(w => w.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)))
            return 2;
        return name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ? 1 : 0;
    }

    public static IReadOnlyList<RecentItem> GetRecent(int count)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            return new DirectoryInfo(folder).EnumerateFiles("*.lnk")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(count)
                .Select(f => new RecentItem(Path.GetFileNameWithoutExtension(f.Name), f.FullName, f.LastWriteTime,
                                            FileIcon(f.FullName)))
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>Ícono del archivo (el del destino, en accesos directos) obtenido de forma síncrona.</summary>
    private static ImageSource? FileIcon(string path)
    {
        var info = new ShFileInfo();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiSmallIcon) == IntPtr.Zero
            || info.Icon == IntPtr.Zero)
            return null;
        try
        {
            var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                info.Icon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    private const uint ShgfiIcon = 0x100, ShgfiSmallIcon = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    /// <summary>Ejecuta un comando escrito en el buscador (como Win+R) si no hay apps que coincidan.</summary>
    public static bool RunCommand(string command)
    {
        try
        {
            Process.Start(new ProcessStartInfo(command.Trim()) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void OpenFolder(Environment.SpecialFolder folder) => OpenPath(Environment.GetFolderPath(folder));

    public static void OpenDownloads() => OpenPath("shell:Downloads");

    /// <summary>
    /// Abre una carpeta (o muestra un archivo seleccionado en la suya) en Archivos de Strata UI o, si
    /// se eligió el de Windows (o es una ruta shell: que Archivos no lee), en el Explorador.
    /// </summary>
    public static void OpenPath(string? path, bool select = false)
    {
        if (path is "shell:Downloads" && Modules.OptionalModules.Files.ReplacesExplorer)
            path = Places.Downloads;
        if (Modules.OptionalModules.Files.ReplacesExplorer && (path is null || !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)))
        {
            App.Current.OpenFiles(path);
            return;
        }
        if (path is null)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        else if (select)
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // ── Energía ─────────────────────────────────────────────────────────────

    public static void Lock() => ShellHelper.Lock();
    public static void SignOut() => ShellHelper.Logoff();
    public static void Sleep() => SetSuspendState(false, false, false);
    public static void Restart() => Shutdown("/r /t 0");
    public static void PowerOff() => Shutdown("/s /t 0");

    private static void Shutdown(string args) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });

    /// <summary>
    /// Hay actualizaciones de Windows esperando un reinicio: el menú de energía ofrece "Actualizar y
    /// apagar" y "Actualizar y reiniciar", como el de Windows (que lee las mismas marcas).
    /// </summary>
    public static bool UpdatesPending
    {
        get
        {
            try
            {
                using var orchestrator = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\Orchestrator");
                if (orchestrator?.GetValue("ShutdownFlyoutOptions") is int options && options != 0)
                    return true;
                using var required = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                using var pending = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                return required != null || pending != null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Instala las actualizaciones pendientes y apaga (como "Actualizar y apagar" de Windows).</summary>
    public static void UpdateAndPowerOff() => InstallUpdatesAnd(ShutdownPowerOff);

    /// <summary>Instala las actualizaciones pendientes y reinicia.</summary>
    public static void UpdateAndRestart() => InstallUpdatesAnd(ShutdownRestart);

    /// <summary>
    /// InitiateShutdown con SHUTDOWN_INSTALL_UPDATES: Windows instala lo pendiente antes de apagar o
    /// reiniciar. Necesita el privilegio de apagado del proceso, que se activa antes.
    /// </summary>
    private static void InstallUpdatesAnd(uint action)
    {
        EnableShutdownPrivilege();
        uint error = InitiateShutdown(null, null, 0, action | ShutdownInstallUpdates | ShutdownForceOthers,
                                      ReasonMajorOperatingSystem | ReasonMinorUpgrade | ReasonFlagPlanned);
        if (error != 0)
        {
            Log.Info($"InitiateShutdown con actualizaciones falló ({error}); se apaga sin ellas");
            Shutdown(action == ShutdownRestart ? "/r /t 0" : "/s /t 0");
        }
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out var token))
            return;
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out long luid))
                return;
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
            AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private const uint ShutdownForceOthers = 0x1, ShutdownRestart = 0x4, ShutdownPowerOff = 0x8, ShutdownInstallUpdates = 0x40;
    private const uint ReasonMajorOperatingSystem = 0x00020000, ReasonMinorUpgrade = 0x3, ReasonFlagPlanned = 0x80000000;
    private const uint TokenAdjustPrivileges = 0x20, TokenQuery = 0x8, SePrivilegeEnabled = 0x2;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint InitiateShutdown(string? machine, string? message, uint grace, uint flags, uint reason);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, uint length,
                                                     IntPtr previous, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

    private static string GetUserDisplayName()
    {
        try
        {
            var size = 256u;
            var buffer = new System.Text.StringBuilder((int)size);
            // NameDisplay = 3: nombre completo de la cuenta (Microsoft o local), si existe.
            if (GetUserNameEx(3, buffer, ref size) && buffer.Length > 0)
                return buffer.ToString();
        }
        catch (Exception)
        {
        }
        return Environment.UserName;
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserNameEx(int format, System.Text.StringBuilder name, ref uint size);
}
