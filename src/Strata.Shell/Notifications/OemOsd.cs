using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Strata.Shell.Notifications;

/// <summary>
/// Avisos en pantalla de las utilidades del fabricante que duplican los de Strata UI: en los Lenovo,
/// FnHotkeyUtility muestra su propio cuadro gris con las teclas Fn (brillo, Bloq Num, micrófono,
/// cámara…). Mientras Strata UI muestre sus avisos, ese cuadro se oculta al aparecer.
/// </summary>
internal static class OemOsd
{
    private static readonly string[] Utilities = ["FnHotkeyUtility"];

    private static IntPtr _hook;
    private static WinEventProc? _proc;
    private static Func<bool> _active = () => false;
    private static readonly Dictionary<int, bool> Pids = new();

    /// <param name="active">Si en este momento se ocultan (algún monitor tiene avisos en pantalla).</param>
    public static void Start(Func<bool> active)
    {
        _active = active;
        if (_hook != IntPtr.Zero)
            return;
        _proc = OnShow;
        _hook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _proc, 0, 0,
                                WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero)
            UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
    }

    private static void OnShow(IntPtr hook, uint e, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero || GetAncestor(hwnd, GaRoot) != hwnd || !IsUtility(hwnd) || !_active())
            return;
        ShowWindow(hwnd, SwHide);
    }

    private static bool IsUtility(IntPtr hwnd)
    {
        var name = new StringBuilder(32);
        if (GetClassName(hwnd, name, name.Capacity) == 0 || name.ToString() != "#32770")
            return false;
        GetWindowThreadProcessId(hwnd, out int pid);
        if (!Pids.TryGetValue(pid, out bool utility))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                utility = Utilities.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                utility = false; // ya terminó o no se puede abrir
            }
            Pids[pid] = utility;
        }
        return utility;
    }

    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0x0000, WinEventSkipOwnProcess = 0x0002;
    private const uint GaRoot = 2;
    private const int SwHide = 0;

    private delegate void WinEventProc(IntPtr hook, uint e, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
