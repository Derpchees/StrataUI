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
    public void Move(ApplicationWindow moving, ApplicationWindow target, bool after, IEnumerable<ApplicationWindow> all) =>
        Move([moving], target, after, all);

    /// <summary>
    /// Pone varias ventanas juntas (un grupo de la barra, en su orden) justo antes o después de
    /// <paramref name="target"/>.
    /// </summary>
    public void Move(IReadOnlyList<ApplicationWindow> moving, ApplicationWindow target, bool after, IEnumerable<ApplicationWindow> all)
    {
        if (moving.Count == 0 || moving.Contains(target))
            return;
        // Renumerar con huecos deja sitio entre dos ventanas cualesquiera.
        var ordered = all.OrderBy(Of).ToList();
        _order.Clear();
        _next = 0;
        foreach (var window in ordered)
            _order[window.Handle] = _next += 2;
        if (!_order.TryGetValue(target.Handle, out double anchor))
            return;
        // Entre el destino y su vecino hay un hueco de 2: el grupo entra en él, en su orden.
        double start = anchor + (after ? 0.5 : -1.5);
        for (int i = 0; i < moving.Count; i++)
            _order[moving[i].Handle] = start + i / (double)(moving.Count + 1);
        Changed?.Invoke();
    }
}
