using Strata.Shell.Hub;

namespace Strata.Tests;

public class HubSearchTests
{
    private static readonly List<HubSearchEntry> Entries =
    [
        new("Desactivar telemetría", "Privacidad", "PrivacyPage", "Desactivar telemetría"),
        new("Telemetría de NVIDIA", "Rendimiento › Hardware", "HardwarePage"),
        new("Modo de juego", "Rendimiento › Juegos", "GamingPage"),
        new("Barra", "Barra de tareas", "TaskbarPage"),
    ];

    [Fact]
    public void Accents_and_case_do_not_matter() =>
        Assert.Equal(2, HubSearch.Find(Entries, "TELEMETRIA").Count);

    [Fact]
    public void Every_word_has_to_match_the_title_or_where_it_is()
    {
        var found = HubSearch.Find(Entries, "juego rendimiento");
        Assert.Equal("Modo de juego", Assert.Single(found).Title);
    }

    [Fact]
    public void Titles_that_start_with_the_text_come_first() =>
        Assert.Equal("Telemetría de NVIDIA", HubSearch.Find(Entries, "telem")[0].Title);

    [Fact]
    public void An_empty_query_finds_nothing() => Assert.Empty(HubSearch.Find(Entries, "  "));

    [Fact]
    public void Signs_are_ignored() => Assert.Equal(" que es esto ", HubSearch.Fold("¿Qué es esto?"));

    [Fact]
    public void The_spanish_text_also_finds_a_translated_entry()
    {
        var english = new HubSearchEntry("Turn off telemetry", "Privacy", "PrivacyPage", Also: "Desactivar telemetría Privacidad");
        Assert.Single(HubSearch.Find([english], "telemetria"));
    }
}
