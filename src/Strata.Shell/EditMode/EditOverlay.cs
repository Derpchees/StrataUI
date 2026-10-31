using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Strata.Shell.Services.Widgets;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = Wpf.Ui.Controls.Button;
using Image = System.Windows.Controls.Image;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Strata.Shell.EditMode;

/// <summary>
/// La capa del modo edición sobre un monitor: tapa todo lo que hay en pantalla (ventanas y barras
/// reales) con el fondo de pantalla y dibuja encima el borrador de ese monitor: sus barras como se
/// van a ver, los bordes libres donde se puede crear una, y los widgets en su lugar. Arriba, la
/// barra de herramientas (aplicar, cancelar, monitor principal) y la biblioteca para arrastrar.
/// </summary>
internal sealed class EditOverlay : Window
{
    private const double EdgeHint = 36;

    private readonly Forms.Screen _screen;
    private readonly Grid _root = new();
    private readonly DockPanel _frame = new() { LastChildFill = true };
    private readonly Grid _desktop = new() { Background = Brushes.Transparent };
    private readonly Canvas _widgets = new();
    private readonly Canvas _bannerLayer = new();
    private FrameworkElement? _banner;
    private Border? _colorPanel;
    private static readonly Size BannerSize = new(380, 86);
    private const double BannerGap = 12;
    private readonly Grid _edgeHints = new();
    private readonly Canvas _markerLayer = new() { IsHitTestVisible = false };
    private readonly Border _marker = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _message = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _library;
    private readonly CheckBox _mainBox = new();
    private readonly List<BarPreview> _bars = new();
    private readonly Dictionary<TaskbarPosition, FrameworkElement> _placeholders = new();
    private readonly Dictionary<string, (FrameworkElement Box, Size Size)> _widgetBoxes = new();
    private System.Windows.Threading.DispatcherTimer? _messageTimer;
    private bool _closing;

    public string Device { get; }

    /// <summary>El monitor en píxeles de pantalla.</summary>
    public Rect ScreenBounds { get; }

    public EditOverlay(Forms.Screen screen)
    {
        _screen = screen;
        Device = screen.DeviceName;
        ScreenBounds = new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        Title = "Strata UI edit";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        // El estilo de ventanas de WPF-UI impone un tamaño mínimo y su propio fondo.
        Style = null;
        MinWidth = 0;
        MinHeight = 0;
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        // Fuera de la vista hasta que se ubica en su monitor (en píxeles, al crearse).
        Left = -32000;
        Top = -32000;
        if (ThemeManager.PaletteFor(Device) is { } palette)
            Resources.MergedDictionaries.Add(palette);
        // Sin el estilo de ventana de WPF-UI el texto saldría negro (tarjetas de widgets nuevos, menús).
        _root.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");

        // Fondo de pantalla, un velo leve y un marco del color de acento: se nota que es el modo edición.
        _root.Children.Add(WallpaperImage());
        _root.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), IsHitTestVisible = false });
        _desktop.Children.Add(_edgeHints);
        _desktop.Children.Add(_widgets);
        _desktop.Children.Add(_bannerLayer);
        _bannerLayer.SizeChanged += (_, _) => PlaceBanner();
        _desktop.ContextMenu = DesktopMenu();
        _desktop.ContextMenuOpening += (_, _) => _desktop.ContextMenu = DesktopMenu();
        _widgets.SizeChanged += (_, _) => PlaceWidgets();
        _root.Children.Add(_frame);
        _markerLayer.Children.Add(_marker);
        _marker.SetResourceReference(Border.BorderBrushProperty, "StrataAccentBrush");
        _root.Children.Add(_markerLayer);
        var outline = new Border { BorderThickness = new Thickness(3), IsHitTestVisible = false };
        outline.SetResourceReference(Border.BorderBrushProperty, "StrataAccentBrush");
        _root.Children.Add(outline);

        _library = Floating(new ScrollViewer
        {
            Content = new EditLibrary(Device),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = Math.Max(240, screen.WorkingArea.Height * 0.55),
        }, width: 320);
        _library.HorizontalAlignment = HorizontalAlignment.Left;
        _library.VerticalAlignment = VerticalAlignment.Center;
        _library.Margin = new Thickness(EdgeHint + 16, 0, 0, 0);
        _library.Visibility = Visibility.Collapsed;
        var toolbar = Toolbar();
        toolbar.HorizontalAlignment = HorizontalAlignment.Center;
        toolbar.VerticalAlignment = VerticalAlignment.Top;
        toolbar.Margin = new Thickness(0, EdgeHint + 16, 0, 0);
        _desktop.Children.Add(_library);
        _desktop.Children.Add(toolbar);
        Content = _root;

        SourceInitialized += (_, _) => OnSourceInitialized();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceOnScreen);
        KeyDown += OnKeyDown;
        Closed += (_, _) =>
        {
            bool byUs = _closing;
            _closing = true;
            EditSession.Changed -= Rebuild;
            BarPreview.EditColor -= OnEditColor;
            if (!byUs)
                EditSession.Cancel(); // Alt+F4 u otra forma de cerrarla: es cancelar
        };
        EditSession.Changed += Rebuild;
        BarPreview.EditColor += OnEditColor;
        Rebuild();
    }

    public void CloseOverlay()
    {
        if (_closing)
            return;
        _closing = true;
        _messageTimer?.Stop();
        Close();
    }

    private void OnSourceInitialized()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Sin Alt+Tab ni botón en la barra.
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExToolWindow);
        PlaceOnScreen();
    }

    /// <summary>Ocupa todo el monitor, en píxeles (WPF ajusta la escala de ese monitor solo).</summary>
    private void PlaceOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _closing)
            return;
        var b = _screen.Bounds;
        SetWindowPos(hwnd, HwndTopmost, b.X, b.Y, b.Width, b.Height, SwpShowWindow);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (EditDrag.Active)
                EditDrag.Abort();
            else
                EditSession.Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EditSession.Apply();
            e.Handled = true;
        }
    }

    // ── Fondo ───────────────────────────────────────────────────────────────

    /// <summary>El fondo de pantalla (el de Wallpaper Engine si está abierto), a la resolución del monitor.</summary>
    private Image WallpaperImage()
    {
        var image = new Image { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
        try
        {
            string? path = WallpaperEngine.CurrentWallpaperImage() ?? Wallpaper.Current;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = _screen.Bounds.Width;
                bitmap.EndInit();
                bitmap.Freeze();
                image.Source = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Log.Error("Modo edición: no se pudo leer el fondo de pantalla", ex);
        }
        return image;
    }

    // ── Borrador ────────────────────────────────────────────────────────────

    /// <summary>Vuelve a dibujar las barras, los bordes libres y los widgets del borrador.</summary>
    private void Rebuild()
    {
        if (_closing)
            return;
        var layout = EditSession.LayoutOf(Device);
        _frame.Children.Clear();
        _bars.Clear();
        // Arriba y abajo primero: en el DockPanel, como en la pantalla, se quedan las esquinas.
        foreach (var bar in BarLayout.InCreationOrder(layout))
        {
            var preview = new BarPreview(Device, bar, layout);
            DockPanel.SetDock(preview.Root, bar.Edge switch
            {
                TaskbarPosition.Top => Dock.Top,
                TaskbarPosition.Left => Dock.Left,
                TaskbarPosition.Right => Dock.Right,
                _ => Dock.Bottom,
            });
            _frame.Children.Add(preview.Root);
            _bars.Add(preview);
        }
        _frame.Children.Add(_desktop);

        _edgeHints.Children.Clear();
        _placeholders.Clear();
        foreach (var edge in BarPreview.Edges.Where(e => layout.Bars.All(b => b.Edge != e)))
        {
            var hint = EdgePlaceholder(edge);
            _placeholders[edge] = hint;
            _edgeHints.Children.Add(hint);
        }

        bool isMain = (EditSession.MainScreen is { } main && Forms.Screen.AllScreens.Any(sc => sc.DeviceName == main)
            ? main : Forms.Screen.PrimaryScreen?.DeviceName) == Device;
        _mainBox.IsChecked = isMain;
        _mainBox.IsEnabled = !isMain;
        BuildWidgets();
        BuildBanner();
    }

    // ── Avisos ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Un aviso de ejemplo en el monitor y la esquina donde saldrán las notificaciones: se arrastra a
    /// otra esquina u otro monitor (antes, Hub › Notificaciones).
    /// </summary>
    private void BuildBanner()
    {
        _bannerLayer.Children.Clear();
        _banner = null;
        if (!Settings.Current.NotificationsEnabled || EditSession.BannerDevice != Device)
            return;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = Loc.T("Avisos de notificación"), FontSize = 13, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = Settings.Current.NotificationBanners ? Loc.T("Salen aquí. Arrástralo a otra esquina o a otro monitor.")
                : Loc.T("Están desactivados (Hub › Notificaciones); saldrían aquí."),
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
        });
        var row = new DockPanel();
        var icon = new SymbolIcon { Symbol = SymbolRegular.Alert24, FontSize = 22, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        var card = new Border
        {
            Width = BannerSize.Width,
            Height = BannerSize.Height,
            Padding = new Thickness(16, 10, 16, 10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = row,
            Cursor = Cursors.SizeAll,
            Opacity = Settings.Current.NotificationBanners ? 1 : 0.6,
            ToolTip = Loc.T("Dónde salen los avisos"),
        };
        card.SetResourceReference(Border.BackgroundProperty, "StrataPopupBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "StrataFlyoutBorderBrush");
        card.MouseLeftButtonDown += (_, e) => EditDrag.Press(card, new BannerPayload(), e);
        _banner = card;
        _bannerLayer.Children.Add(card);
        PlaceBanner();
    }

    private void PlaceBanner()
    {
        if (_banner is null)
            return;
        var rect = CornerRect(EditSession.ScreenOf(Device).NotificationCorner);
        Canvas.SetLeft(_banner, rect.Left);
        Canvas.SetTop(_banner, rect.Top);
    }

    /// <summary>Lugar del aviso en una esquina del escritorio (dentro de las barras), en DIP de su capa.</summary>
    private Rect CornerRect(NotificationCorner corner)
    {
        double width = _bannerLayer.ActualWidth, height = _bannerLayer.ActualHeight;
        bool top = corner.IsTop();
        double x = corner.IsCenter() ? Math.Max(0, (width - BannerSize.Width) / 2)
                 : corner.IsLeft() ? BannerGap
                 : Math.Max(0, width - BannerSize.Width - BannerGap);
        return new Rect(x,
                        top ? BannerGap : Math.Max(0, height - BannerSize.Height - BannerGap),
                        BannerSize.Width, BannerSize.Height);
    }

    // ── Color propio de las barras ──────────────────────────────────────────

    /// <summary>Panel con el editor del color propio de las barras de este monitor (va al borrador).</summary>
    private void OnEditColor(string device)
    {
        if (device != Device || _closing)
            return;
        if (_colorPanel != null)
            _desktop.Children.Remove(_colorPanel);
        var content = new StackPanel();
        content.Children.Add(Hub.Pages.ColorsPage.CustomColorCard(this, Loc.T("Color de la barra"),
            Loc.T("Con su opacidad: a menos, más se ve el material del tema detrás."), SymbolRegular.Color24,
            () => EditSession.ScreenOf(Device).BarColor, (_, _) => { }, toggle: false,
            save: value => EditSession.ChangeScreen(Device, s => s.BarColor = value)));
        var done = ToolButton(SymbolRegular.Checkmark24, Loc.T("Listo"), () => _colorPanel!.Visibility = Visibility.Collapsed);
        done.HorizontalAlignment = HorizontalAlignment.Right;
        content.Children.Add(done);
        _colorPanel = Floating(content, width: 560);
        _colorPanel.HorizontalAlignment = HorizontalAlignment.Center;
        _colorPanel.VerticalAlignment = VerticalAlignment.Center;
        _desktop.Children.Add(_colorPanel);
    }

    /// <summary>Un borde sin barra: un hueco punteado donde soltar un elemento (o clic) crea la barra.</summary>
    private FrameworkElement EdgePlaceholder(TaskbarPosition edge)
    {
        bool vertical = BarLayout.IsVertical(edge);
        var dash = new Rectangle
        {
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
            RadiusX = 8,
            RadiusY = 8,
            Fill = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
        };
        dash.SetResourceReference(Shape.StrokeProperty, "StrataForegroundBrush");
        var label = new TextBlock
        {
            Text = "+  " + BarLayout.BarName(edge),
            FontSize = 12,
            Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LayoutTransform = vertical ? new RotateTransform(edge == TaskbarPosition.Left ? -90 : 90) : Transform.Identity,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "StrataForegroundBrush");
        var grid = new Grid
        {
            Margin = new Thickness(6),
            Opacity = 0.55,
            Cursor = Cursors.Hand,
            ToolTip = Loc.T("Clic o suelta aquí un elemento para crear una barra en este borde"),
            HorizontalAlignment = edge switch { TaskbarPosition.Left => HorizontalAlignment.Left, TaskbarPosition.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Stretch },
            VerticalAlignment = edge switch { TaskbarPosition.Top => VerticalAlignment.Top, TaskbarPosition.Bottom => VerticalAlignment.Bottom, _ => VerticalAlignment.Stretch },
        };
        if (vertical)
        {
            grid.Width = EdgeHint;
            grid.Margin = new Thickness(6, EdgeHint + 12, 6, EdgeHint + 12);
        }
        else
        {
            grid.Height = EdgeHint;
            grid.Margin = new Thickness(EdgeHint + 12, 6, EdgeHint + 12, 6);
        }
        grid.Children.Add(dash);
        grid.Children.Add(label);
        grid.MouseEnter += (_, _) => grid.Opacity = 1;
        grid.MouseLeave += (_, _) => grid.Opacity = 0.55;
        grid.MouseLeftButtonUp += (_, e) =>
        {
            if (!EditDrag.Active)
                EditSession.AddBar(Device, edge);
            e.Handled = true;
        };
        var menu = new ContextMenu();
        menu.Items.Add(BarPreview.Entry(Loc.T("Agregar barra aquí"), () => EditSession.AddBar(Device, edge)));
        grid.ContextMenu = menu;
        return grid;
    }

    // ── Widgets ─────────────────────────────────────────────────────────────

    private void BuildWidgets()
    {
        _widgets.Children.Clear();
        _widgetBoxes.Clear();
        foreach (var widget in EditSession.Widgets.Where(w => (w.Device ?? MainDevice()) == Device))
        {
            var (box, size) = WidgetBox(widget);
            _widgetBoxes[widget.Id] = (box, size);
            _widgets.Children.Add(box);
        }
        PlaceWidgets();
    }

    private static string? MainDevice() =>
        App.MainScreenDevice(Forms.Screen.AllScreens.Select(s => (s.DeviceName, s.Primary)));

    /// <summary>
    /// El widget como se ve: si ya tiene ventana, su imagen en vivo (escalada si se cambió el
    /// tamaño); si es nuevo, una tarjeta con su nombre.
    /// </summary>
    private (FrameworkElement Box, Size Size) WidgetBox(WidgetConfig widget)
    {
        Size size;
        var grid = new Grid { Cursor = Cursors.SizeAll, ToolTip = WidgetCatalog.NameOf(widget) };
        if (WidgetManager.WindowOf(widget.Id) is { IsVisible: true, Content: Visual live } window && window.ActualWidth > 1)
        {
            double ratio = widget.Scale / Math.Max(0.1, window.Config.Scale);
            size = new Size(window.ActualWidth * ratio, window.ActualHeight * ratio);
            grid.Background = new VisualBrush(live) { Stretch = Stretch.Fill };
        }
        else
        {
            size = WidgetCatalog.EstimatedSize(widget);
            var card = new Border { CornerRadius = new CornerRadius(12), Opacity = 0.9 };
            card.SetResourceReference(Border.BackgroundProperty, "StrataPopupBackgroundBrush");
            var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new SymbolIcon { Symbol = WidgetCatalog.IconOf(widget), FontSize = 28 * widget.Scale, HorizontalAlignment = HorizontalAlignment.Center });
            label.Children.Add(new TextBlock { Text = WidgetCatalog.NameOf(widget), FontSize = 13 * widget.Scale, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
            card.Child = label;
            grid.Children.Add(card);
        }
        grid.Width = size.Width;
        grid.Height = size.Height;
        var outline = new Rectangle { StrokeThickness = 1.5, StrokeDashArray = [4, 3], RadiusX = 10, RadiusY = 10, Opacity = 0.7 };
        outline.SetResourceReference(Shape.StrokeProperty, "StrataForegroundBrush");
        grid.Children.Add(outline);
        grid.MouseEnter += (_, _) => outline.SetResourceReference(Shape.StrokeProperty, "StrataAccentBrush");
        grid.MouseLeave += (_, _) => outline.SetResourceReference(Shape.StrokeProperty, "StrataForegroundBrush");
        grid.MouseLeftButtonDown += (_, e) =>
        {
            var grab = e.GetPosition(grid);
            EditDrag.Press(grid, new WidgetPayload(widget, size, new Vector(grab.X, grab.Y)), e);
        };
        grid.ContextMenu = WidgetMenu(widget);
        return (grid, size);
    }

    private void PlaceWidgets()
    {
        double width = _widgets.ActualWidth, height = _widgets.ActualHeight;
        foreach (var widget in EditSession.Widgets)
        {
            if (!_widgetBoxes.TryGetValue(widget.Id, out var entry))
                continue;
            Canvas.SetLeft(entry.Box, Math.Clamp(widget.X, 0, 1) * Math.Max(0, width - entry.Size.Width));
            Canvas.SetTop(entry.Box, Math.Clamp(widget.Y, 0, 1) * Math.Max(0, height - entry.Size.Height));
        }
    }

    private ContextMenu WidgetMenu(WidgetConfig widget)
    {
        var menu = new ContextMenu();
        var size = new MenuItem { Header = Loc.T("Tamaño") };
        foreach (var (name, scale) in new[] { (Loc.T("Chico"), 0.75), (Loc.T("Mediano"), 1.0), (Loc.T("Grande"), 1.5) })
        {
            var entry = BarPreview.Entry(name, () => EditSession.ChangeWidget(widget.Id, w => w.Scale = scale));
            entry.IsCheckable = true;
            entry.IsChecked = Math.Abs(widget.Scale - scale) < 0.01;
            size.Items.Add(entry);
        }
        menu.Items.Add(size);
        if (widget.Kind == WidgetKind.Clock)
        {
            var date = BarPreview.Entry(Loc.T("Mostrar la fecha"), () => EditSession.ChangeWidget(widget.Id, w => w.ShowDate = !w.ShowDate));
            date.IsCheckable = true;
            date.IsChecked = widget.ShowDate;
            menu.Items.Add(date);
        }
        var locked = BarPreview.Entry(Loc.T("Bloquear posición"), () => EditSession.ChangeWidget(widget.Id, w => w.Locked = !w.Locked));
        locked.IsCheckable = true;
        locked.IsChecked = widget.Locked;
        menu.Items.Add(locked);
        menu.Items.Add(new Separator());
        menu.Items.Add(BarPreview.Entry(Loc.T("Quitar"), () => EditSession.RemoveWidget(widget.Id)));
        return menu;
    }

    // ── Soltar ──────────────────────────────────────────────────────────────

    /// <summary>Dónde caería lo arrastrado con el puntero en ese punto de la pantalla.</summary>
    public DropTarget? TargetAt(Point screen, DragPayload payload)
    {
        switch (payload)
        {
            case ItemPayload item:
                foreach (var bar in _bars)
                    if (bar.TargetAt(screen, item.Item, _root) is { } target)
                        return target;
                foreach (var (edge, hint) in _placeholders)
                    if (BarPreview.ScreenRect(hint, 8).Contains(screen))
                        return new NewBarTarget(edge, BarPreview.Local(hint, _root));
                return item.FromDevice != null ? new RemoveTarget(Rect.Empty) : null;

            case WidgetPayload widget:
            {
                // La esquina del widget donde quedaría, dentro del escritorio (lo que dejan las barras).
                var point = _widgets.PointFromScreen(screen) - widget.Grab;
                double width = _widgets.ActualWidth, height = _widgets.ActualHeight;
                double left = Math.Clamp(point.X, 0, Math.Max(0, width - widget.Size.Width));
                double top = Math.Clamp(point.Y, 0, Math.Max(0, height - widget.Size.Height));
                double x = width > widget.Size.Width ? left / (width - widget.Size.Width) : 0;
                double y = height > widget.Size.Height ? top / (height - widget.Size.Height) : 0;
                var marker = _widgets.TransformToVisual(_root).TransformBounds(new Rect(left, top, widget.Size.Width, widget.Size.Height));
                return new DesktopTarget(x, y, marker);
            }

            case BannerPayload:
            {
                // La esquina o el centro del borde más cercano al puntero (tercios a lo ancho).
                var point = _bannerLayer.PointFromScreen(screen);
                int column = Math.Clamp((int)(point.X / Math.Max(1, _bannerLayer.ActualWidth) * 3), 0, 2);
                bool top = point.Y < _bannerLayer.ActualHeight / 2;
                var corner = (column, top) switch
                {
                    (0, true) => NotificationCorner.TopLeft,
                    (1, true) => NotificationCorner.TopCenter,
                    (2, true) => NotificationCorner.TopRight,
                    (0, false) => NotificationCorner.BottomLeft,
                    (1, false) => NotificationCorner.BottomCenter,
                    _ => NotificationCorner.BottomRight,
                };
                return new CornerTarget(corner, _bannerLayer.TransformToVisual(_root).TransformBounds(CornerRect(corner)));
            }
        }
        return null;
    }

    /// <summary>Resalta dónde caería (null: nada).</summary>
    public void ShowMarker(DropTarget? target)
    {
        bool removing = target is RemoveTarget;
        if (removing)
            ShowMessage(Loc.T("Suelta aquí para quitarlo de las barras"), seconds: 0);
        else if (_message.Tag is "drag")
            HideMessage();
        if (target is null || target.Marker.IsEmpty)
        {
            _marker.Visibility = Visibility.Collapsed;
            return;
        }
        var rect = target.Marker;
        bool line = target is BarTarget && (rect.Width <= 4 || rect.Height <= 4);
        _marker.Visibility = Visibility.Visible;
        _marker.Width = Math.Max(3, rect.Width);
        _marker.Height = Math.Max(3, rect.Height);
        _marker.CornerRadius = new CornerRadius(line ? 1.5 : 6);
        if (line)
            _marker.SetResourceReference(Border.BackgroundProperty, "StrataAccentBrush");
        else
            _marker.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        Canvas.SetLeft(_marker, rect.Left);
        Canvas.SetTop(_marker, rect.Top);
    }

    /// <summary>Mientras se arrastra: zonas vacías visibles en las barras y bordes libres resaltados.</summary>
    public void ShowDropZones(bool show)
    {
        foreach (var bar in _bars)
            bar.ShowDropZones(show);
        foreach (var hint in _placeholders.Values)
            hint.Opacity = show ? 0.9 : 0.55;
        if (!show)
        {
            _marker.Visibility = Visibility.Collapsed;
            if (_message.Tag is "drag")
                HideMessage();
        }
    }

    // ── Herramientas ────────────────────────────────────────────────────────

    /// <summary>Un panel flotante con el aspecto de los paneles de Strata UI, que se mueve arrastrando su borde.</summary>
    private Border Floating(UIElement content, double width = double.NaN)
    {
        var panel = new Border
        {
            Child = content,
            Width = width,
            Padding = new Thickness(14, 10, 14, 12),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 },
            RenderTransform = new TranslateTransform(),
        };
        panel.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        panel.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        Movable(panel);
        return panel;
    }

    /// <summary>Arrastrar el panel por su fondo (no por sus botones) lo mueve.</summary>
    private static void Movable(Border panel)
    {
        Point? start = null;
        var transform = (TranslateTransform)panel.RenderTransform;
        Point origin = default;
        panel.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (Border or StackPanel or TextBlock or Grid))
                return;
            start = e.GetPosition(null);
            origin = new Point(transform.X, transform.Y);
            panel.CaptureMouse();
            e.Handled = true;
        };
        panel.MouseMove += (_, e) =>
        {
            if (start is not { } s || e.LeftButton != MouseButtonState.Pressed)
                return;
            var now = e.GetPosition(null);
            transform.X = origin.X + now.X - s.X;
            transform.Y = origin.Y + now.Y - s.Y;
        };
        panel.MouseLeftButtonUp += (_, _) =>
        {
            start = null;
            if (panel.IsMouseCaptured)
                panel.ReleaseMouseCapture();
        };
    }

    private Border Toolbar()
    {
        var screens = Forms.Screen.AllScreens.ToList();
        int index = screens.FindIndex(sc => sc.DeviceName == Device);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        title.Children.Add(new TextBlock { Text = Loc.T("Modo edición"), FontSize = 15, FontWeight = FontWeights.SemiBold });
        title.Children.Add(new TextBlock
        {
            Text = screens.Count > 1 ? Hub.ScreenPicker.NameOf(_screen, index, screens.Count) : Loc.T("Este monitor"),
            FontSize = 12,
            Opacity = 0.7,
        });

        var library = ToolButton(SymbolRegular.Library24, Loc.T("Biblioteca"), () =>
            _library.Visibility = _library.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
        library.ToolTip = Loc.T("Elementos y widgets para arrastrar");

        _mainBox.Content = Loc.T("Monitor principal");
        _mainBox.ToolTip = Loc.T("Su barra lleva la tecla Windows y las ventanas de los monitores sin apps abiertas");
        _mainBox.Margin = new Thickness(6, 0, 10, 0);
        _mainBox.VerticalAlignment = VerticalAlignment.Center;
        _mainBox.Click += (_, _) =>
        {
            if (_mainBox.IsChecked == true)
                EditSession.SetMainScreen(Device);
        };
        _mainBox.Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        var reset = ToolButton(SymbolRegular.ArrowReset24, Loc.T("Restablecer"), () => EditSession.ResetScreen(Device));
        reset.ToolTip = Loc.T("Este monitor vuelve a una barra abajo con lo de siempre");
        var cancel = ToolButton(SymbolRegular.Dismiss24, Loc.T("Cancelar"), EditSession.Cancel);
        cancel.ToolTip = Loc.T("Descarta los cambios (Esc)");
        var apply = ToolButton(SymbolRegular.Checkmark24, Loc.T("Aplicar"), EditSession.Apply);
        apply.Appearance = ControlAppearance.Primary;
        apply.ToolTip = Loc.T("Guarda y aplica el diseño de todos los monitores (Enter)");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new SymbolIcon { Symbol = SymbolRegular.DesignIdeas24, FontSize = 22, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(title);
        row.Children.Add(library);
        row.Children.Add(_mainBox);
        row.Children.Add(reset);
        row.Children.Add(cancel);
        row.Children.Add(apply);

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock
        {
            Text = Loc.T("Arrastra los elementos entre barras y monitores, o fuera de la barra para quitarlos. Clic derecho para más opciones."),
            FontSize = 11.5,
            Opacity = 0.65,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 640,
        });
        stack.Children.Add(_message);
        return Floating(stack);
    }

    private static Button ToolButton(SymbolRegular icon, string text, Action action)
    {
        var button = new Button
        {
            Icon = new SymbolIcon { Symbol = icon },
            Content = text,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"Modo edición: \"{text}\" falló", ex);
            }
        };
        return button;
    }

    /// <summary>Un aviso bajo la barra de herramientas (unos segundos; 0: hasta que se quite).</summary>
    public void ShowMessage(string text, double seconds = 5)
    {
        _message.Text = text;
        _message.Tag = seconds == 0 ? "drag" : null;
        _message.Visibility = Visibility.Visible;
        _message.SetResourceReference(TextBlock.ForegroundProperty, seconds == 0 ? "TextFillColorPrimaryBrush" : "SystemFillColorCautionBrush");
        _messageTimer?.Stop();
        if (seconds <= 0)
            return;
        _messageTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _messageTimer.Tick += (_, _) => HideMessage();
        _messageTimer.Start();
    }

    private void HideMessage()
    {
        _messageTimer?.Stop();
        _message.Tag = null;
        _message.Visibility = Visibility.Collapsed;
    }

    private ContextMenu DesktopMenu()
    {
        var menu = new ContextMenu();
        var layout = EditSession.LayoutOf(Device);
        var bars = new MenuItem { Header = Loc.T("Agregar barra") };
        foreach (var edge in BarPreview.Edges.Where(e => layout.Bars.All(b => b.Edge != e)))
            bars.Items.Add(BarPreview.Entry(BarLayout.EdgeName(edge), () => EditSession.AddBar(Device, edge)));
        bars.IsEnabled = bars.Items.Count > 0;
        menu.Items.Add(bars);
        var widgets = new MenuItem { Header = Loc.T("Agregar widget") };
        foreach (var entry in WidgetCatalog.All())
            widgets.Items.Add(BarPreview.Entry(entry.Name, () => EditSession.PlaceWidget(entry.Create(), Device, 0.5, 0.3)));
        menu.Items.Add(widgets);
        menu.Items.Add(BarPreview.Entry(Loc.T("Mostrar u ocultar la biblioteca"), () =>
            _library.Visibility = _library.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        menu.Items.Add(new Separator());
        menu.Items.Add(BarPreview.Entry(Loc.T("Restablecer este monitor"), () => EditSession.ResetScreen(Device)));
        menu.Items.Add(new Separator());
        menu.Items.Add(BarPreview.Entry(Loc.T("Aplicar"), EditSession.Apply));
        menu.Items.Add(BarPreview.Entry(Loc.T("Cancelar"), EditSession.Cancel));
        return menu;
    }

    // ── Win32 ───────────────────────────────────────────────────────────────

    private const int GwlExStyle = -20, WsExToolWindow = 0x80;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
