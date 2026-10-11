using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Flyouts;

/// <summary>
/// Panel flotante que aparece sobre un botón de la barra (volumen, red, Bluetooth…).
/// Usa el material Acrylic de Windows 11 y se cierra al perder el foco o con Esc.
/// </summary>
public abstract class FlyoutWindow : FluentWindow
{
    private const int GapPx = 12;
    private bool _closing;

    protected FlyoutWindow()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        MinHeight = 0; // el estilo de FluentWindow impone un mínimo pensado para ventanas normales
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = WindowBackdropType.Acrylic;
        WindowCornerPreference = WindowCornerPreference.Round;
        Opacity = 0; // se muestra al terminar de posicionarse, sin parpadeo

        Deactivated += (_, _) => CloseFlyout();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                CloseFlyout();
        };
    }

    /// <summary>
    /// Todo el contenido va dentro de un único ScrollViewer: si el panel no cabe en la pantalla
    /// aparece la barra de desplazamiento y la rueda del mouse funciona en cualquier parte.
    /// (Las subclases no deben anidar otros ScrollViewer, que "atraparían" la rueda.)
    /// </summary>
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        if (newContent is UIElement element and not ScrollViewer)
        {
            Content = new ScrollViewer
            {
                Content = element,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly,
            };
            return;
        }
        base.OnContentChanged(oldContent, newContent);
    }

    public void ShowAt(FrameworkElement anchor)
    {
        Show();
        LimitHeightToScreen(anchor);
        PositionAbove(anchor);
        // Si el contenido cambia de tamaño (secciones que se expanden, listas que terminan de cargar),
        // se mantiene pegado a la barra. Se difiere hasta después del render: al disparar SizeChanged
        // la ventana nativa todavía tiene el tamaño anterior.
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(() => PositionAbove(anchor),
                                                        System.Windows.Threading.DispatcherPriority.Render);
        Activate();
        AnimateIn();
    }

    public void CloseFlyout()
    {
        if (_closing)
            return;
        _closing = true;
        Dispatcher.BeginInvoke(Close);
    }

    private void LimitHeightToScreen(FrameworkElement anchor)
    {
        var center = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        var work = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y)).WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        MaxHeight = work.Height / scale - 2 * GapPx;
    }

    private void PositionAbove(FrameworkElement anchor)
    {
        if (_closing || !anchor.IsVisible)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // Tamaño según WPF (ya actualizado), convertido a píxeles físicos.
        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);

        // Todo en píxeles físicos: PointToScreen ya los devuelve así en modo PerMonitorV2.
        var anchorRight = anchor.PointToScreen(new Point(anchor.ActualWidth, 0));
        var anchorCenter = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        var work = Forms.Screen.FromPoint(new System.Drawing.Point((int)anchorCenter.X, (int)anchorCenter.Y)).WorkingArea;
        int gap = (int)(GapPx * scale);

        int x = Math.Clamp((int)anchorRight.X - width, work.Left + gap, work.Right - width - gap);
        int y = work.Bottom - height - gap;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void AnimateIn()
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (Content is FrameworkElement content)
        {
            var slide = new TranslateTransform(0, 16);
            content.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, duration) { EasingFunction = ease });
        }
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

/// <summary>Garantiza un solo panel abierto y que el botón funcione como interruptor.</summary>
public static class FlyoutManager
{
    private static FlyoutWindow? _open;
    private static Type? _lastClosedType;
    private static DateTime _lastClosedAt;

    public static void Toggle<T>(FrameworkElement anchor) where T : FlyoutWindow, new()
    {
        if (_open is T current)
        {
            current.CloseFlyout();
            return;
        }

        // El clic sobre el botón ya cerró el panel al quitarle el foco: no reabrirlo.
        if (_lastClosedType == typeof(T) && DateTime.UtcNow - _lastClosedAt < TimeSpan.FromMilliseconds(350))
            return;

        _open?.CloseFlyout();
        var flyout = new T();
        flyout.Closed += (_, _) =>
        {
            _lastClosedType = typeof(T);
            _lastClosedAt = DateTime.UtcNow;
            if (ReferenceEquals(_open, flyout))
                _open = null;
        };
        _open = flyout;
        flyout.ShowAt(anchor);
    }

    public static void CloseAll() => _open?.CloseFlyout();
}
