using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Strata.Shell.Hub;

/// <summary>
/// Arrastrar "fichas" para ordenarlas dentro de un panel o pasarlas a otro (editor de elementos de
/// las barras, botones del pie de Inicio). Se arrastra con la captura del mouse en la página, no
/// con DragDrop de OLE: mover la ficha de panel quita la captura de la propia ficha, y OLE cancela
/// en silencio ante cualquier excepción. La ficha se mueve en vivo mientras se arrastra; al soltar
/// se avisa con <see cref="Committed"/> para guardar el nuevo orden.
/// </summary>
internal sealed class ChipDrag
{
    private const double Threshold = 5;

    private readonly FrameworkElement _root;
    private readonly Func<IEnumerable<Panel>> _panels;
    private readonly Func<FrameworkElement, Panel, bool> _canDrop;
    private FrameworkElement? _chip;
    private Point _start;
    private bool _dragging;

    /// <summary>Se soltó una ficha que se movió.</summary>
    public event Action? Committed;

    /// <param name="root">La página: recibe la captura del mouse durante el arrastre.</param>
    /// <param name="panels">Paneles que aceptan fichas.</param>
    /// <param name="canDrop">Si la ficha puede ir a ese panel (p. ej. sin repetir elementos).</param>
    public ChipDrag(FrameworkElement root, Func<IEnumerable<Panel>> panels, Func<FrameworkElement, Panel, bool>? canDrop = null)
    {
        _root = root;
        _panels = panels;
        _canDrop = canDrop ?? ((_, _) => true);
        _root.MouseMove += OnMove;
        _root.MouseLeftButtonUp += OnUp;
        _root.LostMouseCapture += (_, e) =>
        {
            // El evento sube por el árbol: solo cuenta cuando la página pierde la captura.
            if (ReferenceEquals(e.OriginalSource, _root))
                Finish(commit: _dragging);
        };
    }

    /// <summary>Hace arrastrable una ficha (los botones que tenga dentro siguen funcionando).</summary>
    public void Attach(FrameworkElement chip) => chip.PreviewMouseLeftButtonDown += (_, e) =>
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject, chip))
            return;
        _chip = chip;
        _start = e.GetPosition(_root);
        _dragging = false;
        _root.CaptureMouse();
        e.Handled = true;
    };

    private static bool IsInsideButton(DependencyObject? element, FrameworkElement chip)
    {
        for (var current = element; current != null && !ReferenceEquals(current, chip);
             current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
                return true;
        }
        return false;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_chip is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        var position = e.GetPosition(_root);
        if (!_dragging)
        {
            if ((position - _start).Length < Threshold)
                return;
            _dragging = true;
            _chip.Opacity = 0.55;
        }
        if (PanelAt(position) is not { } panel || _chip.Parent is not Panel from)
            return;
        if (!ReferenceEquals(panel, from) && !_canDrop(_chip, panel))
            return;
        int index = InsertIndex(panel, position);
        int current = ReferenceEquals(panel, from) ? from.Children.IndexOf(_chip) : -1;
        if (current >= 0 && (index == current || index == current + 1))
            return; // ya está ahí
        from.Children.Remove(_chip);
        if (current >= 0 && index > current)
            index--;
        panel.Children.Insert(Math.Min(index, panel.Children.Count), _chip);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_chip is null)
            return;
        bool commit = _dragging;
        _root.ReleaseMouseCapture(); // dispara LostMouseCapture → Finish
        Finish(commit);
    }

    private void Finish(bool commit)
    {
        if (_chip is null)
            return;
        _chip.Opacity = 1;
        _chip = null;
        _dragging = false;
        if (commit)
            Committed?.Invoke();
    }

    private Panel? PanelAt(Point position)
    {
        foreach (var panel in _panels())
        {
            if (!panel.IsVisible)
                continue;
            var bounds = panel.TransformToAncestor(_root).TransformBounds(new Rect(panel.RenderSize));
            bounds.Inflate(8, 8);
            if (bounds.Contains(position))
                return panel;
        }
        return null;
    }

    /// <summary>Posición de inserción en orden de lectura: antes de la primera ficha que quede después del puntero.</summary>
    private int InsertIndex(Panel panel, Point position)
    {
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is not FrameworkElement child || ReferenceEquals(child, _chip))
                continue;
            var r = child.TransformToAncestor(_root).TransformBounds(new Rect(child.RenderSize));
            if (position.Y < r.Top || (position.Y <= r.Bottom && position.X < r.Left + r.Width / 2))
                return i;
        }
        return panel.Children.Count;
    }
}
