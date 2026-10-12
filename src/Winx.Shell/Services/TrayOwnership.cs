using System.Runtime.InteropServices;

namespace Winx.Shell.Services;

/// <summary>
/// Vigila que los íconos de la bandeja lleguen a WINX. Las apps los mandan a la primera
/// <c>Shell_TrayWnd</c> que encuentra <c>FindWindow</c>; a veces es la del Explorador (en Windows
/// 26300 queda en una capa del sistema que WINX no puede pasar) y la bandeja de WINX se queda sin
/// esos íconos. Cuando la de WINX vuelve a ser la primera, se avisa "TaskbarCreated" (lo mismo que
/// hace el Explorador al reiniciarse) para que las apps vuelvan a registrar sus íconos aquí.
/// </summary>
public static class TrayOwnership
{
    private static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private static readonly uint OwnPid = (uint)Environment.ProcessId;
    private static Timer? _timer;
    private static bool? _wasOurs;

    public static void Start() =>
        _timer ??= new Timer(_ => Check(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static void Check()
    {
        try
        {
            bool ours = IsOurs(FindWindow("Shell_TrayWnd", null));
            if (!ours)
                RaiseOwn();
            if (ours == _wasOurs)
                return;

            // También la primera vez: al iniciar, el aviso de ManagedShell pudo llegar cuando la primera
            // era todavía la del Explorador.
            if (ours)
            {
                Log.Info("La bandeja de WINX vuelve a recibir los íconos: se piden de nuevo a las apps");
                SendNotifyMessage(HwndBroadcast, TaskbarCreated, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                Log.Info("Los íconos de la bandeja van a la barra del Explorador; se esperará a recuperarlos");
            }
            _wasOurs = ours;
        }
        catch (Exception ex)
        {
            Log.Error("Vigilancia de la bandeja", ex);
        }
    }

    private static bool IsOurs(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == OwnPid;
    }

    /// <summary>Intenta poner la ventana de WINX delante de la del Explorador (no siempre se puede).</summary>
    private static void RaiseOwn()
    {
        var hwnd = IntPtr.Zero;
        while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            if (IsOurs(hwnd))
            {
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                return;
            }
        }
    }

    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool SendNotifyMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
