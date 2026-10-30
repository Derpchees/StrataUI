using Strata.Shell.Services.StartMenu;
using Strata.Shell.Services.Updates;

namespace Strata.Tests;

/// <summary>Desinstalador de las apps de Inicio y lectura de las versiones publicadas.</summary>
public sealed class StartAndUpdateTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\App\uninst.exe"" /S", @"C:\Program Files\App\uninst.exe", "/S")]
    [InlineData(@"C:\Program Files\App\uninstall.exe --force", @"C:\Program Files\App\uninstall.exe", "--force")]
    [InlineData("MsiExec.exe /X{1234}", "MsiExec.exe", "/X{1234}")]
    [InlineData(@"""C:\Apps\Tool\unins000.exe""", @"C:\Apps\Tool\unins000.exe", "")]
    public void Uninstall_command_is_split_into_file_and_arguments(string command, string file, string args) =>
        Assert.Equal((file, args), AppActions.SplitCommand(command));

    [Fact]
    public void Uninstaller_prefers_the_entry_that_owns_the_executable()
    {
        const string exe = @"C:\Apps\Motrix\Motrix.exe";
        // El ícono es el propio ejecutable: la coincidencia más segura.
        Assert.Equal(4, AppActions.UninstallScore("Motrix", @"C:\Apps\Motrix", exe, "Motrix 1.8", null, "\"" + exe + "\",0"));
        // La carpeta de instalación contiene el ejecutable.
        Assert.Equal(3, AppActions.UninstallScore("Motrix", @"C:\Apps\Motrix", exe, "Otro", @"C:\Apps\Motrix\", null));
        // Solo por nombre.
        Assert.Equal(2, AppActions.UninstallScore("Motrix", null, null, "Motrix", null, null));
        Assert.Equal(1, AppActions.UninstallScore("Motrix", null, null, "Motrix 1.8.19", null, null));
        Assert.Equal(0, AppActions.UninstallScore("Motrix", null, null, "MotrixHelper", null, null));
    }

    [Fact]
    public void Release_is_read_from_the_github_response()
    {
        const string json = """
        {
          "tag_name": "v1.2.3",
          "html_url": "https://github.com/Derpchees/StrataUI/releases/tag/v1.2.3",
          "body": "### Añadido\n- Algo nuevo",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://x/notes.txt", "size": 10 },
            { "name": "StrataUI-Setup-1.2.3.exe", "browser_download_url": "https://x/StrataUI-Setup-1.2.3.exe", "size": 5000,
              "digest": "sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08" }
          ]
        }
        """;
        var info = UpdateService.Parse(json)!;
        Assert.Equal(new Version(1, 2, 3), info.Version);
        Assert.Equal("https://x/StrataUI-Setup-1.2.3.exe", info.InstallerUrl);
        Assert.Equal(5000, info.InstallerSize);
        Assert.Equal("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", info.InstallerSha256);
        Assert.StartsWith("### Añadido", info.Notes);
    }

    [Fact]
    public void A_release_without_digest_has_no_hash_to_check()
    {
        var info = UpdateService.Parse("""
            { "tag_name": "v1.0.0", "assets": [ { "name": "a.exe", "browser_download_url": "https://x/a.exe", "digest": null } ] }
            """)!;
        Assert.Null(info.InstallerSha256);
    }

    [Fact]
    public void Release_with_an_invalid_tag_is_ignored() =>
        Assert.Null(UpdateService.Parse("""{ "tag_name": "nightly" }"""));

    [Theory]
    [InlineData("Brave._crx_pmniijhifabcifkhcllpfigbjbkjnlcb", "pmniijhifabcifkhcllpfigbjbkjnlcb")]
    [InlineData("Chrome._crx_agimnkijcaahngcdmfeangaknmldooml!App", "agimnkijcaahngcdmfeangaknmldooml")]
    [InlineData("Brave.BraveBrowser", null)]
    [InlineData("_crx_short", null)]
    public void Web_apps_are_recognized_by_their_id(string id, string? expected) =>
        Assert.Equal(expected, AppActions.CrxId(id));

    [Theory]
    [InlineData(" --profile-directory=Default --app-id=mbjjclfgakoikobpdbdffincflpcjkag", "mbjjclfgakoikobpdbdffincflpcjkag")]
    [InlineData("--app-id=xyz", null)]
    [InlineData(null, null)]
    public void Web_apps_are_recognized_by_their_shortcut(string? arguments, string? expected) =>
        Assert.Equal(expected, AppActions.AppIdArgument(arguments));
}
