using System.Runtime.InteropServices;
using ManagedShell.WindowsTray;
using Microsoft.Win32;

namespace Winx.Shell.Services;

/// <summary>
/// Persistencia de qué íconos de la bandeja están siempre visibles.
/// La primera vez que WINX ve un ícono respeta la configuración de Windows 11
/// ("Otros iconos de la bandeja del sistema"); a partir de ahí manda lo que elija el usuario.
/// </summary>
public static class TrayPromotion
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    private static NotificationArea? _tray;

    public static void Attach(NotificationArea tray)
    {
        _tray = tray;
        var promoted = LoadPromotedPaths();
        foreach (var icon in tray.TrayIcons)
            OnIconSeen(icon, promoted);
        tray.TrayIcons.CollectionChanged += (_, args) =>
        {
            foreach (var icon in args.NewItems?.OfType<NotifyIcon>() ?? [])
                OnIconSeen(icon, promoted);
        };
    }

    /// <summary>Ancla o desancla un ícono y guarda la elección.</summary>
    public static void SetPinned(NotifyIcon icon, bool pinned)
    {
        if (pinned == icon.IsPinned)
            return;
        if (pinned)
            icon.Pin();
        else
            icon.Unpin();
        SavePinned();
    }

    private static void OnIconSeen(NotifyIcon icon, HashSet<string> promoted)
    {
        var known = Settings.Current.KnownTrayIcons;
        if (string.IsNullOrEmpty(icon.Identifier) || known.Contains(icon.Identifier))
            return;

        known.Add(icon.Identifier);
        if (!icon.IsPinned && !string.IsNullOrEmpty(icon.Path) && promoted.Contains(icon.Path))
            icon.Pin();
        SavePinned();
    }

    private static void SavePinned()
    {
        if (_tray is null)
            return;
        Settings.Current.PinnedTrayIcons = _tray.PinnedNotifyIcons;
        Settings.Save();
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
