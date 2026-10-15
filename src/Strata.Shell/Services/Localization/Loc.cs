using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Markup;

namespace Strata.Shell.Services.Localization;

/// <summary>
/// Traducción de la interfaz al estilo gettext: el texto en español del código es la clave.
/// En español se muestra tal cual; en otro idioma se busca en <c>Localization/{idioma}.json</c>
/// (recurso incrustado, { "texto en español": "traducción" }) o en un archivo propio en
/// <c>%LOCALAPPDATA%\StrataUI\Languages\{idioma}.json</c> (traducciones de la comunidad, que
/// también completan o corrigen las incluidas). Lo que le falte a un idioma se muestra en inglés.
/// El idioma sale de la interfaz de Windows, salvo que el usuario elija otro en el Hub; cambiarlo
/// requiere reiniciar Strata UI (los textos se resuelven al crear cada ventana).
/// </summary>
public static class Loc
{
    private const string ResourcePrefix = "Strata.Shell.Localization.";

    /// <summary>Carpeta de idiomas propios (un "xx.json" por idioma).</summary>
    public static string ExternalFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "Languages");

    /// <summary>Idiomas con traducción (además del español, que es el del código): incluidos y propios.</summary>
    public static IReadOnlyList<string> Translated { get; } = Discover();

    private static Dictionary<string, string>? _table;

    /// <summary>Idioma en uso: "es", "en"…</summary>
    public static string Language { get; private set; } = "es";

    private static IReadOnlyList<string> Discover()
    {
        var codes = typeof(Loc).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^".json".Length])
            .ToList();
        try
        {
            if (Directory.Exists(ExternalFolder))
                codes.AddRange(Directory.EnumerateFiles(ExternalFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return codes.Where(IsLanguageCode).Select(c => c.ToLowerInvariant()).Where(c => c != "es").Distinct().Order().ToList();
    }

    /// <summary>"pt", "zh-Hans"…: algo que .NET reconozca como idioma (no cualquier archivo .json).</summary>
    private static bool IsLanguageCode(string code)
    {
        try
        {
            return code.Length is >= 2 and <= 12 && !CultureInfo.GetCultureInfo(code).EnglishName.StartsWith("Unknown", StringComparison.Ordinal);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Nombre del idioma en ese idioma ("Português", "Deutsch").</summary>
    public static string NativeName(string code)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(code).NativeName;
            return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.GetCultureInfo(code)) + name[1..] : code;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    /// <summary>Elige el idioma: el de la configuración o, con "auto", el de la interfaz de Windows.</summary>
    public static void Initialize(string? setting)
    {
        Language = Resolve(setting, CultureInfo.CurrentUICulture);
        if (Language == "es")
        {
            _table = null;
            return;
        }
        // Base en inglés y encima el idioma elegido: lo que le falte se ve en inglés, no en español.
        _table = Load("en");
        if (Language != "en")
            foreach (var (key, value) in Load(Language))
                _table[key] = value;
        // Fechas, horas y números siguen la región de Windows; solo se cambian los textos.
    }

    /// <summary>"es" para cualquier español; un idioma traducido si lo hay; si no, inglés.</summary>
    internal static string Resolve(string? setting, CultureInfo windowsUi)
    {
        string wanted = string.IsNullOrEmpty(setting) || setting == "auto" ? windowsUi.TwoLetterISOLanguageName : setting;
        return wanted == "es" || Translated.Contains(wanted) ? wanted : "en";
    }

    /// <summary>El idioma incluido y, encima, el archivo propio de la carpeta de idiomas (si hay).</summary>
    internal static Dictionary<string, string> Load(string language)
    {
        var table = new Dictionary<string, string>();
        using (var stream = typeof(Loc).Assembly.GetManifestResourceStream($"{ResourcePrefix}{language}.json"))
        {
            if (stream != null)
                Merge(table, JsonSerializer.Deserialize<Dictionary<string, string>>(stream));
        }
        var external = Path.Combine(ExternalFolder, language + ".json");
        try
        {
            if (File.Exists(external))
                Merge(table, JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(external)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error($"No se pudo leer el idioma {external}", ex);
        }
        return table;
    }

    private static void Merge(Dictionary<string, string> into, Dictionary<string, string>? from)
    {
        if (from is null)
            return;
        foreach (var (key, value) in from)
        {
            if (!string.IsNullOrEmpty(value))
                into[key] = value;
        }
    }

    /// <summary>Texto traducido (o el original si falta la traducción).</summary>
    public static string T(string spanish) =>
        _table != null && _table.TryGetValue(spanish, out var translated) && translated.Length > 0 ? translated : spanish;

    /// <summary>Texto con huecos {0}, {1}…: se traduce el formato y luego se rellena.</summary>
    public static string F(string spanishFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(spanishFormat), args);
}

/// <summary>Uso en XAML: <c>Text="{l:T 'Mostrar fecha'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; set; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
