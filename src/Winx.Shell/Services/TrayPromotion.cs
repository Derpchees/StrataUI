using System.Runtime.InteropServices;
using ManagedShell.WindowsTray;
using Microsoft.Win32;

namespace Winx.Shell.Services;

/// <summary>
/// Respeta la configuración de Windows 11 "Otros iconos de la bandeja del sistema":
/// los íconos que el usuario promovió a la barra se anclan también en WINX.
/// </summary>
public static class TrayPromotion
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    public static void Attach(NotificationArea tray)
    {
        var promoted = LoadPromotedPaths();
        foreach (var icon in tray.TrayIcons)
            PinIfPromoted(icon, promoted);
        tray.TrayIcons.CollectionChanged += (_, args) =>
        {
            foreach (var icon in args.NewItems?.OfType<NotifyIcon>() ?? [])
                PinIfPromoted(icon, promoted);
        };
    }

    private static void PinIfPromoted(NotifyIcon icon, HashSet<string> promoted)
    {
        if (!icon.IsPinned && !string.IsNullOrEmpty(icon.Path) && promoted.Contains(icon.Path))
            icon.Pin();
    }

    private static HashSet<string> LoadPromotedPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (root is null)
            return paths;

        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("IsPromoted") is 1 && key.GetValue("ExecutablePath") is string path)
                paths.Add(ResolveKnownFolder(path));
        }
        return paths;
    }

    /// <summary>Windows guarda rutas como "{GUID-de-carpeta-conocida}\App\app.exe".</summary>
    private static string ResolveKnownFolder(string path)
    {
        int end = path.IndexOf('}');
        if (path.StartsWith('{') && end > 0
            && Guid.TryParse(path[..(end + 1)], out var folderId)
            && SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out var folder) == 0)
        {
            return folder + path[(end + 1)..];
        }
        return path;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out string path);
}
