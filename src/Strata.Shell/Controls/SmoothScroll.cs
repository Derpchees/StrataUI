using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Strata.Shell.Controls;

/// <summary>
/// Desplazamiento suave con la rueda: cada giro suma al destino y la vista se desliza hacia él,
/// frenando al llegar (como en un navegador), en lugar de saltar de a tres filas. Uso en XAML:
/// <c>controls:SmoothScroll.Enabled="True"</c> en un ScrollViewer o en un control que tenga uno
/// (ListBox). Con listas virtualizadas, el panel debe desplazarse por píxeles.
/// </summary>
public static class SmoothScroll
{
    /// <summary>Píxeles por cada "clic" de la rueda (120 unidades).</summary>
    private const double PixelsPerNotch = 96;
    /// <summary>Qué parte de lo que falta se recorre en cada cuadro (más alto = más rápido).</summary>
    private const double Approach = 0.22;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(SmoothScroll));

    /// <summary>Destino del desplazamiento de un ScrollViewer mientras se anima.</summary>
    private sealed class State
    {
        public double Target;
        public bool Running;
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;
        if ((bool)e.NewValue)
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        else
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+rueda queda para quien la use (zoom); Mayús+rueda, para el desplazamiento horizontal.
        if (Keyboard.Modifiers != ModifierKeys.None || FindScrollViewer((DependencyObject)sender) is not { } viewer
            || viewer.ScrollableHeight <= 0)
            return;
        // Un ScrollViewer de adentro que todavía puede desplazarse se queda con la rueda.
        if (e.OriginalSource is DependencyObject source && InnerScrollable(source, viewer, e.Delta))
            return;
        e.Handled = true;
        if (viewer.GetValue(StateProperty) is not State state)
        {
            state = new State();
            viewer.SetValue(StateProperty, state);
        }
        if (!state.Running)
            state.Target = viewer.VerticalOffset;
        state.Target = Math.Clamp(state.Target - e.Delta / 120.0 * PixelsPerNotch, 0, viewer.ScrollableHeight);
        if (state.Running)
            return;
        state.Running = true;
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            double remaining = state.Target - viewer.VerticalOffset;
            if (Math.Abs(remaining) < 0.5 || !viewer.IsVisible)
            {
                viewer.ScrollToVerticalOffset(state.Target);
                state.Running = false;
                CompositionTarget.Rendering -= tick;
                return;
            }
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + remaining * Approach);
        };
        CompositionTarget.Rendering += tick;
    }

    /// <summary>Hay otro ScrollViewer entre el puntero y este que todavía puede desplazarse en esa dirección.</summary>
    private static bool InnerScrollable(DependencyObject source, ScrollViewer outer, int delta)
    {
        for (var node = source; node != null && node != outer; node = Parent(node))
        {
            if (node is ScrollViewer inner && inner.ScrollableHeight > 0
                && (delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight))
                return true;
        }
        return false;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
            return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        }
        return null;
    }
}
