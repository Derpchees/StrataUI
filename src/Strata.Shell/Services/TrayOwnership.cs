using System.Runtime.InteropServices;

namespace Strata.Shell.Services;

/// <summary>
/// Vigila que los íconos de la bandeja lleguen a Strata UI. Las apps los mandan a la primera
/// <c>Shell_TrayWnd</c> que encuentra <c>FindWindow</c>; a veces es la del Explorador (en Windows
/// 26300 queda en una capa del sistema que Strata UI no puede pasar) y la bandeja de Strata UI se queda sin
/// esos íconos. Cuando la de Strata UI vuelve a ser la primera, se avisa "TaskbarCreated" (lo mismo que
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
                Log.Info("La bandeja de Strata UI vuelve a recibir los íconos: se piden de nuevo a las apps");
                SendNotifyMessage(HwndBroadcast, TaskbarCreated, IntPtr.Zero, IntPtr.Zero);
                Task.Delay(5000).ContinueWith(_ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(LogIcons));
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

    /// <summary>Qué íconos tiene la bandeja (para comparar con los de Windows al revisar el registro).</summary>
    private static void LogIcons()
    {
        var icons = App.Current.Shell.NotificationArea.TrayIcons.ToList();
        Log.Info($"Bandeja: {icons.Count} íconos: " + string.Join(" | ", icons.Select(i =>
            $"{System.IO.Path.GetFileName(i.Path)} \"{i.Title?.Split('\n')[0]}\""
            + (i.IsPinned ? " [anclado]" : "") + (i.IsHidden ? " [escondido por la app]" : ""))));
    }

    private static bool IsOurs(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == OwnPid;
    }

    /// <summary>La ventana Shell_TrayWnd que creó ManagedShell para Strata UI.</summary>
    public static IntPtr OwnTrayWindow()
    {
        var hwnd = IntPtr.Zero;
        while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            if (IsOurs(hwnd))
                return hwnd;
        }
        return IntPtr.Zero;
    }

    /// <summary>Intenta poner la ventana de Strata UI delante de la del Explorador (no siempre se puede).</summary>
    private static void RaiseOwn()
    {
        var hwnd = OwnTrayWindow();
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
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
