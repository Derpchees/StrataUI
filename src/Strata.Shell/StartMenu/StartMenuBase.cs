using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Strata.Shell.Flyouts;
using Strata.Shell.Services;
using Strata.Shell.Services.StartMenu;

namespace Strata.Shell.StartMenu;

/// <summary>
/// Comportamiento común de los estilos de menú Inicio: modelo compartido, foco en el buscador,
/// escribir en cualquier parte busca, Enter abre el primer resultado, Esc limpia antes de cerrar,
/// y asas para cambiar el tamaño (que se recuerda por estilo).
/// </summary>
public abstract class StartMenuBase : FlyoutWindow
{
    private const double MinWidthDip = 300, MinHeightDip = 320;

    protected StartMenuBase()
    {
        DataContext = Model;
        // Lo instalado o quitado mientras tanto (las apps de la Tienda no avisan de otra forma).
        StartMenuService.RefreshIfStale();
        // Clic derecho en un lugar vacío del menú: tamaño de los íconos y nombres (lo que tiene su
        // propio menú, como las apps o los anclados, lo muestra en lugar de este).
        ContextMenu = new ContextMenu { Items = { StartViewMenu.Create() } };
        Loaded += (_, _) =>
        {
            if (!FillWorkArea)
                AddResizeGrips();
            SearchInput?.Focus();
        };
        WatchAppDrag();
        PreviewTextInput += (_, e) =>
        {
            // Empezar a escribir con el foco en otro control lleva el texto al buscador.
            if (SearchInput is { IsKeyboardFocusWithin: false } box && !string.IsNullOrWhiteSpace(e.Text)
                && Keyboard.FocusedElement is not TextBox)
            {
                box.Focus();
                box.AppendText(e.Text);
                box.CaretIndex = box.Text.Length;
                e.Handled = true;
            }
        };
    }

    public StartMenuModel Model { get; } = new();

    /// <summary>Caja de búsqueda del estilo concreto.</summary>
    protected abstract TextBox? SearchInput { get; }

    /// <summary>False en estilos que crecen con su contenido (solo se puede cambiar el ancho).</summary>
    protected virtual bool ResizableHeight => SizeToContent == SizeToContent.Manual;

    protected override bool ScrollContent => false;

    /// <summary>Junto al botón Inicio según su zona en la barra: al principio, al centro o al final.</summary>
    protected override FlyoutPlacement Placement =>
        (Anchor is null ? null : Window.GetWindow(Anchor) as Taskbar)?.ZoneOf(BarItem.Start) switch
        {
            BarZone.Start => FlyoutPlacement.Left,
            BarZone.End => FlyoutPlacement.Right,
            _ => FlyoutPlacement.Center,
        };

    protected override bool HandleEscape()
    {
        if (Model.ShowAllApps || Model.IsSearching)
        {
            Model.Query = "";
            Model.ShowAllApps = false;
            return true;
        }
        return false;
    }

    protected void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Model.Submit();
            e.Handled = true;
        }
    }

    protected void OnShowAllApps(object sender, RoutedEventArgs e) => Model.ShowAllApps = true;

    protected void OnBackToHome(object sender, RoutedEventArgs e) => Model.ShowAllApps = false;

    // ── Arrastrar una app a la barra ────────────────────────────────────────
    // Desde la lista de apps o los resultados de búsqueda (los anclados tienen su propio arrastre en
    // PinnedLayoutView): la app va con el puntero y, soltada sobre una barra, se ancla ahí.

    private StartApp? _pressedApp;
    private FrameworkElement? _pressedElement;
    private Point _pressPoint;
    private bool _appDragging;

    private void WatchAppDrag()
    {
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressedApp = null;
            // Un Run de un texto no es Visual: su padre se busca en el árbol lógico (y el de lo que
            // está en un Popup, como una carpeta abierta, es el Popup).
            static DependencyObject? Parent(DependencyObject node) =>
                (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);
            StartApp? pressed = null;
            FrameworkElement? pressedElement = null;
            for (var node = e.OriginalSource as DependencyObject; node != null && !ReferenceEquals(node, this); node = Parent(node))
            {
                // Los anclados tienen su propio arrastre (mover, carpetas y también a la barra): el
                // mosaico se encuentra antes que la vista, así que hay que subir hasta el final.
                if (node is PinnedLayoutView)
                    return;
                if (pressed is null && node is FrameworkElement { DataContext: StartApp app } element and (Button or ListBoxItem or ContentPresenter))
                {
                    pressed = app;
                    pressedElement = element;
                }
            }
            _pressedApp = pressed;
            _pressedElement = pressedElement;
            _pressPoint = e.GetPosition(this);
        };
        PreviewMouseMove += (_, e) =>
        {
            if (_appDragging)
            {
                Controls.DragGhost.Move();
                return;
            }
            if (_pressedApp is null || e.LeftButton != MouseButtonState.Pressed)
                return;
            var delta = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            // La captura la toma el menú: así el botón no se "pulsa" al soltar.
            if (!CaptureMouse())
                return;
            _appDragging = true;
            if (_pressedElement != null)
                Controls.DragGhost.Begin(_pressedElement);
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_appDragging)
            {
                _pressedApp = null;
                return;
            }
            var app = _pressedApp;
            EndAppDrag();
            if (app != null)
                TaskbarPins.DropOnTaskbar(app.Id);
            e.Handled = true;
        };
        LostMouseCapture += (_, e) =>
        {
            if (_appDragging && ReferenceEquals(e.OriginalSource, this))
                EndAppDrag();
        };
    }

    private void EndAppDrag()
    {
        _appDragging = false;
        _pressedApp = null;
        _pressedElement = null;
        Controls.DragGhost.End();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    // ── Tamaño ──────────────────────────────────────────────────────────────

    private string SizeKey => GetType().Name;

    protected override void BeforeShow() => RestoreSize();

    private void RestoreSize()
    {
        if (!Settings.Current.StartMenuSizes.TryGetValue(SizeKey, out var size) || size.Length < 2)
            return;
        Width = Math.Max(MinWidthDip, size[0]);
        if (ResizableHeight)
            Height = Math.Max(MinHeightDip, size[1]);
    }

    private void SaveSize()
    {
        Settings.Current.StartMenuSizes[SizeKey] = [Width, ResizableHeight ? Height : 0];
        Settings.Save();
    }

    /// <summary>
    /// Asas invisibles en los bordes libres del menú (los que no están pegados a la barra) y en el
    /// lateral que se aleja del botón Inicio. En menús centrados el ancho crece por ambos lados.
    /// </summary>
    private void AddResizeGrips()
    {
        // Con la barra a un lado, el menú queda pegado a ella: crece hacia el lado contrario y hacia abajo.
        bool sideBar = BarPosition is TaskbarPosition.Left or TaskbarPosition.Right && Placement != FlyoutPlacement.ScreenCenter;
        bool growsLeft = sideBar ? BarPosition == TaskbarPosition.Right : Placement == FlyoutPlacement.Right;
        bool symmetric = !sideBar && Placement is FlyoutPlacement.Center or FlyoutPlacement.ScreenCenter;
        var sideAlign = growsLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        // El alto se cambia en el borde libre: arriba si la barra está abajo; abajo en los demás
        // casos (barra arriba o a un lado, o "solo búsqueda", que tiene fija la parte de arriba).
        var verticalEdge = BarPosition == TaskbarPosition.Bottom && Placement != FlyoutPlacement.ScreenCenter
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;


        var grips = new Grid();
        grips.Children.Add(Grip(Cursors.SizeWE, sideAlign, VerticalAlignment.Stretch, 6, double.NaN, horizontal: true, vertical: false));
        if (ResizableHeight)
        {
            grips.Children.Add(Grip(Cursors.SizeNS, HorizontalAlignment.Stretch, verticalEdge, double.NaN, 6, horizontal: false, vertical: true));
            var corner = (growsLeft, verticalEdge) switch
            {
                (true, VerticalAlignment.Top) or (false, VerticalAlignment.Bottom) => Cursors.SizeNWSE,
                _ => Cursors.SizeNESW,
            };
            grips.Children.Add(Grip(corner, sideAlign, verticalEdge, 14, 14, horizontal: true, vertical: true));
        }
        AddOverlay(grips);

        Thumb Grip(Cursor cursor, HorizontalAlignment h, VerticalAlignment v, double width, double height, bool horizontal, bool vertical)
        {
            var thumb = new Thumb
            {
                Cursor = cursor,
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Width = width,
                Height = height,
                Template = TransparentThumb(),
            };
            Point start = default;
            Size startSize = default;
            thumb.DragStarted += (_, _) =>
            {
                start = CursorPosition();
                startSize = new Size(ActualWidth, ActualHeight);
            };
            thumb.DragDelta += (_, _) =>
            {
                // Coordenadas de pantalla: la ventana se reposiciona mientras crece y las
                // coordenadas relativas al asa no servirían.
                var now = CursorPosition();
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                if (horizontal)
                {
                    double dx = (now.X - start.X) / scale * (growsLeft ? -1 : 1) * (symmetric ? 2 : 1);
                    Width = Math.Max(MinWidthDip, startSize.Width + dx);
                }
                if (vertical)
                {
                    double dy = (now.Y - start.Y) / scale * (verticalEdge == VerticalAlignment.Top ? -1 : 1);
                    Height = Math.Clamp(startSize.Height + dy, MinHeightDip, MaxHeight);
                }
            };
            thumb.DragCompleted += (_, _) => SaveSize();
            return thumb;
        }
    }

    private static ControlTemplate TransparentThumb()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        template.VisualTree = border;
        return template;
    }

    private static Point CursorPosition()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
