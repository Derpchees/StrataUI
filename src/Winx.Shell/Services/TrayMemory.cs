using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ManagedShell.Interop;
using ManagedShell.WindowsTray;

namespace Winx.Shell.Services;

/// <summary>
/// Recuerda los íconos de la bandeja para devolverlos cuando WINX se reinicia. Muchas apps (Spotify,
/// ControlCenter4 de Brother…) registran su ícono una sola vez y no atienden "TaskbarCreated", así
/// que al cerrarse WINX su ícono se perdía para siempre. Al iniciar, WINX se manda a sí mismo el
/// mismo mensaje que mandaría la app por cada ícono cuya ventana siga viva y sea del mismo programa;
/// los clics van a la app como siempre (ventana, número y mensaje de aviso son los de ella).
/// </summary>
public static class TrayMemory
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WINX", "tray-icons.json");

    private static NotificationArea? _tray;
    private static DispatcherTimer? _saveTimer;

    private sealed record Entry(long HWnd, uint UID, uint CallbackMessage, Guid GUID, uint Version,
                                string? Path, string? Title, bool IsHidden, string? IconPng);

    /// <summary>Devuelve los íconos guardados y empieza a seguir los cambios.</summary>
    public static void Attach(NotificationArea tray, IntPtr trayHwnd)
    {
        _tray = tray;
        try
        {
            Restore(trayHwnd);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron devolver los íconos de la bandeja", ex);
        }

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
        foreach (var icon in tray.TrayIcons)
            icon.PropertyChanged += OnIconChanged;
        tray.TrayIcons.CollectionChanged += OnIconsChanged;
    }

    /// <summary>Guarda ya (al salir).</summary>
    public static void Flush()
    {
        if (_saveTimer is null)
            return;
        _saveTimer.Stop();
        Save();
    }

    private static void OnIconsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var icon in e.NewItems?.OfType<NotifyIcon>() ?? [])
            icon.PropertyChanged += OnIconChanged;
        foreach (var icon in e.OldItems?.OfType<NotifyIcon>() ?? [])
            icon.PropertyChanged -= OnIconChanged;
        ScheduleSave();
    }

    private static void OnIconChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotifyIcon.Icon) or nameof(NotifyIcon.Title) or nameof(NotifyIcon.IsHidden))
            ScheduleSave();
    }

    private static void ScheduleSave()
    {
        if (_saveTimer is { IsEnabled: false })
            _saveTimer.Start();
    }

    private static void Save()
    {
        if (_tray is null)
            return;
        try
        {
            var entries = _tray.TrayIcons
                .Where(i => i.HWnd != IntPtr.Zero)
                .Select(i => new Entry(i.HWnd.ToInt64(), i.UID, i.CallbackMessage, i.GUID, i.Version,
                                       i.Path, i.Title, i.IsHidden, ToPng(i.Icon)))
                .ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron guardar los íconos de la bandeja", ex);
        }
    }

    private static void Restore(IntPtr trayHwnd)
    {
        if (trayHwnd == IntPtr.Zero || !File.Exists(FilePath))
            return;
        var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? [];
        int restored = 0;
        foreach (var entry in entries)
        {
            var hwnd = new IntPtr(entry.HWnd);
            // Solo si la ventana sigue viva y es del mismo programa (un identificador puede reutilizarse).
            if (!IsWindow(hwnd) || string.IsNullOrEmpty(entry.Path)
                || !string.Equals(ProcessPath(hwnd), entry.Path, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_tray!.TrayIcons.Any(i => i.HWnd == hwnd && i.UID == entry.UID))
                continue;

            var hIcon = FromPng(entry.IconPng);
            try
            {
                var flags = NativeMethods.NIF.MESSAGE | NativeMethods.NIF.TIP | NativeMethods.NIF.STATE;
                if (hIcon != IntPtr.Zero)
                    flags |= NativeMethods.NIF.ICON;
                if (entry.GUID != Guid.Empty)
                    flags |= NativeMethods.NIF.GUID;
                var nid = new NativeMethods.NOTIFYICONDATA
                {
                    hWnd = (uint)hwnd.ToInt64(),
                    uID = entry.UID,
                    uFlags = flags,
                    uCallbackMessage = entry.CallbackMessage,
                    hIcon = (uint)hIcon.ToInt64(),
                    szTip = entry.Title ?? "",
                    dwState = entry.IsHidden ? 1 : 0,
                    dwStateMask = 1,
                    szInfo = "",
                    szInfoTitle = "",
                    guidItem = entry.GUID,
                };
                if (!Send(trayHwnd, NativeMethods.NIM.NIM_ADD, nid))
                    continue;
                if (entry.Version > 0)
                {
                    nid.uVersion = entry.Version;
                    Send(trayHwnd, NativeMethods.NIM.NIM_SETVERSION, nid);
                }
                restored++;
            }
            finally
            {
                if (hIcon != IntPtr.Zero)
                    DestroyIcon(hIcon);
            }
        }
        if (restored > 0)
            Log.Info($"Bandeja: {restored} íconos devueltos de la sesión anterior de WINX");
    }

    /// <summary>Le pasa el mensaje a la bandeja de WINX igual que lo haría Shell_NotifyIcon.</summary>
    private static bool Send(IntPtr trayHwnd, NativeMethods.NIM message, NativeMethods.NOTIFYICONDATA nid)
    {
        nid.cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>();
        var data = new NativeMethods.SHELLTRAYDATA { dwUnknown = 0x34753423, dwMessage = (uint)message, nid = nid };
        int size = Marshal.SizeOf<NativeMethods.SHELLTRAYDATA>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(data, buffer, false);
            var copy = new CopyData { dwData = 1, cbData = size, lpData = buffer };
            return SendMessage(trayHwnd, WmCopyData, IntPtr.Zero, ref copy) != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? ToPng(ImageSource? image)
    {
        if (image is not BitmapSource bitmap)
            return null;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Un HICON (de WINX) a partir del PNG guardado; ManagedShell lo copia al recibirlo.</summary>
    private static IntPtr FromPng(string? png)
    {
        if (string.IsNullOrEmpty(png))
            return IntPtr.Zero;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(png));
            using var bitmap = new System.Drawing.Bitmap(stream);
            return bitmap.GetHicon();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static string? ProcessPath(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var path = new System.Text.StringBuilder(1024);
            int size = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder path, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private const int WmCopyData = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref CopyData lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
