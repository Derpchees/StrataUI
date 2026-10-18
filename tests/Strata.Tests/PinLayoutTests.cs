using Strata.Shell.Services;
using Strata.Shell.Services.StartMenu;

namespace Strata.Tests;

/// <summary>Operaciones sobre secciones y carpetas de anclados (sin tocar el settings.json real).</summary>
public sealed class PinLayoutTests
{
    public PinLayoutTests()
    {
        Settings.SuppressSave = true;
        // Sin catálogo de apps: todas cuentan como instaladas salvo las que la prueba diga.
        PinLayout.IsAvailable = id => !id.StartsWith("missing", StringComparison.Ordinal);
        Settings.Current.StartLayout =
        [
            new PinSection { Id = "s1", Name = "Anclado", Items = [App("a"), App("b"), App("c")] },
            new PinSection { Id = "s2", Name = "Juegos", Items = [App("d")] },
        ];
    }

    private static PinEntry App(string id) => new() { Id = "e-" + id, AppId = id };

    /// <summary>Deja una celda vacía en esa posición de la sección (como la colocación libre).</summary>
    private static void GapAt(int section, int index) => Layout[section].Items.Insert(index, new PinEntry { Id = $"g{section}-{index}" });

    private static List<PinSection> Layout => Settings.Current.StartLayout!;

    private static string[] Ids(int section) =>
        Layout[section].Items.Select(e => e.IsGap ? "_" : e.IsFolder ? "[" + string.Join(",", e.FolderApps!) + "]" : e.AppId!).ToArray();

    [Fact]
    public void Move_reorders_within_and_between_sections()
    {
        PinLayout.Move("e-c", "s1", 0);
        Assert.Equal(["c", "a", "b"], Ids(0));

        PinLayout.Move("e-a", "s2", 1);
        Assert.Equal(["c", "b"], Ids(0));
        Assert.Equal(["d", "a"], Ids(1));
    }

    [Fact]
    public void Swapping_moves_only_the_two_apps()
    {
        PinLayout.Swap("e-a", "e-c");
        Assert.Equal(["c", "b", "a"], Ids(0));

        PinLayout.Swap("e-b", "e-d");
        Assert.Equal(["c", "d", "a"], Ids(0));
        Assert.Equal(["b"], Ids(1));
    }

    [Fact]
    public void Dropping_an_app_on_another_creates_a_folder_in_place()
    {
        PinLayout.Merge("e-c", "e-a");
        Assert.Equal(["[a,c]", "b"], Ids(0));
    }

    [Fact]
    public void Dropping_an_app_on_a_folder_adds_it()
    {
        PinLayout.Merge("e-b", "e-a");
        var folderId = Layout[0].Items[0].Id;
        PinLayout.Merge("e-d", folderId);
        Assert.Equal(["[a,b,d]", "c"], Ids(0));
        Assert.Empty(Layout[1].Items);
    }

    [Fact]
    public void Folder_with_one_app_left_becomes_a_plain_app()
    {
        PinLayout.Merge("e-b", "e-a");
        var folderId = Layout[0].Items[0].Id;
        PinLayout.TakeOutOfFolder(folderId, "b", "s2", 0);
        Assert.Equal(["a", "c"], Ids(0));
        Assert.Equal(["b", "d"], Ids(1));
    }

    [Fact]
    public void Ungroup_puts_apps_back_in_the_same_place()
    {
        PinLayout.Merge("e-c", "e-b");
        PinLayout.Ungroup(Layout[0].Items[1].Id);
        Assert.Equal(["a", "b", "c"], Ids(0));
    }

    [Fact]
    public void Deleting_a_section_keeps_its_apps()
    {
        PinLayout.DeleteSection("s2");
        Assert.Single(Layout);
        Assert.Equal(["a", "b", "c", "d"], Ids(0));
    }

    [Fact]
    public void Unpinning_leaves_its_cell_empty()
    {
        // Como al moverla: las demás no se corren (y al final no queda una celda vacía colgando).
        PinLayout.Unpin("b");
        Assert.Equal(["a", "_", "c"], Ids(0));
        PinLayout.Unpin("c");
        Assert.Equal(["a"], Ids(0));
    }

    [Fact]
    public void Unpin_removes_from_folders_too()
    {
        PinLayout.Merge("e-b", "e-a");
        PinLayout.Unpin("a");
        Assert.Equal(["b", "c"], Ids(0));
        Assert.False(PinLayout.Contains("a"));
    }

    [Fact]
    public void Querying_before_migration_does_not_block_it()
    {
        Settings.Current.StartLayout = null;
        Settings.Current.PinnedStartApps = ["x"];
        Assert.False(PinLayout.Contains("x"));   // antes creaba una lista vacía aquí
        PinLayout.EnsureLayout(() => []);
        Assert.Equal(["x"], Ids(0));
    }

    [Fact]
    public void Old_flat_list_migrates_to_one_section()
    {
        Settings.Current.StartLayout = null;
        Settings.Current.PinnedStartApps = ["x", "y"];
        PinLayout.EnsureLayout(() => []);
        Assert.Single(Layout);
        Assert.Equal(["x", "y"], Ids(0));
        Assert.Null(Settings.Current.PinnedStartApps);
    }

    [Fact]
    public void Empty_cells_at_the_end_of_a_section_are_dropped()
    {
        // Mover lo último de una sección no deja celdas vacías colgando.
        PinLayout.PlaceAtCell("s2", 3, "e-c");
        Assert.Equal(["a", "b"], Ids(0));
        Assert.Equal(["d", "_", "_", "c"], Ids(1));
        PinLayout.Move("e-c", "s1", 0);
        Assert.Equal(["d"], Ids(1));
    }

    [Fact]
    public void Dropping_on_a_gap_swaps_places()
    {
        // La app ocupa el hueco y el hueco queda donde estaba: las demás no se corren.
        GapAt(0, 3);
        var gap = Layout[0].Items[3].Id;
        PinLayout.DropOnGap(gap, "e-a");
        Assert.Equal(["_", "b", "c", "a"], Ids(0));

        // También entre secciones (la sección que queda vacía no guarda la celda).
        PinLayout.DropOnGap(Layout[0].Items[0].Id, "e-d");
        Assert.Equal(["d", "b", "c", "a"], Ids(0));
        Assert.Empty(Ids(1));
    }

    [Fact]
    public void Placing_past_the_end_fills_with_gaps_and_leaves_a_gap_behind()
    {
        // Como en un teléfono: "a" va a la celda 5 y nada se corre.
        PinLayout.PlaceAtCell("s1", 5, "e-a");
        Assert.Equal(["_", "b", "c", "_", "_", "a"], Ids(0));

        // Otra sección y una app que sale de una carpeta.
        PinLayout.Merge("e-c", "e-b");
        var folder = Layout[0].Items[1].Id;
        PinLayout.PlaceAtCell("s2", 2, null, folder, "c");
        Assert.Equal(["d", "_", "c"], Ids(1));
        Assert.Equal(["_", "b", "_", "_", "a"], Ids(0)); // la carpeta con solo "b" vuelve a ser "b"
    }

    [Fact]
    public void Placing_on_a_gap_cell_takes_it_and_an_occupied_cell_is_ignored()
    {
        GapAt(0, 1);                                         // a _ b c
        PinLayout.PlaceAtCell("s1", 1, "e-d");
        Assert.Equal(["a", "d", "b", "c"], Ids(0));
        PinLayout.PlaceAtCell("s1", 0, "e-c");               // ocupada: no cambia nada
        Assert.Equal(["a", "d", "b", "c"], Ids(0));
    }

    [Fact]
    public void Cells_count_only_what_is_shown()
    {
        // Las apps no instaladas siguen en el diseño pero no ocupan celda: la celda 4 de la vista
        // es la segunda después de "c" (antes se tomaba como índice del diseño y no pasaba nada).
        Layout[0].Items.Add(App("missing1"));
        Layout[0].Items.Insert(1, App("missing2"));          // a (missing2) b c (missing1)
        PinLayout.PlaceAtCell("s1", 4, "e-d");
        Assert.Equal(["a", "missing2", "b", "c", "missing1", "_", "d"], Ids(0));

        // Una celda ocupada de la vista (la 1 es "b") no cambia nada.
        PinLayout.PlaceAtCell("s1", 1, "e-a");
        Assert.Equal(["a", "missing2", "b", "c", "missing1", "_", "d"], Ids(0));
    }

    [Fact]
    public void Pinning_fills_the_first_gap()
    {
        GapAt(0, 1);
        PinLayout.Pin("x");
        Assert.Equal(["a", "x", "b", "c"], Ids(0));
        PinLayout.Pin("y");                                  // sin huecos: al final
        Assert.Equal(["a", "x", "b", "c", "y"], Ids(0));
    }

    [Fact]
    public void A_gap_is_never_merged_into_a_folder()
    {
        GapAt(0, 1);
        var gap = Layout[0].Items[1].Id;
        PinLayout.Merge(gap, "e-c");
        PinLayout.Merge("e-c", gap);
        Assert.Equal(["a", "_", "b", "c"], Ids(0));
    }
}
