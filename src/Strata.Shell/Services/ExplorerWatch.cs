using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Strata.Shell.Services;

/// <summary>
/// Avisa cuando la barra del Explorador aparece de nuevo: al reiniciarse el Explorador (o si Strata UI
/// arrancó antes que él al iniciar sesión). El espacio que reservan las barras (appbars) lo lleva el
/// Explorador; uno nuevo no conoce las de Strata UI y las ventanas maximizadas quedaban debajo de ellas,
/// así que hay que registrarlas otra vez.
/// </summary>
/// <remarks>
/// Sin temporizador mientras el Explorador corre: se espera a que termine su proceso. Solo cuando no
/// hay barra del Explorador se consulta cada segundo hasta que aparece.
/// </remarks>
public static class ExplorerWatch
{
    /// <summary>La barra del Explorador apareció (en el hilo de la UI).</summary>
    public static event Action? Restarted;

    private static Process? _explorer;
    private static CancellationTokenSource? _cancel;

    public static void Start()
    {
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        if (ExplorerTaskbarProcess() is { } explorer)
            Watch(explorer, token);
        else
            _ = WaitForExplorerAsync(token);
    }

    public static void Stop()
    {
        _cancel?.Cancel();
        _explorer?.Dispose();
        _explorer = null;
    }

    private static void Watch(Process explorer, CancellationToken token)
    {
        _explorer?.Dispose();
        _explorer = explorer;
        try
        {
            explorer.EnableRaisingEvents = true;
            explorer.Exited += (_, _) =>
            {
                if (!token.IsCancellationRequested)
                {
                    Log.Info("El Explorador se cerró; se esperan su barra y su bandeja para registrar las barras de nuevo");
                    _ = WaitForExplorerAsync(token);
                }
            };
            if (explorer.HasExited)
                _ = WaitForExplorerAsync(token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("No se pudo vigilar el Explorador", ex);
        }
    }

    private static async Task WaitForExplorerAsync(CancellationToken token)
    {
        try
        {
            Process? explorer;
            while ((explorer = ExplorerTaskbarProcess()) is null)
                await Task.Delay(1000, token);
            // El Explorador termina de iniciar su barra después de crear la ventana.
            await Task.Delay(3000, token);
            Watch(explorer, token);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!token.IsCancellationRequested)
                    Restarted?.Invoke();
            });
        }
        catch (TaskCanceledException)
        {
        }
    }

    /// <summary>Proceso del Explorador dueño de una <c>Shell_TrayWnd</c> (Strata UI tiene otra propia).</summary>
    private static Process? ExplorerTaskbarProcess()
    {
        uint found = 0;
        EnumWindows((hwnd, _) =>
        {
            var cls = new StringBuilder(32);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() != "Shell_TrayWnd")
                return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId)
                return true;
            found = pid;
            return false;
        }, IntPtr.Zero);
        if (found == 0)
            return null;
        try
        {
            var process = Process.GetProcessById((int)found);
            if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                return process;
            process.Dispose();
        }
        catch (ArgumentException)
        {
        }
        return null;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
