using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Services.StartMenu;

/// <summary>Carpeta de apps ancladas tal como se muestra en el menú.</summary>
public sealed class PinFolderView(PinEntry entry, IReadOnlyList<StartApp> apps)
{
    public PinEntry Entry { get; } = entry;
    public string Id => Entry.Id;
    public string Name => Entry.FolderName ?? Loc.T("Carpeta");
    public IReadOnlyList<StartApp> Apps { get; } = apps;
    /// <summary>Hasta cuatro íconos para la miniatura 2×2 de la carpeta.</summary>
    public IReadOnlyList<ImageSource?> Preview => Apps.Take(4).Select(a => a.Icon).ToList();
}

/// <summary>Hueco entre anclados: ocupa una celda vacía.</summary>
public sealed class PinGapView(PinEntry entry)
{
    public string Id => entry.Id;
}

/// <summary>Sección de anclados tal como se muestra en el menú.</summary>
public sealed class PinSectionView(PinSection section, IEnumerable<object> tiles) : INotifyPropertyChanged
{
    public PinSection Section { get; } = section;
    public string Id => Section.Id;
    public string Name => Section.Name;
    /// <summary><see cref="StartApp"/>, <see cref="PinFolderView"/> o <see cref="PinGapView"/>, en orden.</summary>
    public ObservableCollection<object> Tiles { get; } = new(tiles);
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
}

/// <summary>
/// Organización de los anclados en secciones y carpetas (Settings.StartLayout). Todas las
/// operaciones guardan la configuración y reconstruyen las vistas.
/// </summary>
public static class PinLayout
{
    public static ObservableCollection<PinSectionView> Sections { get; } = new();

    /// <summary>Cambió la organización (para refrescar las vistas abiertas).</summary>
    public static event Action? Changed;

    /// <summary>
    /// Diseño actual. Solo lectura no lo crea (si no, consultar "¿está anclada?" antes de migrar
    /// dejaba una lista vacía y la migración de los anclados antiguos nunca ocurría).
    /// </summary>
    private static List<PinSection> Model => Settings.Current.StartLayout ?? Empty;
    private static readonly List<PinSection> Empty = new();

    private static List<PinSection> Editable => Settings.Current.StartLayout ??= new();

    /// <summary>Primera ejecución o migración desde la lista plana anterior.</summary>
    internal static void EnsureLayout(Func<List<string>> defaultPins)
    {
        // Una lista vacía con anclados antiguos pendientes es la consecuencia del error anterior.
        bool pendingMigration = Settings.Current.StartLayout is { Count: 0 } && Settings.Current.PinnedStartApps != null;
        if (Settings.Current.StartLayout != null && !pendingMigration)
            return;
        var ids = Settings.Current.PinnedStartApps ?? defaultPins();
        Settings.Current.StartLayout =
        [
            new PinSection { Name = Loc.T("Anclado"), Items = ids.Select(id => new PinEntry { AppId = id }).ToList() },
        ];
        Settings.Current.PinnedStartApps = null;
        Settings.Save();
    }

    /// <summary>Reconstruye las vistas a partir del modelo y del catálogo de apps.</summary>
    internal static void RebuildViews()
    {
        var byId = StartMenuService.Apps.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        StartApp? Find(string? id) => id != null && byId.TryGetValue(id, out var app) ? app : null;

        Sections.Clear();
        foreach (var section in Model)
        {
            var tiles = new List<object>();
            foreach (var entry in section.Items)
            {
                if (entry.IsGap)
                    tiles.Add(new PinGapView(entry));
                else if (entry.IsFolder)
                {
                    var apps = entry.FolderApps!.Select(Find).Where(a => a != null).Select(a => a!).ToList();
                    if (apps.Count > 0)
                        tiles.Add(new PinFolderView(entry, apps));
                }
                else if (Find(entry.AppId) is { } app)
                {
                    tiles.Add(app);
                }
            }
            Sections.Add(new PinSectionView(section, tiles));
        }

        // Lista plana (para los estilos sin secciones y para saber qué está anclado).
        StartMenuService.Pinned.Clear();
        foreach (var id in AllAppIds())
            if (Find(id) is { } app)
                StartMenuService.Pinned.Add(app);
    }

    public static IEnumerable<string> AllAppIds() =>
        Model.SelectMany(s => s.Items).SelectMany(e => e.IsFolder ? e.FolderApps! : [e.AppId!]).Where(id => id != null);

    public static bool Contains(string appId) => AllAppIds().Contains(appId);

    // ── Operaciones ─────────────────────────────────────────────────────────

    public static void Pin(string appId)
    {
        if (Contains(appId))
            return;
        if (Editable.Count == 0)
            Editable.Add(new PinSection());
        Editable[0].Items.Add(new PinEntry { AppId = appId });
        Commit();
    }

    public static void Unpin(string appId)
    {
        foreach (var section in Model)
        {
            section.Items.RemoveAll(e => e.AppId == appId);
            foreach (var folder in section.Items.Where(e => e.IsFolder).ToList())
            {
                folder.FolderApps!.Remove(appId);
                NormalizeFolder(section, folder);
            }
        }
        Commit();
    }

    /// <summary>Mueve un elemento a otra posición (de la misma u otra sección).</summary>
    public static void Move(string entryId, string sectionId, int index)
    {
        if (Detach(entryId) is not { } entry || FindSection(sectionId) is not { } target)
            return;
        target.Items.Insert(Math.Clamp(index, 0, target.Items.Count), entry);
        Commit();
    }

    /// <summary>
    /// Suelta un elemento sobre otro: si el destino es una app se crea una carpeta con ambas;
    /// si es una carpeta, la app entra en ella. Una carpeta no se mete dentro de otra.
    /// </summary>
    public static void Merge(string draggedEntryId, string targetEntryId)
    {
        if (draggedEntryId == targetEntryId)
            return;
        var dragged = FindEntry(draggedEntryId);
        var (section, target) = FindEntryWithSection(targetEntryId);
        if (dragged is null || target is null || section is null || dragged.IsFolder || dragged.IsGap || target.IsGap)
            return;

        Detach(draggedEntryId);
        if (target.IsFolder)
        {
            target.FolderApps!.Add(dragged.AppId!);
        }
        else
        {
            int index = section.Items.IndexOf(target);
            section.Items[index] = new PinEntry
            {
                FolderName = Loc.T("Carpeta"),
                FolderApps = [target.AppId!, dragged.AppId!],
            };
        }
        Commit();
    }

    /// <summary>Saca una app de una carpeta y la deja en la sección indicada. Devuelve el ID del nuevo elemento.</summary>
    public static string? TakeOutOfFolder(string folderId, string appId, string sectionId, int index)
    {
        var (folderSection, folder) = FindEntryWithSection(folderId);
        if (folder?.FolderApps is null || folderSection is null || FindSection(sectionId) is not { } target)
            return null;
        folder.FolderApps.Remove(appId);
        var entry = new PinEntry { AppId = appId };
        target.Items.Insert(Math.Clamp(index, 0, target.Items.Count), entry);
        NormalizeFolder(folderSection, folder);
        Commit();
        return entry.Id;
    }

    /// <summary>Deja un espacio vacío antes o después de un elemento.</summary>
    public static void InsertGap(string entryId, bool after)
    {
        var (section, entry) = FindEntryWithSection(entryId);
        if (section is null || entry is null)
            return;
        section.Items.Insert(section.Items.IndexOf(entry) + (after ? 1 : 0), new PinEntry());
        Commit();
    }

    public static void RemoveGap(string gapId)
    {
        if (FindEntry(gapId) is { IsGap: true })
        {
            Detach(gapId);
            Commit();
        }
    }

    /// <summary>
    /// Suelta sobre un hueco: el elemento ocupa esa celda y el hueco pasa a donde estaba (así se
    /// puede colocar una app en cualquier celda sin correr las demás). Una app que sale de una
    /// carpeta simplemente ocupa el hueco.
    /// </summary>
    public static void DropOnGap(string gapId, string? entryId, string? fromFolderId = null, string? appId = null)
    {
        var (gapSection, gap) = FindEntryWithSection(gapId);
        if (gapSection is null || gap is not { IsGap: true } || gapId == entryId)
            return;
        int gapIndex = gapSection.Items.IndexOf(gap);

        if (entryId != null)
        {
            var (section, entry) = FindEntryWithSection(entryId);
            if (section is null || entry is null)
                return;
            int index = section.Items.IndexOf(entry);
            section.Items[index] = gap;
            gapSection.Items[gapIndex] = entry;
        }
        else if (fromFolderId != null && appId != null)
        {
            var (folderSection, folder) = FindEntryWithSection(fromFolderId);
            if (folderSection is null || folder?.FolderApps is null)
                return;
            folder.FolderApps.Remove(appId);
            gapSection.Items[gapIndex] = new PinEntry { AppId = appId };
            NormalizeFolder(folderSection, folder);
        }
        Commit();
    }

    public static void RenameFolder(string folderId, string name)
    {
        if (FindEntry(folderId) is { IsFolder: true } folder && !string.IsNullOrWhiteSpace(name))
        {
            folder.FolderName = name.Trim();
            Commit();
        }
    }

    /// <summary>Deshace una carpeta: sus apps quedan sueltas en el mismo lugar.</summary>
    public static void Ungroup(string folderId)
    {
        var (section, folder) = FindEntryWithSection(folderId);
        if (section is null || folder?.FolderApps is null)
            return;
        int index = section.Items.IndexOf(folder);
        section.Items.RemoveAt(index);
        section.Items.InsertRange(index, folder.FolderApps.Select(id => new PinEntry { AppId = id }));
        Commit();
    }

    public static void AddSection(string name, string? afterSectionId = null)
    {
        int index = afterSectionId is null ? Editable.Count : Editable.FindIndex(s => s.Id == afterSectionId) + 1;
        Editable.Insert(Math.Clamp(index, 0, Editable.Count), new PinSection { Name = string.IsNullOrWhiteSpace(name) ? Loc.T("Sección") : name.Trim() });
        Commit();
    }

    public static void RenameSection(string sectionId, string name)
    {
        if (FindSection(sectionId) is { } section && !string.IsNullOrWhiteSpace(name))
        {
            section.Name = name.Trim();
            Commit();
        }
    }

    /// <summary>Elimina una sección; sus elementos pasan a la sección anterior (o a la primera).</summary>
    public static void DeleteSection(string sectionId)
    {
        int index = Model.FindIndex(s => s.Id == sectionId);
        if (index < 0 || Model.Count == 1)
            return;
        var removed = Model[index];
        Model.RemoveAt(index);
        Model[Math.Max(0, index - 1)].Items.AddRange(removed.Items);
        Commit();
    }

    // ── Auxiliares ──────────────────────────────────────────────────────────

    private static void Commit()
    {
        Settings.Save();
        RebuildViews();
        Changed?.Invoke();
    }

    /// <summary>Una carpeta con una sola app vuelve a ser una app suelta; vacía, desaparece.</summary>
    private static void NormalizeFolder(PinSection section, PinEntry folder)
    {
        int index = section.Items.IndexOf(folder);
        if (index < 0 || folder.FolderApps is null)
            return;
        if (folder.FolderApps.Count == 0)
            section.Items.RemoveAt(index);
        else if (folder.FolderApps.Count == 1)
            section.Items[index] = new PinEntry { AppId = folder.FolderApps[0] };
    }

    private static PinEntry? Detach(string entryId)
    {
        foreach (var section in Model)
        {
            int index = section.Items.FindIndex(e => e.Id == entryId);
            if (index >= 0)
            {
                var entry = section.Items[index];
                section.Items.RemoveAt(index);
                return entry;
            }
        }
        return null;
    }

    private static PinSection? FindSection(string id) => Model.FirstOrDefault(s => s.Id == id);

    private static PinEntry? FindEntry(string id) => FindEntryWithSection(id).Entry;

    private static (PinSection? Section, PinEntry? Entry) FindEntryWithSection(string id)
    {
        foreach (var section in Model)
            if (section.Items.FirstOrDefault(e => e.Id == id) is { } entry)
                return (section, entry);
        return (null, null);
    }

    /// <summary>ID del elemento que corresponde a una app suelta en el diseño (para arrastrarla).</summary>
    public static string? EntryIdOf(string appId) =>
        Model.SelectMany(s => s.Items).FirstOrDefault(e => e.AppId == appId)?.Id;

    /// <summary>Posición de un elemento dentro de su sección.</summary>
    public static (string SectionId, int Index)? PositionOf(string entryId)
    {
        foreach (var section in Model)
        {
            int index = section.Items.FindIndex(e => e.Id == entryId);
            if (index >= 0)
                return (section.Id, index);
        }
        return null;
    }
}
