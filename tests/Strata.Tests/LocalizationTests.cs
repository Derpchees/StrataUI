using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Strata.Shell.Services.Localization;

namespace Strata.Tests;

/// <summary>Cada texto de la interfaz tiene traducción y los huecos {0} coinciden.</summary>
public sealed partial class LocalizationTests
{
    private static readonly string Source = FindSource();

    private static string FindSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StrataUI.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("StrataUI.sln"), "src", "Strata.Shell");
    }

    [GeneratedRegex(@"Loc\.[TF]\(\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex CsKey();

    [GeneratedRegex(@"\{l:T '((?:[^'\\]|\\.)*)'\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"\{(\d+)(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Source, pattern, SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                          && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static HashSet<string> Keys()
    {
        var keys = new HashSet<string>();
        foreach (var file in SourceFiles("*.cs"))
            foreach (Match m in CsKey().Matches(File.ReadAllText(file)))
                keys.Add(Regex.Unescape(m.Groups[1].Value));
        foreach (var file in SourceFiles("*.xaml"))
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file)))
                keys.Add(WebUtility.HtmlDecode(m.Groups[1].Value.Replace("\\'", "'").Replace(@"\\", @"\")));
        return keys;
    }

    [Fact]
    public void Every_text_has_an_english_translation()
    {
        var english = Loc.Load("en");
        var keys = Keys();
        Assert.True(keys.Count > 400, $"Solo se encontraron {keys.Count} textos: ¿cambió la forma de marcarlos?");
        var missing = keys.Where(k => !english.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Sin traducción al inglés:\n" + string.Join("\n", missing));
    }

    /// <summary>Los idiomas incluidos en el programa (no los propios de la carpeta del usuario).</summary>
    public static TheoryData<string> Bundled() =>
        new(Directory.EnumerateFiles(Path.Combine(Source, "Localization"), "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!));

    [Theory]
    [MemberData(nameof(Bundled))]
    public void Bundled_languages_translate_every_text(string language)
    {
        var table = Loc.Load(language);
        var missing = Keys().Where(k => !table.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, $"Sin traducción ({language}):\n" + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(Bundled))]
    public void Translations_keep_the_placeholders(string language)
    {
        static string Holes(string s) => string.Join(",", Placeholder().Matches(s).Select(m => m.Groups[1].Value).Order());
        var wrong = Loc.Load(language).Where(p => Holes(p.Key) != Holes(p.Value)).Select(p => p.Key).ToList();
        Assert.True(wrong.Count == 0, $"Huecos distintos ({language}):\n" + string.Join("\n", wrong));
    }

    [Theory]
    [InlineData("auto", "es-MX", "es")]
    [InlineData("auto", "en-US", "en")]
    [InlineData("auto", "de-DE", "de")]
    [InlineData("auto", "pt-BR", "pt")]
    [InlineData("auto", "ja-JP", "en")]   // sin traducción: inglés
    [InlineData("es", "en-US", "es")]
    [InlineData("en", "es-ES", "en")]
    [InlineData("fr", "en-US", "fr")]
    public void Language_follows_windows_unless_chosen(string setting, string windows, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, CultureInfo.GetCultureInfo(windows)));
}
