using Winx.Shell.Services.StartMenu;
using Winx.Shell.Services.Updates;

namespace Winx.Tests;

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
          "html_url": "https://github.com/Derpchees/WINX/releases/tag/v1.2.3",
          "body": "### Añadido\n- Algo nuevo",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://x/notes.txt", "size": 10 },
            { "name": "WINX-Setup-1.2.3.exe", "browser_download_url": "https://x/WINX-Setup-1.2.3.exe", "size": 5000 }
          ]
        }
        """;
        var info = UpdateService.Parse(json)!;
        Assert.Equal(new Version(1, 2, 3), info.Version);
        Assert.Equal("https://x/WINX-Setup-1.2.3.exe", info.InstallerUrl);
        Assert.Equal(5000, info.InstallerSize);
        Assert.StartsWith("### Añadido", info.Notes);
    }

    [Fact]
    public void Release_with_an_invalid_tag_is_ignored() =>
        Assert.Null(UpdateService.Parse("""{ "tag_name": "nightly" }"""));
}
