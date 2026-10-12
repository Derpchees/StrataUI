using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Winx.Shell.Services;

namespace Winx.Shell.Tweaks;

/// <summary>
/// Guarda el valor *original* de cada registro la primera vez que WINX lo modifica,
/// para poder revertir todo. Archivo: %LOCALAPPDATA%\WINX\tweaks-backup.json.
/// </summary>
public sealed class TweakBackup
{
    public sealed record Entry(Hive Hive, string Path, string Name, bool Existed,
                               RegistryValueKind Kind, string? Data, string? OwnerKey);

    public sealed class Document
    {
        public Dictionary<string, Entry> Values { get; set; } = new();
        public List<string> RemovedApps { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WINX", "tweaks-backup.json");

    public string FilePath { get; }
    public Document Data { get; private set; }

    public TweakBackup(string? path = null)
    {
        FilePath = path ?? DefaultPath;
        Data = Load();
    }

    public void Reload() => Data = Load();

    public void SaveOriginals(IEnumerable<RegSetting> settings)
    {
        bool changed = false;
        foreach (var s in settings)
        {
            if (Data.Values.ContainsKey(s.Key))
                continue;
            var current = RegistryStore.Read(s.Hive, s.Path, s.Name);
            Data.Values[s.Key] = new Entry(s.Hive, s.Path, s.Name, current != null,
                current?.Kind ?? RegistryValueKind.Unknown, current is { } c ? Encode(c.Kind, c.Data) : null, s.OwnerKey);
            changed = true;
        }
        if (changed)
            Save();
    }

    public void AddRemovedApps(IEnumerable<string> names)
    {
        foreach (var name in names.Where(n => !Data.RemovedApps.Contains(n)))
            Data.RemovedApps.Add(name);
        Save();
    }

    /// <summary>Quedan valores que solo un administrador puede restaurar (HKLM, directivas).</summary>
    public bool HasAdminValues => Data.Values.Values.Any(NeedsAdmin);

    private static bool NeedsAdmin(Entry entry) => RegSetting.NeedsAdminFor(entry.Hive, entry.Path);

    /// <summary>
    /// Restaura los valores que pide el permiso (los de administrador o los del usuario); los que
    /// fallan quedan para reintentar.
    /// </summary>
    public List<string> Restore(bool admin)
    {
        var failed = new List<string>();
        foreach (var (key, entry) in Data.Values.Where(v => NeedsAdmin(v.Value) == admin).ToList())
        {
            try
            {
                if (entry.Existed && entry.Data != null)
                {
                    RegistryStore.Write(entry.Hive, entry.Path, entry.Name, entry.Kind, Decode(entry.Kind, entry.Data));
                }
                else
                {
                    RegistryStore.DeleteValue(entry.Hive, entry.Path, entry.Name);
                    if (entry.OwnerKey != null)
                        RegistryStore.DeleteTree(entry.Hive, entry.OwnerKey);
                }
                Data.Values.Remove(key);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                failed.Add(key);
            }
        }
        Save();
        return failed;
    }

    private static string Encode(RegistryValueKind kind, object data) => kind switch
    {
        RegistryValueKind.Binary => Convert.ToHexString((byte[])data),
        RegistryValueKind.MultiString => string.Join('\0', (string[])data),
        _ => Convert.ToString(data, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private static object Decode(RegistryValueKind kind, string data) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)long.Parse(data, System.Globalization.CultureInfo.InvariantCulture)),
        RegistryValueKind.QWord => long.Parse(data, System.Globalization.CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromHexString(data),
        RegistryValueKind.MultiString => data.Split('\0'),
        _ => data,
    };

    private Document Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error("Respaldo de ajustes ilegible", ex);
        }
        return new();
    }

    private void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
