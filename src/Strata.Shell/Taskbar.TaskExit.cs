using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ManagedShell.WindowsTasks;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes;

namespace Strata.Shell;

/// <summary>
/// Salida del botón de una app que se cierra: la vista lo quita de golpe, así que antes se toma una
/// imagen del botón y esa imagen hace la animación de salida en su lugar (en la capa de adornos).
/// </summary>
public partial class Taskbar
{
    /// <summary>
    /// Se suscribe a la vista de la barra antes de dársela a la lista: así este aviso llega antes que
    /// el de la lista y el botón todavía existe. Cubre la ventana que se cierra y la que deja de
    /// mostrarse (la vista la filtra antes de que la ventana desaparezca del todo).
    /// </summary>
    private void WatchClosingWindows(ICollectionView view)
    {
        var windows = (INotifyCollectionChanged)view;
        windows.CollectionChanged += OnWindowRemoved;
        Closed += (_, _) => windows.CollectionChanged -= OnWindowRemoved;
    }

    private void OnWindowRemoved(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Remove || e.OldItems is null
            || Settings.Current.PanelAnimation == PanelAnimation.None && Settings.Current.AnimationModule is null)
            return;
        foreach (var window in e.OldItems.OfType<ApplicationWindow>())
        {
            if (TaskList.ItemContainerGenerator.ContainerFromItem(window) is not FrameworkElement button || !button.IsVisible)
                continue;
            // Agrupadas: si quedan ventanas de la app, el botón sigue (con otra al frente).
            if (_combine && AllWindows.Any(w => w != window && ShowsWindow(w) && TaskGroups.Key(w) == TaskGroups.Key(window)))
                continue;
            ShowGhost(button);
        }
    }

    private void ShowGhost(FrameworkElement button)
    {
        // La capa de la ventana, no la de la lista: la lista se achica al quitar el botón y su capa
        // recortaba la imagen.
        if (Content is not FrameworkElement root || AdornerLayer.GetAdornerLayer(root) is not { } layer
            || button.ActualWidth < 1 || button.ActualHeight < 1)
            return;
        try
        {
            double dpi = VisualTreeHelper.GetDpi(button).PixelsPerDip;
            var size = new Size(button.ActualWidth, button.ActualHeight);
            // Render dibuja el botón corrido por su posición dentro de la lista: se dibuja en una
            // imagen que la incluye y se recorta. (Con un VisualBrush salía vacía: el botón deja el
            // árbol antes de que el pincel llegue a dibujarse.)
            var offset = VisualTreeHelper.GetOffset(button);
            int left = (int)Math.Floor(Math.Max(0, offset.X) * dpi), top = (int)Math.Floor(Math.Max(0, offset.Y) * dpi);
            int width = (int)Math.Ceiling(size.Width * dpi), height = (int)Math.Ceiling(size.Height * dpi);
            var full = new RenderTargetBitmap(left + width, top + height, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            full.Render(button);
            var bitmap = new CroppedBitmap(full, new Int32Rect(left, top, width, height));
            bitmap.Freeze();

            var ghost = new GhostAdorner(root, bitmap, button.TranslatePoint(new Point(0, 0), root), size);
            layer.Add(ghost);
            Motion.Out(ghost.Image, ghost.Image, Position, () => layer.Remove(ghost));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Log.Error("No se pudo animar la salida de un botón de la barra", ex);
        }
    }

    /// <summary>La imagen del botón que se va, dibujada donde estaba.</summary>
    private sealed class GhostAdorner : Adorner
    {
        private readonly Point _at;
        public Image Image { get; }

        public GhostAdorner(UIElement adorned, ImageSource bitmap, Point at, Size size) : base(adorned)
        {
            _at = at;
            IsHitTestVisible = false;
            Image = new Image { Source = bitmap, Width = size.Width, Height = size.Height, Stretch = Stretch.Fill };
            AddVisualChild(Image);
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => Image;

        protected override Size MeasureOverride(Size constraint)
        {
            Image.Measure(new Size(Image.Width, Image.Height));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Image.Arrange(new Rect(_at, new Size(Image.Width, Image.Height)));
            return finalSize;
        }
    }
}
