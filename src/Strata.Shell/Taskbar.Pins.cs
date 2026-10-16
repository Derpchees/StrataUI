using System.Collections.ObjectModel;
using System.Windows;
using ManagedShell.WindowsTasks;
using Strata.Shell.Controls;
using Strata.Shell.Services;

namespace Strata.Shell;

/// <summary>
/// Apps ancladas (<see cref="TaskbarPins"/>): van delante de las ventanas, en la lista PinnedList.
/// Si la app está abierta en esta barra, su botón es el de sus ventanas (como en Windows) y esas
/// ventanas no se repiten en la lista de abiertas. Se ordenan arrastrándolas entre ellas.
/// </summary>
public partial class Taskbar
{
    private readonly ObservableCollection<PinSlot> _pinSlots = new();
    private string? _pinDrag;

    /// <summary>Esta barra muestra los anclados (las que llevan ventanas).</summary>
    private bool ShowsPins => Role.ShowsApps;

    private void WatchPins()
    {
        PinnedList.ItemsSource = _pinSlots;
        TaskbarPins.Changed += OnPinsChanged;
        RebuildPinSlots();
    }

    private void UnwatchPins() => TaskbarPins.Changed -= OnPinsChanged;

    private void OnPinsChanged()
    {
        RebuildPinSlots();
        _taskView.Refresh(); // las ventanas de una app recién anclada pasan a su botón
        TasksChanged?.Invoke();
    }

    private void RebuildPinSlots()
    {
        _pinSlots.Clear();
        if (ShowsPins)
            foreach (var id in TaskbarPins.Ids)
                _pinSlots.Add(new PinSlot(id));
        PinnedList.Visibility = _pinSlots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshPinLeaders();
    }

    /// <summary>Cada anclado toma su primera ventana abierta en esta barra (si tiene).</summary>
    private void RefreshPinLeaders()
    {
        foreach (var slot in _pinSlots)
        {
            slot.Leader = AllWindows
                .Where(w => ShowsWindow(w) && TaskbarPins.Matches(slot.Id, w))
                .OrderBy(w => w, Comparer<ApplicationWindow>.Create((a, b) => TaskOrder.Instance.Compare(a, b)))
                .FirstOrDefault();
        }
    }

    /// <summary>La ventana va en el botón de un anclado de esta barra (y no en la lista de abiertas).</summary>
    private bool InPinnedButton(ApplicationWindow window) => ShowsPins && TaskbarPins.PinOf(window) != null;

    /// <summary>Todas las ventanas de un anclado en esta barra (null si la ventana no es de ninguno).</summary>
    private IReadOnlyList<ApplicationWindow>? PinnedMembers(ApplicationWindow window)
    {
        if (!ShowsPins || TaskbarPins.PinOf(window) is not { } id)
            return null;
        return AllWindows.Where(w => ShowsWindow(w) && TaskbarPins.Matches(id, w))
                         .OrderBy(w => w, Comparer<ApplicationWindow>.Create((a, b) => TaskOrder.Instance.Compare(a, b)))
                         .ToList();
    }

    // ── Ordenar arrastrando ─────────────────────────────────────────────────

    private bool StartPinDrag(TaskButton source)
    {
        if (source.Slot is not { } slot || !PinnedList.IsAncestorOf(source))
            return false;
        _pinDrag = slot.Id;
        if (!CaptureMouse())
        {
            _pinDrag = null;
            return true;
        }
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeAll;
        Controls.DragGhost.Begin(source);
        _taskDragButton = source;
        source.Opacity = 0.55;
        return true;
    }

    private void PinDragMove(string id)
    {
        Controls.DragGhost.Move();
        GetCursorPos(out var cursor);
        var point = new Point(cursor.X, cursor.Y);
        int from = _pinSlots.ToList().FindIndex(s => s.Id == id);
        for (int i = 0; i < _pinSlots.Count; i++)
        {
            if (i == from || PinnedList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement item
                || !ScreenRect(item, 0).Contains(point))
                continue;
            TaskbarPins.Move(id, i);
            return;
        }
    }

    private void EndPinDrag()
    {
        _pinDrag = null;
        Controls.DragGhost.End();
        if (_taskDragButton != null)
            _taskDragButton.Opacity = 1;
        _taskDragButton = null;
        System.Windows.Input.Mouse.OverrideCursor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }
}
