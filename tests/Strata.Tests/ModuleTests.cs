using System.IO;
using System.Text.Json;
using Strata.Shell.Services;
using Strata.Shell.Services.Modules;
using Strata.Shell.Services.Themes;

namespace Strata.Tests;

/// <summary>Módulos importables: los ejemplos de docs/modules son válidos y lo inválido se rechaza.</summary>
public sealed class ModuleTests
{
    private static string Examples()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StrataUI.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("StrataUI.sln"), "docs", "modules", "examples");
    }

    private static ModuleKind KindOf(string type) => type switch
    {
        "theme" => ModuleKind.Theme,
        "animation" => ModuleKind.Animation,
        "widget" => ModuleKind.Widget,
        _ => ModuleKind.StartMenu,
    };

    public static TheoryData<string> ExampleFiles() =>
        new(Directory.EnumerateFiles(Examples(), "*.json").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Examples_are_valid(string file)
    {
        var path = Path.Combine(Examples(), file);
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var kind = KindOf(doc.RootElement.GetProperty("type").GetString()!);
        var (module, error) = ModuleStore.Parse(kind, path);
        Assert.True(module != null, $"{file}: {error}");
        Assert.Equal(Path.GetFileNameWithoutExtension(file), module!.Id);
    }

    [Fact]
    public void All_module_kinds_have_an_example()
    {
        var types = Directory.EnumerateFiles(Examples(), "*.json")
            .Select(f => JsonDocument.Parse(File.ReadAllText(f), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })
                .RootElement.GetProperty("type").GetString())
            .ToHashSet();
        foreach (var kind in Enum.GetValues<ModuleKind>())
            Assert.Contains(ModuleStore.TypeName(kind), types);
    }

    [Theory]
    [InlineData("""{ "type": "animation", "name": "X" }""")]                                    // otro tipo
    [InlineData("""{ "type": "theme", "name": "" }""")]                                         // sin nombre
    [InlineData("""{ "type": "theme", "name": "X", "colors": { "bar": "red" } }""")]            // color con nombre
    [InlineData("""{ "type": "theme", "name": "X", "colors": { "bar": "#000", "panel": "#000000" } }""")]
    [InlineData("""{ "type": "theme", "name": "X", "backdrop": "plasma" }""")]
    [InlineData("""{ "type": "theme" "name": "X" }""")]                                         // JSON roto
    public void Invalid_themes_are_rejected(string json)
    {
        var (module, error) = ModuleStore.ParseText(ModuleKind.Theme, json);
        Assert.Null(module);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("""{ "type": "widget", "name": "X", "lines": [] }""")]
    [InlineData("""{ "type": "widget", "name": "X", "lines": [ { "text": "a", "color": "blue" } ] }""")]
    [InlineData("""{ "type": "widget", "name": "X", "background": "#GG0000", "lines": [ { "text": "a" } ] }""")]
    public void Invalid_widgets_are_rejected(string json) =>
        Assert.Null(ModuleStore.ParseText(ModuleKind.Widget, json).Module);

    [Fact]
    public void Theme_without_light_is_deduced_from_the_panel()
    {
        var (module, _) = ModuleStore.ParseText(ModuleKind.Theme, """
            { "type": "THEME", "Name": "Paper", "colors": { "bar": "#EEEEEE", "panel": "#FAFAFA", "surface": "#FFFFFF",
              "text": "#111111", "subText": "#555555", "border": "#DDDDDD" } }
            """);
        var theme = ((ThemeModule)module!).ToTheme();
        Assert.True(theme.Light);
        Assert.Equal(Backdrop.Solid, theme.Backdrop);
        Assert.NotNull(theme.Scheme);
        Assert.StartsWith("module:", theme.Key);
    }

    [Fact]
    public void Animation_values_are_clamped_and_exit_is_filled()
    {
        var (module, _) = ModuleStore.ParseText(ModuleKind.Animation, """
            { "type": "animation", "name": "Wild", "enter": { "offset": 9999, "scale": 0, "duration": 100, "easing": "elastic" } }
            """);
        var animation = (AnimationModule)module!;
        Assert.Equal(200, animation.Enter.Offset);
        Assert.Equal(0.3, animation.Enter.Scale);
        Assert.Equal(4, animation.Enter.Duration);
        Assert.NotNull(animation.Exit);
        Assert.True(animation.Exit!.Offset > 0);
    }

    [Fact]
    public void Start_menu_look_changes_only_what_it_has()
    {
        var settings = new StrataSettings { StartShowRecent = true, StartIconSize = StartIconSize.Large };
        var (module, _) = ModuleStore.ParseText(ModuleKind.StartMenu, """
            { "type": "startmenu", "name": "M", "style": "compact", "pinnedLabels": false, "footer": ["Power", "Nope", "Power"], "width": 400, "height": 500 }
            """);
        ((StartMenuModule)module!).ApplyTo(settings);
        Assert.Equal(StartMenuStyle.Compact, settings.StartStyle);
        Assert.False(settings.StartShowPinnedLabels);
        Assert.True(settings.StartShowRecent);                       // no venía: queda igual
        Assert.Equal(StartIconSize.Large, settings.StartIconSize);
        Assert.Equal(["Power"], settings.StartFooterButtons);          // sin desconocidos ni repetidos
        Assert.Equal([400.0, 500.0], settings.StartMenuSizes["StartMenuCompact"]);
    }

    [Fact]
    public void Exported_start_menu_reads_back_the_same()
    {
        var settings = new StrataSettings { StartStyle = StartMenuStyle.Icons, StartIcon = StartIconKind.Grid, StartShowUser = false };
        var exported = StartMenuModule.From(settings, "Mine");
        var json = JsonSerializer.Serialize(exported, ModuleStore.Json);
        var (module, error) = ModuleStore.ParseText(ModuleKind.StartMenu, json);
        Assert.True(module != null, error);
        var copy = new StrataSettings();
        ((StartMenuModule)module!).ApplyTo(copy);
        Assert.Equal(StartMenuStyle.Icons, copy.StartStyle);
        Assert.Equal(StartIconKind.Grid, copy.StartIcon);
        Assert.False(copy.StartShowUser);
    }

    [Theory]
    [InlineData("Solarized Dark", "Solarized Dark")]
    [InlineData(@"..\..\evil", "evil")]
    [InlineData("a/b:c*d", "a-b-c-d")]
    [InlineData("...", "module")]
    public void File_names_are_safe(string name, string expected) =>
        Assert.Equal(expected, ModuleStore.FileNameFor(name));
}
