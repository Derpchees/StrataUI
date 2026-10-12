using Winx.Shell.Services;
using Winx.Shell.Services.StartMenu;

namespace Winx.Tests;

/// <summary>Operaciones sobre secciones y carpetas de anclados (sin tocar el settings.json real).</summary>
public sealed class PinLayoutTests
{
    public PinLayoutTests()
    {
        Settings.SuppressSave = true;
        Settings.Current.StartLayout =
        [
            new PinSection { Id = "s1", Name = "Anclado", Items = [App("a"), App("b"), App("c")] },
            new PinSection { Id = "s2", Name = "Juegos", Items = [App("d")] },
        ];
    }

    private static PinEntry App(string id) => new() { Id = "e-" + id, AppId = id };

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
    public void Gaps_are_inserted_and_removed()
    {
        PinLayout.InsertGap("e-b", after: false);
        PinLayout.InsertGap("e-c", after: true);
        Assert.Equal(["a", "_", "b", "c", "_"], Ids(0));

        PinLayout.RemoveGap(Layout[0].Items[1].Id);
        Assert.Equal(["a", "b", "c", "_"], Ids(0));
    }

    [Fact]
    public void Dropping_on_a_gap_swaps_places()
    {
        // La app ocupa el hueco y el hueco queda donde estaba: las demás no se corren.
        PinLayout.InsertGap("e-c", after: true);
        var gap = Layout[0].Items[3].Id;
        PinLayout.DropOnGap(gap, "e-a");
        Assert.Equal(["_", "b", "c", "a"], Ids(0));

        // También entre secciones.
        PinLayout.DropOnGap(Layout[0].Items[0].Id, "e-d");
        Assert.Equal(["d", "b", "c", "a"], Ids(0));
        Assert.Equal(["_"], Ids(1));
    }

    [Fact]
    public void A_gap_is_never_merged_into_a_folder()
    {
        PinLayout.InsertGap("e-a", after: true);
        var gap = Layout[0].Items[1].Id;
        PinLayout.Merge(gap, "e-c");
        PinLayout.Merge("e-c", gap);
        Assert.Equal(["a", "_", "b", "c"], Ids(0));
    }
}
