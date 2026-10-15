using System.Runtime.InteropServices;
using ManagedShell.WindowsTray;
using Microsoft.Win32;

namespace Strata.Shell.Services;

/// <summary>
/// Persistencia de qué íconos de la bandeja están siempre visibles.
/// La primera vez que Strata UI ve un ícono respeta la configuración de Windows 11
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

    // ── Orden ───────────────────────────────────────────────────────────────

    private static readonly List<System.Windows.Data.ListCollectionView> HiddenViews = new();

    /// <summary>
    /// Íconos ocultos en el orden elegido por el usuario (ManagedShell solo ordena los anclados).
    /// </summary>
    public static System.ComponentModel.ICollectionView CreateHiddenView(NotificationArea tray)
    {
        var view = new System.Windows.Data.ListCollectionView(tray.TrayIcons)
        {
            Filter = item => item is NotifyIcon { IsPinned: false, IsHidden: false },
            CustomSort = Comparer<NotifyIcon>.Create((a, b) => HiddenIndex(a).CompareTo(HiddenIndex(b))),
            IsLiveFiltering = true,
        };
        view.LiveFilteringProperties.Add(nameof(NotifyIcon.IsPinned));
        view.LiveFilteringProperties.Add(nameof(NotifyIcon.IsHidden));
        HiddenViews.Add(view);
        return view;
    }

    public static void ReleaseHiddenView(System.ComponentModel.ICollectionView view) =>
        HiddenViews.Remove((System.Windows.Data.ListCollectionView)view);

    /// <summary>Posición en el orden guardado; los nuevos van al final, en el orden en que llegaron.</summary>
    private static int HiddenIndex(NotifyIcon icon)
    {
        string key = Key(icon);
        int index = key.Length > 0 ? Settings.Current.HiddenTrayOrder.FindIndex(id => Matches(id, key)) : -1;
        return index >= 0 ? index : 10_000 + (_tray?.TrayIcons.IndexOf(icon) ?? 0);
    }

    /// <summary>Íconos ocultos en el orden en que se muestran.</summary>
    public static List<NotifyIcon> HiddenIcons() =>
        _tray?.TrayIcons.Where(i => !i.IsPinned && !i.IsHidden).OrderBy(HiddenIndex).ToList() ?? [];

    /// <summary>
    /// Coloca un ícono entre los ocultos en esa posición (desanclándolo si hacía falta).
    /// <paramref name="save"/> en false durante un arrastre: se guarda al soltar.
    /// </summary>
    public static void MoveToHidden(NotifyIcon icon, int index, bool save = true)
    {
        if (icon.IsPinned)
            icon.Unpin();
        var order = HiddenIcons().Where(i => !ReferenceEquals(i, icon)).ToList();
        order.Insert(Math.Clamp(index, 0, order.Count), icon);
        Settings.Current.HiddenTrayOrder = order.Select(Key).Where(id => id.Length > 0).ToList();
        foreach (var view in HiddenViews)
            view.Refresh();
        if (save)
            SaveOrder();
    }

    /// <summary>Ancla un ícono en esa posición de la barra (o lo mueve si ya estaba anclado).</summary>
    public static void MoveToPinned(NotifyIcon icon, int index, bool save = true)
    {
        icon.Pin(index);
        if (save)
            SaveOrder();
    }

    /// <summary>Guarda anclados y orden de los ocultos (al terminar un arrastre).</summary>
    public static void SaveOrder() => SavePinned();

    private static void OnIconSeen(NotifyIcon icon, HashSet<string> promoted)
    {
        var known = Settings.Current.KnownTrayIcons;
        string key = Key(icon);
        if (key.Length == 0 || known.Any(id => Matches(id, key)))
            return;

        known.Add(key);
        if (!icon.IsPinned && !string.IsNullOrEmpty(icon.Path) && promoted.Contains(icon.Path))
            icon.Pin();
        SavePinned();
    }

    /// <summary>
    /// Clave que no cambia mientras el ícono exista. <see cref="NotifyIcon.Identifier"/> no sirve:
    /// sin GUID termina con el texto emergente ("Thunderbird · 1 mensaje no leído"), y al cambiar
    /// ese texto el ícono parecía nuevo (perdía su lugar y volvía a anclarse si Windows lo promovía).
    /// </summary>
    internal static string Key(NotifyIcon icon) =>
        icon.GUID != default ? icon.GUID.ToString()
        : string.IsNullOrEmpty(icon.Path) ? ""
        : icon.Path + ":" + icon.UID;

    /// <summary>Si una clave guardada es de ese ícono (las viejas llevan además ":texto").</summary>
    internal static bool Matches(string stored, string key) =>
        stored.Equals(key, StringComparison.OrdinalIgnoreCase)
        || stored.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase);

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
