using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Strata.Shell.Services;

/// <summary>
/// Algunas apps leen sin signo la posición del clic que les manda la bandeja (WhatsApp): en un
/// monitor a la izquierda o arriba del principal (coordenadas negativas) su menú sale en el borde
/// derecho o inferior de la pantalla. En Windows no se nota porque la bandeja siempre está en el
/// principal. Después de un clic en esos monitores se busca la ventana nueva que abrió la app y, si
/// quedó en otro monitor, se lleva junto al cursor.
/// </summary>
public static class TrayMenuFix
{
    private static DispatcherTimer? _timer;

    /// <param name="iconWindow">Ventana dueña del ícono (la de la app).</param>
    public static void Watch(IntPtr iconWindow)
    {
        if (!GetCursorPos(out var cursor) || (cursor.X >= 0 && cursor.Y >= 0))
            return;
        GetWindowThreadProcessId(iconWindow, out uint pid);
        if (pid == 0)
            return;
        var before = VisibleWindows(pid);
        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        int ticks = 0;

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) =>
        {
            // La app tarda un momento en abrir su menú; se espera hasta un segundo.
            if (++ticks > 25)
            {
                _timer.Stop();
                return;
            }
            foreach (var window in VisibleWindows(pid).Except(before))
            {
                _timer.Stop();
                if (GetWindowRect(window, out var rect) && MonitorFromRect(ref rect, MonitorDefaultToNearest) != monitor)
                    MoveNextTo(window, rect, cursor, monitor);
                return;
            }
        };
        _timer.Start();
    }

    private static DispatcherTimer? _keepTimer;

    /// <summary>
    /// El panel de íconos ocultos (un Popup que se cierra al perder el mouse) sigue abierto mientras la
    /// app muestra su menú: si se cerraba cuando ella tomaba el foco, su menú se cerraba también o
    /// quedaba sin poder usarse. Cuando el menú se va, se cierra el panel (como en Windows). Si la app
    /// no abre ningún menú (abre su ventana, o nada), el panel vuelve a cerrarse con un clic afuera.
    /// </summary>
    public static void KeepOpenWhileMenu(System.Windows.Controls.Primitives.Popup popup, IntPtr iconWindow)
    {
        GetWindowThreadProcessId(iconWindow, out uint pid);
        if (pid == 0)
            return;
        var before = VisibleWindows(pid);
        popup.StaysOpen = true;
        IntPtr menu = IntPtr.Zero;
        int ticks = 0;

        _keepTimer?.Stop();
        var timer = _keepTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        void Finish(bool close)
        {
            timer.Stop();
            popup.StaysOpen = false; // vuelve a tomar el mouse: un clic afuera lo cierra
            if (close)
                popup.IsOpen = false;
        }
        timer.Tick += (_, _) =>
        {
            ticks++;
            if (!popup.IsOpen)
            {
                timer.Stop();
                popup.StaysOpen = false;
                return;
            }
            if (menu == IntPtr.Zero)
            {
                menu = VisibleWindows(pid).Except(before).FirstOrDefault(IsMenuLike);
                if (menu == IntPtr.Zero && ticks > 25) // un segundo y medio sin menú
                    Finish(close: false);
                return;
            }
            if (!IsWindow(menu) || !IsWindowVisible(menu))
                Finish(close: true);
        };
        timer.Start();
    }

    /// <summary>Un menú (#32768) o una ventana emergente sin barra de título (los menús propios de algunas apps).</summary>
    private static bool IsMenuLike(IntPtr window)
    {
        var name = new System.Text.StringBuilder(32);
        if (GetClassName(window, name, name.Capacity) > 0 && name.ToString() == "#32768")
            return true;
        long style = GetWindowLongPtr(window, GwlStyle).ToInt64();
        return (style & WsPopup) != 0 && (style & WsCaption) != WsCaption;
    }

    private const int GwlStyle = -16;
    private const long WsPopup = 0x80000000L, WsCaption = 0x00C00000L;

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    /// <summary>Junto al cursor, dentro del área de trabajo del monitor (hacia arriba o a la izquierda si no cabe).</summary>
    private static void MoveNextTo(IntPtr window, NativeRect rect, NativePoint cursor, IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;
        var work = info.Work;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int x = cursor.X + width <= work.Right ? cursor.X : cursor.X - width;
        int y = cursor.Y + height <= work.Bottom ? cursor.Y : cursor.Y - height;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        Log.Info($"Menú de bandeja movido al monitor del clic ({rect.Left},{rect.Top} → {x},{y})");
    }

    private static HashSet<IntPtr> VisibleWindows(uint pid)
    {
        var windows = new HashSet<IntPtr>();
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && GetWindowThreadProcessId(window, out uint owner) != 0 && owner == pid)
                windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
