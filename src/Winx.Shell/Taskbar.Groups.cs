using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using ManagedShell.WindowsTasks;
using Winx.Shell.Services;

namespace Winx.Shell;

/// <summary>
/// Ventanas agrupadas por app (opción del Hub): un solo botón por app, con todas sus ventanas en la
/// vista previa. El botón es el de la primera ventana del grupo en el orden de la barra; las demás
/// quedan fuera de la lista. Una pestaña con un sitio conocido cuenta como esa app (ver
/// <see cref="TaskGroups"/>), así que cambiar de pestaña puede mover la ventana de grupo.
/// </summary>
public partial class Taskbar
{
    private bool _combine;
    private bool _groupRefreshPending;
    private readonly HashSet<ApplicationWindow> _watchedWindows = new();

    /// <summary>Cambiaron los grupos o el estado de sus ventanas: los botones actualizan contador e indicador.</summary>
    public event Action? TasksChanged;

    private IEnumerable<ApplicationWindow> AllWindows => _shell.Tasks.GroupedWindows.OfType<ApplicationWindow>();

    /// <summary>La ventana va en esta barra (sin contar los grupos).</summary>
    private bool ShowsWindow(ApplicationWindow window) => window.ShowInTaskbar && Shows(window);

    /// <summary>Con las ventanas agrupadas, solo la primera de cada app tiene botón.</summary>
    private bool LeadsGroup(ApplicationWindow window)
    {
        if (!_combine)
            return true;
        string key = TaskGroups.Key(window);
        return !AllWindows.Any(other => !ReferenceEquals(other, window) && ShowsWindow(other)
                                        && TaskOrder.Instance.Compare(other, window) < 0
                                        && TaskGroups.Key(other) == key);
    }

    /// <summary>Ventanas del botón de <paramref name="window"/>: solo ella o, agrupando, todas las de su app.</summary>
    public IReadOnlyList<ApplicationWindow> GroupMembers(ApplicationWindow window)
    {
        if (!_combine)
            return [window];
        string key = TaskGroups.Key(window);
        return AllWindows.Where(w => ShowsWindow(w) && TaskGroups.Key(w) == key)
                         .OrderBy(w => w, Comparer<ApplicationWindow>.Create((a, b) => TaskOrder.Instance.Compare(a, b)))
                         .ToList();
    }

    private void WatchWindows()
    {
        ((INotifyCollectionChanged)_shell.Tasks.GroupedWindows).CollectionChanged += OnWindowsChanged;
        SyncWatchedWindows();
    }

    private void UnwatchWindows()
    {
        ((INotifyCollectionChanged)_shell.Tasks.GroupedWindows).CollectionChanged -= OnWindowsChanged;
        foreach (var window in _watchedWindows)
            window.PropertyChanged -= OnWatchedWindowChanged;
        _watchedWindows.Clear();
    }

    private void OnWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncWatchedWindows();
        ScheduleGroupRefresh();
    }

    private void SyncWatchedWindows()
    {
        var current = AllWindows.ToHashSet();
        foreach (var gone in _watchedWindows.Where(w => !current.Contains(w)).ToList())
        {
            gone.PropertyChanged -= OnWatchedWindowChanged;
            _watchedWindows.Remove(gone);
        }
        foreach (var window in current.Where(w => _watchedWindows.Add(w)))
            window.PropertyChanged += OnWatchedWindowChanged;
    }

    private void OnWatchedWindowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_combine)
            return;
        // El título puede cambiar el sitio (y con él el grupo); el estado, el indicador del botón.
        if (e.PropertyName is nameof(ApplicationWindow.Title) or nameof(ApplicationWindow.AppUserModelID))
            ScheduleGroupRefresh();
        else if (e.PropertyName is nameof(ApplicationWindow.State) or nameof(ApplicationWindow.IsMinimized))
            Dispatcher.BeginInvoke(() => TasksChanged?.Invoke());
    }

    /// <summary>Vuelve a armar los grupos una sola vez aunque lleguen varios cambios seguidos.</summary>
    private void ScheduleGroupRefresh()
    {
        if (_groupRefreshPending)
            return;
        _groupRefreshPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _groupRefreshPending = false;
            if (_combine)
                _taskView.Refresh();
            TasksChanged?.Invoke();
        }, DispatcherPriority.Background);
    }

    /// <summary>Se activó o desactivó agrupar en el Hub.</summary>
    private void ApplyCombine(bool combine)
    {
        if (combine == _combine)
            return;
        _combine = combine;
        _taskView.Refresh();
        TasksChanged?.Invoke();
    }
}
