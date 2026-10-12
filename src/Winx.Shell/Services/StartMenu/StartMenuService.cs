using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using ManagedShell.Common.Helpers;
using ManagedShell.ShellFolders;

namespace Winx.Shell.Services.StartMenu;

/// <summary>Una app del menú Inicio (entrada de shell:AppsFolder).</summary>
public sealed class StartApp : INotifyPropertyChanged
{
    private readonly ShellItem _item;

    internal StartApp(ShellItem item)
    {
        _item = item;
        if (item is INotifyPropertyChanged notifier)
            notifier.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ShellItem.LargeIcon) or nameof(ShellItem.DisplayName))
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(e.PropertyName == nameof(ShellItem.LargeIcon) ? nameof(Icon) : nameof(Name)));
            };
    }

    /// <summary>Identificador estable: AppUserModelID o ruta del acceso directo.</summary>
    public string Id => _item.FileName ?? _item.Path;
    public string Name => _item.DisplayName ?? Id;
    public ImageSource? Icon => _item.LargeIcon;
    public string Group => GroupOf(Name);

    public bool IsPinned => StartMenuService.IsPinned(Id);

    internal void NotifyPinned() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));

    public void Launch()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{Id}") { UseShellExecute = true });
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
            Rebuild();
        };
        _appsFolder = new ShellFolder("shell:AppsFolder", IntPtr.Zero, loadAsync: true, watchChanges: true);
        _appsFolder.Files.CollectionChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _rebuildTimer.Stop();
            _rebuildTimer.Start();
        });
        _rebuildTimer.Start();
    }

    public static void Shutdown() => _appsFolder?.Dispose();

    /// <summary>
    /// Tamaños de íconos del menú Inicio como recursos dinámicos: las plantillas los leen con
    /// DynamicResource, así que cambiar la opción en el Hub se refleja sin reabrir nada.
    /// </summary>
    public static void ApplyIconSizes()
    {
        var (tile, row, grid, large) = Settings.Current.StartIconSize switch
        {
            StartIconSize.Small => (26.0, 20.0, 30.0, 40.0),
            StartIconSize.Large => (44.0, 30.0, 50.0, 64.0),
            _ => (32.0, 24.0, 38.0, 48.0),
        };
        bool labels = Settings.Current.StartShowPinnedLabels;
        var resources = Application.Current.Resources;
        resources["StartTileIconSize"] = tile;
        // Sin nombres, el mosaico solo necesita el ícono y su margen.
        resources["StartTileWidth"] = labels ? tile * 3 : tile + 32;
        resources["StartTileLabelVisibility"] = labels ? Visibility.Visible : Visibility.Collapsed;
        resources["StartTileIconMargin"] = new Thickness(0, 0, 0, labels ? 6 : 0);
        resources["StartRowIconSize"] = row;
        resources["StartGridIconSize"] = grid;
        resources["StartGridButtonSize"] = grid + 26;
        resources["StartLargeIconSize"] = large;
        resources["StartLargeTileWidth"] = large * 2.6;
    }

    private static void Rebuild()
    {
        if (_appsFolder is null)
            return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        RebuildCore();
        Log.Info($"Catálogo de apps: {Apps.Count} apps en {watch.ElapsedMilliseconds} ms");
    }

    private static void RebuildCore()
    {
        if (_appsFolder is null)
            return;
        var items = _appsFolder.Files.ToList()
            .Select(f => new StartApp(f))
            .Where(a => !IsNoise(a.Name))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Apps.Clear();
        foreach (var app in items)
            Apps.Add(app);

        // Con pocas apps el catálogo todavía se está cargando: no decidir los anclados por defecto aún.
        if (items.Count > 20)
            PinLayout.EnsureLayout(() => DefaultPinIds(items));
        PinLayout.RebuildViews();
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

    public static void OpenFolder(Environment.SpecialFolder folder) =>
        Process.Start(new ProcessStartInfo(Environment.GetFolderPath(folder)) { UseShellExecute = true });

    public static void OpenDownloads() =>
        Process.Start(new ProcessStartInfo("shell:Downloads") { UseShellExecute = true });

    // ── Energía ─────────────────────────────────────────────────────────────

    public static void Lock() => ShellHelper.Lock();
    public static void SignOut() => ShellHelper.Logoff();
    public static void Sleep() => SetSuspendState(false, false, false);
    public static void Restart() => Shutdown("/r /t 0");
    public static void PowerOff() => Shutdown("/s /t 0");

    private static void Shutdown(string args) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });

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
