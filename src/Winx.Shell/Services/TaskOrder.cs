using System.Collections;
using ManagedShell.WindowsTasks;

namespace Winx.Shell.Services;

/// <summary>
/// Orden de las ventanas en la barra, elegido arrastrándolas (como en Windows). Es el mismo en
/// todas las barras y dura lo que dura la sesión: las ventanas nuevas van al final.
/// </summary>
public sealed class TaskOrder : IComparer
{
    public static TaskOrder Instance { get; } = new();

    private readonly Dictionary<IntPtr, double> _order = new();
    private double _next;

    private TaskOrder()
    {
    }

    /// <summary>Cambió el orden: las barras vuelven a ordenar su lista.</summary>
    public static event Action? Changed;

    /// <summary>Registra las ventanas que ya están, en el orden en que llegan (antes de ordenar la vista).</summary>
    public void Seed(IEnumerable<ApplicationWindow> windows)
    {
        foreach (var window in windows)
            Of(window);
    }

    private double Of(ApplicationWindow window)
    {
        if (!_order.TryGetValue(window.Handle, out var position))
            _order[window.Handle] = position = ++_next;
        return position;
    }

    public int Compare(object? x, object? y) =>
        x is ApplicationWindow a && y is ApplicationWindow b ? Of(a).CompareTo(Of(b)) : 0;

    /// <summary>Pone <paramref name="moving"/> justo antes (o después) de <paramref name="target"/>.</summary>
    public void Move(ApplicationWindow moving, ApplicationWindow target, bool after, IEnumerable<ApplicationWindow> all)
    {
        if (ReferenceEquals(moving, target))
            return;
        // Renumerar con huecos deja sitio entre dos ventanas cualesquiera.
        var ordered = all.OrderBy(Of).ToList();
        _order.Clear();
        _next = 0;
        foreach (var window in ordered)
            _order[window.Handle] = _next += 2;
        double place = _order[target.Handle] + (after ? 1 : -1);
        _order[moving.Handle] = place;
        Changed?.Invoke();
    }
}
