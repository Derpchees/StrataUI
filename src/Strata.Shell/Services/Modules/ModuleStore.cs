using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Modules;

/// <summary>
/// Módulos importados: un JSON por módulo en %LOCALAPPDATA%\StrataUI\Modules\&lt;tipo&gt;. Importar
/// valida y copia el archivo (el mismo nombre lo reemplaza); quitar lo borra. Se leen una vez y se
/// vuelven a leer al importar, quitar o abrir su sección del Hub (por si se editaron a mano).
/// </summary>
public static class ModuleStore
{
    /// <summary>Más grande que esto no es un módulo (ni el más detallado pasa de unos pocos KB).</summary>
    private const int MaxBytes = 256 * 1024;

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "Modules");

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly Dictionary<ModuleKind, IReadOnlyList<ModuleBase>> Cache = new();

    /// <summary>Se importó, quitó o releyó algo de ese tipo.</summary>
    public static event Action<ModuleKind>? Changed;

    public static string Folder(ModuleKind kind) => Path.Combine(Root, kind switch
    {
        ModuleKind.Theme => "Themes",
        ModuleKind.Animation => "Animations",
        ModuleKind.Widget => "Widgets",
        _ => "StartMenus",
    });

    /// <summary>Valor de "type" en el archivo.</summary>
    internal static string TypeName(ModuleKind kind) => kind switch
    {
        ModuleKind.Theme => "theme",
        ModuleKind.Animation => "animation",
        ModuleKind.Widget => "widget",
        _ => "startmenu",
    };

    private static Type ClassOf(ModuleKind kind) => kind switch
    {
        ModuleKind.Theme => typeof(ThemeModule),
        ModuleKind.Animation => typeof(AnimationModule),
        ModuleKind.Widget => typeof(WidgetModule),
        _ => typeof(StartMenuModule),
    };

    public static IReadOnlyList<T> All<T>(ModuleKind kind) where T : ModuleBase => List(kind).OfType<T>().ToList();

    public static T? Get<T>(ModuleKind kind, string? id) where T : ModuleBase =>
        id is null ? null : List(kind).OfType<T>().FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ModuleBase> List(ModuleKind kind)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(kind, out var list))
                Cache[kind] = list = Read(kind);
            return list;
        }
    }

    /// <summary>Vuelve a leer la carpeta (al abrir la sección del Hub) y avisa si cambió algo.</summary>
    public static void Reload(ModuleKind kind)
    {
        var fresh = Read(kind);
        bool changed;
        lock (Cache)
        {
            changed = !Cache.TryGetValue(kind, out var old) || !Same(old, fresh);
            Cache[kind] = fresh;
        }
        if (changed)
            Changed?.Invoke(kind);
    }

    private static bool Same(IReadOnlyList<ModuleBase> a, IReadOnlyList<ModuleBase> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Id == p.Second.Id
            && JsonSerializer.Serialize(p.First, p.First.GetType(), Json) == JsonSerializer.Serialize(p.Second, p.Second.GetType(), Json));

    private static IReadOnlyList<ModuleBase> Read(ModuleKind kind)
    {
        var list = new List<ModuleBase>();
        try
        {
            var folder = Folder(kind);
            if (!Directory.Exists(folder))
                return list;
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                var (module, error) = Parse(kind, file);
                if (module != null)
                    list.Add(module);
                else
                    Log.Info($"Módulo ignorado {file}: {error}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"No se pudieron leer los módulos de {kind}", ex);
        }
        return list;
    }

    /// <summary>Lee y valida un archivo; el módulo o el motivo por el que no sirve.</summary>
    internal static (ModuleBase? Module, string? Error) Parse(ModuleKind kind, string file)
    {
        try
        {
            if (new FileInfo(file).Length > MaxBytes)
                return (null, Loc.T("El archivo es demasiado grande."));
            var (module, error) = ParseText(kind, File.ReadAllText(file));
            if (module != null)
                module.Id = Path.GetFileNameWithoutExtension(file);
            return (module, error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    internal static (ModuleBase? Module, string? Error) ParseText(ModuleKind kind, string text)
    {
        ModuleBase? module;
        try
        {
            module = (ModuleBase?)JsonSerializer.Deserialize(text, ClassOf(kind), Json);
        }
        catch (JsonException)
        {
            return (null, Loc.T("No es un archivo JSON válido para este tipo de módulo."));
        }
        if (module is null)
            return (null, Loc.T("No es un archivo JSON válido para este tipo de módulo."));
        if (!string.Equals(module.Type, TypeName(kind), StringComparison.OrdinalIgnoreCase))
            return (null, Loc.F("Es un módulo de otro tipo (se esperaba \"type\": \"{0}\").", TypeName(kind)));
        return module.Validate() is { } error ? (null, error) : (module, null);
    }

    /// <summary>
    /// Pide uno o varios archivos y los importa. Devuelve los nombres importados y los errores
    /// ("archivo: motivo") para mostrarlos.
    /// </summary>
    public static async Task<(List<ModuleBase> Imported, List<string> Errors)> ImportAsync(ModuleKind kind)
    {
        var files = await FilePicker.PickFilesAsync(Loc.T("Importar módulo"), Loc.T("Módulos de Strata UI (*.json)") + "|*.json", multiselect: true);
        return Import(kind, files);
    }

    internal static (List<ModuleBase> Imported, List<string> Errors) Import(ModuleKind kind, IEnumerable<string> files)
    {
        var imported = new List<ModuleBase>();
        var errors = new List<string>();
        foreach (var file in files)
        {
            var (module, error) = Parse(kind, file);
            if (module is null)
            {
                errors.Add($"{Path.GetFileName(file)}: {error}");
                continue;
            }
            try
            {
                Directory.CreateDirectory(Folder(kind));
                // Se guarda tal como vino (con sus comentarios) bajo un nombre limpio: el mismo nombre lo actualiza.
                module.Id = FileNameFor(module.Name);
                var target = Path.Combine(Folder(kind), module.Id + ".json");
                if (!Path.GetFullPath(file).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    File.Copy(file, target, overwrite: true);
                imported.Add(module);
                Log.Info($"Módulo importado ({kind}): {module.Name} ← {file}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        if (imported.Count > 0)
            Reload(kind);
        return (imported, errors);
    }

    /// <summary>Guarda un módulo creado por Strata UI (p. ej. el menú Inicio actual) y devuelve su ruta.</summary>
    internal static string Save(ModuleKind kind, ModuleBase module)
    {
        Directory.CreateDirectory(Folder(kind));
        module.Type = TypeName(kind);
        module.Id = FileNameFor(module.Name);
        var path = Path.Combine(Folder(kind), module.Id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(module, module.GetType(), Json), new UTF8Encoding(false));
        Reload(kind);
        return path;
    }

    public static void Delete(ModuleKind kind, string id)
    {
        try
        {
            var path = Path.Combine(Folder(kind), FileNameFor(id) + ".json");
            if (File.Exists(path))
                File.Delete(path);
            Log.Info($"Módulo quitado ({kind}): {id}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"No se pudo quitar el módulo {id}", ex);
        }
        Reload(kind);
    }

    /// <summary>Nombre de archivo a partir del nombre del módulo (sin caracteres inválidos ni rutas).</summary>
    internal static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) || c == '.' ? '-' : c).ToArray()).Trim(' ', '-');
        return clean.Length == 0 ? "module" : clean.Length > 60 ? clean[..60] : clean;
    }

    /// <summary>Abre la carpeta del tipo (la crea si hace falta).</summary>
    public static void OpenFolder(ModuleKind kind, string? select = null)
    {
        try
        {
            Directory.CreateDirectory(Folder(kind));
            var args = select != null ? $"/select,\"{select}\"" : $"\"{Folder(kind)}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("No se pudo abrir la carpeta de módulos", ex);
        }
    }
}
