using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Services;

/// <summary>
/// Los cuadros de diálogo (confirmar una desinstalación, avisos de error…) salen en el monitor donde
/// se hizo la acción: el de su ventana dueña o el de la ventana de Strata UI activa (el explorador
/// en el otro monitor, aunque el puntero quedó en este) y, si no hay ninguna, el del puntero. WPF-UI
/// los centraba sobre la ventana principal de la app, que es invisible y está en el monitor principal.
/// </summary>
public static class DialogPlacement
{
    /// <summary>Todos los MessageBox de WPF-UI de la app (también los del módulo de optimización).</summary>
    public static void Watch() =>
        EventManager.RegisterClassHandler(typeof(Wpf.Ui.Controls.MessageBox), FrameworkElement.LoadedEvent,
                                          new RoutedEventHandler((sender, _) =>
                                          {
                                              if (sender is Window window)
                                                  Place(window);
                                          }));

    /// <summary>En el monitor de la ventana que lo abrió (su dueña o la activa) o, sin ninguna, en el del puntero.</summary>
    public static void Place(Window dialog)
    {
        IntPtr hwnd = new WindowInteropHelper(dialog).Handle;
        var origin = dialog.Owner is { IsVisible: true, ActualWidth: > 1 } owner ? owner
            : Application.Current?.Windows.OfType<Window>()
                .FirstOrDefault(w => w != dialog && w.IsActive && w.IsVisible && w.ActualWidth > 1);
        if (origin != null && new WindowInteropHelper(origin).Handle is var originHandle && originHandle != IntPtr.Zero
            && GetWindowRect(originHandle, out var area))
        {
            var center = new System.Drawing.Point((area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2);
            Center(hwnd, Forms.Screen.FromPoint(center).WorkingArea);
            return;
        }
        CenterOnPointerScreen(hwnd);
    }

    /// <summary>Centra la ventana (en píxeles físicos) en el área de trabajo del monitor del puntero.</summary>
    public static void CenterOnPointerScreen(IntPtr hwnd) =>
        Center(hwnd, Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea);

    private static void Center(IntPtr hwnd, System.Drawing.Rectangle work)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return;
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
            // Sin el estilo de WPF-UI, que impone un tamaño mínimo y la corría del centro.
            Style = null,
            MinWidth = 0,
            MinHeight = 0,
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
