using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Services;

/// <summary>
/// Los cuadros de diálogo (confirmar una desinstalación, avisos de error…) salen en el monitor donde
/// se hizo la acción: el del puntero. WPF-UI los centraba sobre la ventana principal de la app, que
/// es invisible y está en el monitor principal de Windows.
/// </summary>
public static class DialogPlacement
{
    /// <summary>Todos los MessageBox de WPF-UI de la app (también los del módulo de optimización).</summary>
    public static void Watch() =>
        EventManager.RegisterClassHandler(typeof(Wpf.Ui.Controls.MessageBox), FrameworkElement.LoadedEvent,
                                          new RoutedEventHandler((sender, _) =>
                                          {
                                              if (sender is Window window)
                                                  CenterOnPointerScreen(new WindowInteropHelper(window).Handle);
                                          }));

    /// <summary>Centra la ventana (en píxeles físicos) en el área de trabajo del monitor del puntero.</summary>
    public static void CenterOnPointerScreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return;
        var work = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int x = work.Left + Math.Max(0, (work.Width - width) / 2);
        int y = work.Top + Math.Max(0, (work.Height - height) / 2);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>
    /// Ventana invisible en el centro del monitor del puntero, para usarla de dueña de un diálogo de
    /// Windows (abrir archivo): Windows lo centra sobre su dueña.
    /// </summary>
    public static Window PointerAnchor()
    {
        var anchor = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = true,
            Opacity = 0,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
        };
        anchor.SourceInitialized += (_, _) => CenterOnPointerScreen(new WindowInteropHelper(anchor).Handle);
        anchor.Show();
        return anchor;
    }

    private const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
