using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes.Glass;

namespace Strata.Shell.StartMenu;

/// <summary>
/// Foto del menú Perfil en su propia ventana transparente, centrada sobre el borde superior del
/// menú. Va aparte porque el desenfoque de DWM ocupa siempre el rectángulo completo de la ventana
/// del menú: dentro de ella la parte que sobresale no podría ser transparente.
/// No se activa ni recibe clics, así que el menú conserva el foco.
/// </summary>
public partial class ProfileAvatarWindow : Window
{
    private readonly Window _menu;

    public ProfileAvatarWindow(Window menu)
    {
        InitializeComponent();
        _menu = menu;
        DataContext = menu.DataContext;
        Topmost = true;
        Opacity = 0; // hasta que el menú se dibuje (Reveal): su primer cuadro tarda más que el de la foto

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExTransparent | WsExToolWindow);
            if (ThemeManager.UseLiveGlass)
                BackdropCapture.ExcludeFromCapture(hwnd, true); // que el vidrio del menú no la capture
        };
        menu.LocationChanged += (_, _) => Follow();
        menu.SizeChanged += (_, _) => Dispatcher.BeginInvoke(Follow, System.Windows.Threading.DispatcherPriority.Render);
        menu.Closed += (_, _) => Close();
    }

    /// <summary>El menú ya se ve: la foto sigue su opacidad (entra y sale con él).</summary>
    public void Reveal()
    {
        if (!_revealed)
            SetBinding(OpacityProperty, new Binding(nameof(Opacity)) { Source = _menu });
        _revealed = true;
    }

    private bool _revealed;

    /// <summary>Centra la foto sobre el borde superior del menú (mitad fuera, mitad dentro).</summary>
    public void Follow()
    {
        var menuHwnd = new WindowInteropHelper(_menu).Handle;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (menuHwnd == IntPtr.Zero || hwnd == IntPtr.Zero || !GetWindowRect(menuHwnd, out var r))
            return;
        double scale = VisualTreeHelper.GetDpi(_menu).DpiScaleX;
        int half = (int)Math.Round(Width * scale / 2);
        SetWindowPos(hwnd, IntPtr.Zero, (r.Left + r.Right) / 2 - half, r.Top - half, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000, WsExTransparent = 0x20, WsExToolWindow = 0x80;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
