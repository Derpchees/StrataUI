using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Strata.Shell.Services;

namespace Strata.Shell.Controls;

/// <summary>
/// Lo que se arrastra en la barra (un elemento, una ventana o un anclado) se ve en la mano: una
/// imagen suya con un marco del color de acento que sigue al puntero en cada movimiento, también de
/// una barra o un monitor a otro. Va en una ventana transparente propia, que no recibe el mouse.
/// </summary>
public static class DragGhost
{
    private static Window? _window;
    private static Point _grab; // dónde se tomó el elemento, en píxeles físicos desde su esquina
    private static double _dpi = 1;

    /// <summary>Empieza a mostrar <paramref name="element"/> bajo el puntero, tomado en el punto actual.</summary>
    public static void Begin(FrameworkElement element)
    {
        End();
        if (element.ActualWidth < 1 || element.ActualHeight < 1 || PresentationSource.FromVisual(element) is null)
            return;
        try
        {
            _dpi = VisualTreeHelper.GetDpi(element).DpiScaleX;
            var topLeft = element.PointToScreen(new Point(0, 0));
            GetCursorPos(out var cursor);
            _grab = new Point(cursor.X - topLeft.X, cursor.Y - topLeft.Y);

            const double pad = 3; // el marco va por fuera de la imagen
            var card = new Border
            {
                Padding = new Thickness(pad - 1),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Child = new Image
                {
                    Source = Snapshot(element),
                    Width = element.ActualWidth,
                    Height = element.ActualHeight,
                    Stretch = Stretch.Fill,
                },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            card.SetResourceReference(Border.BorderBrushProperty, "StrataAccentBrush");
            card.SetResourceReference(Border.BackgroundProperty, "StrataPopupBackgroundBrush");
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 };

            // La imagen queda corrida por el margen de la sombra (6), el marco (2) y su relleno (2).
            _grab.Offset(10 * _dpi, 10 * _dpi);
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                // El estilo de ventanas de WPF-UI impone un tamaño mínimo pensado para ventanas normales.
                Style = null,
                MinWidth = 0,
                MinHeight = 0,
                Left = -32000,
                Top = -32000,
                Content = new Border { Padding = new Thickness(6), Child = card },
            };
            _window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(_window).Handle;
                SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExNoActivate | WsExToolWindow);
            };
            _window.Show();
            Move();
            // En cada cuadro, no solo con los eventos del mouse: así no se queda atrás del puntero.
            CompositionTarget.Rendering += OnRendering;

            // Se "levanta": crece un poco y vuelve, con la velocidad elegida.
            if (Settings.Current.PanelAnimation != PanelAnimation.None)
            {
                var lift = new DoubleAnimation(1, 1.08, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
                ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, lift);
                ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, lift);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Log.Error("No se pudo mostrar lo que se arrastra", ex);
            End();
        }
    }

    /// <summary>Lleva la imagen al puntero (en cada movimiento del arrastre).</summary>
    public static void Move()
    {
        if (_window is null)
            return;
        GetCursorPos(out var cursor);
        SetWindowPos(new WindowInteropHelper(_window).Handle, IntPtr.Zero,
                     (int)Math.Round(cursor.X - _grab.X), (int)Math.Round(cursor.Y - _grab.Y), 0, 0,
                     SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private static void OnRendering(object? sender, EventArgs e) => Move();

    public static void End()
    {
        CompositionTarget.Rendering -= OnRendering;
        _window?.Close();
        _window = null;
    }

    /// <summary>Imagen del elemento tal como se ve (Render lo dibuja corrido por su posición: se recorta).</summary>
    private static BitmapSource Snapshot(FrameworkElement element)
    {
        double dpi = VisualTreeHelper.GetDpi(element).PixelsPerDip;
        var offset = VisualTreeHelper.GetOffset(element);
        int left = (int)Math.Floor(Math.Max(0, offset.X) * dpi), top = (int)Math.Floor(Math.Max(0, offset.Y) * dpi);
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi)), height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi));
        var full = new RenderTargetBitmap(left + width, top + height, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        full.Render(element);
        var cropped = new CroppedBitmap(full, new Int32Rect(left, top, width, height));
        cropped.Freeze();
        return cropped;
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
