using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ManagedShell.WindowsTasks;
using Strata.Shell.Services.StartMenu;

namespace Strata.Shell.Services;

/// <summary>
/// Apps ancladas a la barra (<see cref="StrataSettings.TaskbarPins"/>): los mismos IDs que el menú
/// Inicio (AppUserModelID, o la ruta de un archivo o ejecutable). Cada barra con ventanas las
/// muestra delante de las demás; una abierta usa ese mismo botón para sus ventanas.
/// </summary>
public static class TaskbarPins
{
    /// <summary>Cambió la lista (las barras rehacen sus botones anclados).</summary>
    public static event Action? Changed;

    public static IReadOnlyList<string> Ids => Settings.Current.TaskbarPins;

    public static bool IsPinned(string id) => Settings.Current.TaskbarPins.Contains(id, StringComparer.OrdinalIgnoreCase);

    public static void Pin(string id)
    {
        if (IsPinned(id))
            return;
        Settings.Current.TaskbarPins.Add(id);
        Commit();
    }

    public static void Unpin(string id)
    {
        if (Settings.Current.TaskbarPins.RemoveAll(p => p.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0)
            Commit();
    }

    /// <summary>Mueve un anclado a otra posición (arrastrándolo en la barra).</summary>
    public static void Move(string id, int index)
    {
        var pins = Settings.Current.TaskbarPins;
        int current = pins.FindIndex(p => p.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (current < 0)
            return;
        pins.RemoveAt(current);
        pins.Insert(Math.Clamp(index, 0, pins.Count), id);
        Commit();
    }

    private static void Commit()
    {
        Settings.Save();
        Changed?.Invoke();
        foreach (var app in StartMenuService.Apps)
            app.NotifyTaskbarPinned();
    }

    /// <summary>
    /// Se cargó (o se volvió a leer) el catálogo de apps: la primera vez se traen los anclados de la
    /// barra de Windows, y los botones toman su ícono y nombre.
    /// </summary>
    internal static void OnCatalogLoaded()
    {
        ExeCache.Clear();
        ImportFromWindowsOnce();
        Changed?.Invoke();
    }

    /// <summary>
    /// Accesos directos de "User Pinned\TaskBar" (los anclados de Windows): se anclan como la app de
    /// Inicio con el mismo nombre o, si no hay, como el acceso directo.
    /// </summary>
    private static void ImportFromWindowsOnce()
    {
        var s = Settings.Current;
        if (s.TaskbarPinsImported)
            return;
        s.TaskbarPinsImported = true;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (var link in Directory.EnumerateFiles(folder, "*.lnk"))
                {
                    string name = Path.GetFileNameWithoutExtension(link);
                    string id = StartMenuService.Apps.FirstOrDefault(a => a.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))?.Id ?? link;
                    if (!IsPinned(id))
                        s.TaskbarPins.Add(id);
                }
                Log.Info($"Anclados de la barra de Windows: {s.TaskbarPins.Count}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("No se pudieron leer los anclados de la barra de Windows", ex);
        }
        Settings.Save();
    }

    /// <summary>La app del menú Inicio (o el archivo anclado) que corresponde al ID.</summary>
    public static StartApp? AppFor(string id) =>
        StartMenuService.Apps.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? (StartMenuService.IsPathId(id) ? StartMenuService.FileApp(id) : null);

    /// <summary>
    /// La ventana es de esa app: por su AppUserModelID o, si la ventana no tiene uno, por el
    /// ejecutable (así un navegador no se confunde con sus apps web, que comparten ejecutable).
    /// </summary>
    public static bool Matches(string id, ApplicationWindow window)
    {
        if (!string.IsNullOrEmpty(window.AppUserModelID))
            return window.AppUserModelID.Equals(id, StringComparison.OrdinalIgnoreCase);
        return TargetExe(id) is { } exe && exe.Equals(window.WinFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El anclado al que pertenece una ventana (null si no es de ninguno).</summary>
    public static string? PinOf(ApplicationWindow window) =>
        Settings.Current.TaskbarPins.FirstOrDefault(id => Matches(id, window));

    /// <summary>
    /// Qué anclar para una ventana abierta: su app del menú Inicio (por AppUserModelID o por su
    /// ejecutable) o, si no está en Inicio, el ejecutable mismo.
    /// </summary>
    public static string? IdFor(ApplicationWindow window)
    {
        var apps = StartMenuService.Apps;
        if (!string.IsNullOrEmpty(window.AppUserModelID)
            && apps.FirstOrDefault(a => a.Id.Equals(window.AppUserModelID, StringComparison.OrdinalIgnoreCase)) is { } byId)
            return byId.Id;
        if (string.IsNullOrEmpty(window.WinFileName))
            return null;
        var byExe = apps.FirstOrDefault(a => !AppActions.IsPackaged(a)
                                             && TargetExe(a.Id)?.Equals(window.WinFileName, StringComparison.OrdinalIgnoreCase) == true);
        return byExe?.Id ?? window.WinFileName;
    }

    private static readonly Dictionary<string, string?> ExeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ejecutable de una app (null en las empaquetadas o si no apunta a un .exe).</summary>
    private static string? TargetExe(string id)
    {
        if (ExeCache.TryGetValue(id, out var cached))
            return cached;
        string? exe = null;
        if (StartMenuService.IsPathId(id) && id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exe = id;
        else if (AppFor(id) is { } app && AppActions.TargetPath(app) is { } target
                 && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exe = Path.GetFullPath(target);
        return ExeCache[id] = exe;
    }
}

/// <summary>Botón anclado de una barra: la app y, si está abierta en esa barra, su primera ventana.</summary>
public sealed class PinSlot : INotifyPropertyChanged
{
    private ApplicationWindow? _leader;

    public PinSlot(string id)
    {
        Id = id;
        // El ícono de la app se carga después (en segundo plano): al llegar hay que avisarle al botón.
        // Al iniciar Windows todavía no estaba y los anclados quedaban sin ícono. Débil: la app vive
        // más que el botón, que se rehace cada vez que cambian los anclados.
        if (App is { } app)
            PropertyChangedEventManager.AddHandler(app, OnAppChanged, string.Empty);
    }

    private void OnAppChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StartApp.Icon) or null or "")
            Notify(nameof(Icon));
        if (e.PropertyName is nameof(StartApp.Name) or null or "")
            Notify(nameof(Title));
    }

    public string Id { get; }
    public StartApp? App => TaskbarPins.AppFor(Id);
    public string Title => App?.Name ?? Path.GetFileNameWithoutExtension(Id);
    public ImageSource? Icon => App?.Icon;

    /// <summary>Primera ventana abierta de la app en esta barra (null si no está abierta).</summary>
    public ApplicationWindow? Leader
    {
        get => _leader;
        set
        {
            if (ReferenceEquals(_leader, value))
                return;
            _leader = value;
            Notify();
        }
    }

    public void Launch()
    {
        if (App is { } app)
            app.Launch();
        else
            Log.Info($"El anclado {Id} ya no existe");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
