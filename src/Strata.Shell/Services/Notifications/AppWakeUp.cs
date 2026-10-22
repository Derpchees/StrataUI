using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ManagedShell.Common.Helpers;
using ShellNotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Después de pasarle el clic de una notificación a su activador: si la app no quedó al frente es
/// porque vive en la bandeja con la ventana oculta (Thunderbird minimizado a la bandeja recibe el clic
/// pero no se muestra). Entonces se hace lo que haría el usuario: clic en su ícono de la bandeja o,
/// si no tiene, mostrar su ventana.
/// </summary>
internal static class AppWakeUp
{
    /// <summary>Espera un momento a que la app reaccione y, si no se ve, la trae. <paramref name="folder"/> es su carpeta de instalación.</summary>
    public static async Task EnsureShownAsync(string folder, string appName)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        // Thunderbird en la bandeja toma el foco con una ventana oculta: solo cuenta una que se vea.
        var foreground = GetForegroundWindow();
        if (IsWindowVisible(foreground) && !IsIconic(foreground) && IsUnder(ProcessPath(foreground), folder))
            return;
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() => Show(folder, appName));
        }
        catch (Exception ex)
        {
            Log.Error($"Notificación de {appName}: no se pudo mostrar la app", ex);
        }
    }

    /// <summary>Clic en su ícono de la bandeja (lo que haría el usuario) o, si no tiene, mostrar su ventana.</summary>
    private static void Show(string folder, string appName)
    {
        try
        {
            var icon = App.Current.Shell.NotificationArea.TrayIcons.OfType<ShellNotifyIcon>()
                .FirstOrDefault(i => IsUnder(i.Path, folder));
            if (icon != null)
            {
                uint cursor = MouseHelper.GetCursorPositionParam();
                int doubleClick = System.Windows.Forms.SystemInformation.DoubleClickTime;
                icon.IconMouseDown(MouseButton.Left, cursor, doubleClick);
                icon.IconMouseUp(MouseButton.Left, cursor, doubleClick);
                Log.Info($"Notificación de {appName}: la app no se mostró; clic en su ícono de la bandeja");
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // El shell aún no arrancó.
        }
        if (MainWindow(folder) is { } window && window != IntPtr.Zero)
        {
            ShowWindow(window, IsIconic(window) ? SwRestore : SwShow);
            SetForegroundWindow(window);
            Log.Info($"Notificación de {appName}: la app no se mostró; se muestra su ventana");
        }
        else
        {
            Log.Info($"Notificación de {appName}: la app no se mostró y no se encontró su ícono ni su ventana");
        }
    }

    /// <summary>La carpeta de la DLL o del ejecutable de un CLSID registrado (el activador va junto a la app).</summary>
    public static string? FolderOfClsid(Guid clsid)
    {
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            foreach (var server in new[] { "LocalServer32", "InprocServer32" })
            {
                using var key = hive.OpenSubKey($@"Software\Classes\CLSID\{{{clsid}}}\{server}");
                if (key?.GetValue(null) is string command && command.Length > 0)
                {
                    // "C:\…\app.exe" -args, o la ruta sin comillas.
                    string path = command.StartsWith('"') ? command[1..command.IndexOf('"', 1)] : command.Split(" -")[0].Trim();
                    return Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(path));
                }
            }
        }
        return null;
    }

    private static bool IsUnder(string? path, string folder) =>
        !string.IsNullOrEmpty(path)
        && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>La ventana principal de la app (con título y sin dueña), aunque esté oculta o minimizada.</summary>
    private static IntPtr MainWindow(string folder)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (GetWindow(hwnd, GwOwner) != IntPtr.Zero || GetWindowTextLength(hwnd) == 0
                || (GetWindowLong(hwnd, GwlStyle) & WsCaption) != WsCaption || !IsUnder(ProcessPath(hwnd), folder))
                return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static string? ProcessPath(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const int SwShow = 5, SwRestore = 9, GwlStyle = -16;
    private const uint GwOwner = 4, ProcessQueryLimitedInformation = 0x1000;
    private const int WsCaption = 0x00C00000;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}
