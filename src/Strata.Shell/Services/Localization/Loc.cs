using System.Globalization;
using System.Text.Json;
using System.Windows.Markup;

namespace Strata.Shell.Services.Localization;

/// <summary>
/// Traducción de la interfaz al estilo gettext: el texto en español del código es la clave.
/// En español se muestra tal cual; en otro idioma se busca en <c>Localization/{idioma}.json</c>
/// (recurso incrustado, { "texto en español": "traducción" }) y, si el idioma no está, en inglés.
/// El idioma sale de la interfaz de Windows, salvo que el usuario elija otro en el Hub; cambiarlo
/// requiere reiniciar Strata UI (los textos se resuelven al crear cada ventana).
/// </summary>
public static class Loc
{
    /// <summary>Idiomas con traducción (además del español, que es el del código).</summary>
    public static readonly string[] Translated = ["en"];

    private static Dictionary<string, string>? _table;

    /// <summary>Idioma en uso: "es", "en"…</summary>
    public static string Language { get; private set; } = "es";

    /// <summary>Elige el idioma: el de la configuración o, con "auto", el de la interfaz de Windows.</summary>
    public static void Initialize(string? setting)
    {
        Language = Resolve(setting, CultureInfo.CurrentUICulture);
        _table = Language == "es" ? null : Load(Language);
        // Fechas, horas y números siguen la región de Windows; solo se cambian los textos.
    }

    /// <summary>"es" para cualquier español; un idioma traducido si lo hay; si no, inglés.</summary>
    internal static string Resolve(string? setting, CultureInfo windowsUi)
    {
        string wanted = string.IsNullOrEmpty(setting) || setting == "auto" ? windowsUi.TwoLetterISOLanguageName : setting;
        return wanted == "es" || Translated.Contains(wanted) ? wanted : "en";
    }

    internal static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Strata.Shell.Localization.{language}.json");
        if (stream is null)
            return new();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
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
