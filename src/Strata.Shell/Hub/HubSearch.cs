using System.Globalization;
using System.Text;
using Strata.Shell.EditMode;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Tweaks;

namespace Strata.Shell.Hub;

/// <summary>
/// Un resultado del buscador del Hub: lo que se encontró, dónde está ("Privacidad › Inteligencia
/// artificial"), la página que se abre (un nombre de <see cref="HubWindow.NavigateTo"/>) y el texto que
/// se resalta en ella (el título de su tarjeta), si hay.
/// </summary>
/// <param name="Also">Otras palabras con que se encuentra (el texto en español si la interfaz está en otro idioma).</param>
internal sealed record HubSearchEntry(string Title, string Where, string Page, string? Highlight = null, string Also = "");

/// <summary>
/// Lo que el buscador del Hub encuentra: las entradas de la navegación, sus pestañas, las
/// optimizaciones (aunque el módulo no esté instalado: llevan a instalarlo), los elementos de la barra
/// y los widgets. Sin distinguir mayúsculas ni acentos ("telemetria" encuentra "telemetría").
/// </summary>
internal static class HubSearch
{
    /// <summary>Página de cada categoría del catálogo de optimizaciones.</summary>
    private static readonly Dictionary<string, string> TweakPages = new()
    {
        [TweakCatalog.Privacy] = "PrivacyPage",
        [TweakCatalog.AI] = "AIPage",
        [TweakCatalog.System] = "SystemTweaksPage",
        [TweakCatalog.Update] = "UpdatePage",
        [TweakCatalog.Appearance] = "WindowsAppearancePage",
        [TweakCatalog.Explorer] = "ExplorerPage",
        [TweakCatalog.Multitasking] = "MultitaskingPage",
        [TweakCatalog.Gaming] = "GamingPage",
        [TweakCatalog.Browsers] = "BrowsersPage",
        [TweakCatalog.Performance] = "PerformancePage",
        [TweakCatalog.Hardware] = "HardwarePage",
        [TweakCatalog.Memory] = "MemoryPage",
    };

    /// <param name="navigation">Las entradas de la navegación del Hub: su texto y su página.</param>
    public static List<HubSearchEntry> Build(IEnumerable<(string Title, Type Page)> navigation)
    {
        var entries = new List<HubSearchEntry>();
        foreach (var (title, page) in navigation)
            entries.Add(new(title, Loc.T("Hub"), page.Name));
        var where = new Dictionary<string, string>(); // página → "Entrada › Pestaña"
        foreach (var (_, hostTitle, tabs) in TabbedPage.Registered)
        {
            foreach (var tab in tabs)
            {
                entries.Add(new(tab.Title, hostTitle, tab.PageType.Name));
                where[tab.PageType.Name] = tab.Title == hostTitle ? hostTitle : string.Concat(hostTitle, " › ", tab.Title);
            }
        }
        string optimization = Loc.T("Optimización");
        foreach (var tweak in TweakCatalog.All.Where(t => t.IsAvailable))
        {
            if (!TweakPages.TryGetValue(tweak.Category, out var page))
                continue;
            entries.Add(new(tweak.Title, where.GetValueOrDefault(page, optimization), page, tweak.Title));
        }
        string bar = where.GetValueOrDefault("TaskbarPage", Loc.T("Barra de tareas"));
        foreach (var item in BarLayout.All)
            entries.Add(new(BarLayout.Name(item), bar, "TaskbarPage"));
        foreach (var widget in WidgetCatalog.All())
            entries.Add(new(widget.Name, Loc.T("Widgets"), "WidgetsPage"));
        // También en español: la interfaz puede estar en inglés y uno buscar "telemetría".
        entries = entries.Select(e => e with { Also = string.Join(" ", new[] { Loc.Original(e.Title), Loc.Original(e.Where) }.Where(t => t != null)) }).ToList();
        // Lo mismo dos veces (una pestaña que se llama como su entrada): queda uno.
        return entries.DistinctBy(e => (e.Title, e.Page)).ToList();
    }

    /// <summary>Los que contienen todas las palabras buscadas; primero los que empiezan con ellas.</summary>
    public static List<HubSearchEntry> Find(IReadOnlyList<HubSearchEntry> entries, string query, int max = 8)
    {
        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [];
        return entries
            .Select(entry => (entry, text: Fold(entry.Title), where: Fold(entry.Where + " " + entry.Also)))
            .Where(e => words.All(w => e.text.Contains(w) || e.where.Contains(w)))
            .OrderBy(e => e.text.StartsWith(words[0]) ? 0 : e.text.Contains(" " + words[0]) ? 1 : e.text.Contains(words[0]) ? 2 : 3)
            .ThenBy(e => e.text.Length)
            .Take(max)
            .Select(e => e.entry)
            .ToList();
    }

    /// <summary>En minúsculas y sin acentos ni signos ("¿Qué?" → "que").</summary>
    internal static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return builder.ToString();
    }
}
