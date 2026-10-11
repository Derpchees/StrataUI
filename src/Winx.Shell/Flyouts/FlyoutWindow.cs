using System.Runtime.InteropServices;
using System.Windows;
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
    private FrameworkElement? _anchor;

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

    public void ShowAt(FrameworkElement anchor)
    {
        _anchor = anchor;
        Show();
        PositionAbove(anchor);
        // Si el contenido cambia de tamaño (listas que terminan de cargar), se mantiene pegado a la barra.
        SizeChanged += (_, _) => PositionAbove(anchor);
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

    private void PositionAbove(FrameworkElement anchor)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        GetWindowRect(hwnd, out var self);
        int width = self.Right - self.Left;
        int height = self.Bottom - self.Top;

        // Todo en píxeles físicos: PointToScreen ya los devuelve así en modo PerMonitorV2.
        var anchorRight = anchor.PointToScreen(new Point(anchor.ActualWidth, 0));
        var anchorCenter = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        var work = Forms.Screen.FromPoint(new System.Drawing.Point((int)anchorCenter.X, (int)anchorCenter.Y)).WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

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
