using System.IO;
using System.Text.Json;
using Strata.Shell.Services;
using Xunit;

namespace Strata.Tests;

public class SafeFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("strata-safefile-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string FileIn(string name) => Path.Combine(_folder, name);

    private static int Parse(string text) => JsonSerializer.Deserialize<int>(text);

    [Fact]
    public void Each_save_keeps_the_previous_file_as_backup()
    {
        string path = FileIn("settings.json");

        SafeFile.Write(path, "1");
        SafeFile.Write(path, "2");

        Assert.Equal("2", File.ReadAllText(path));
        Assert.Equal("1", File.ReadAllText(SafeFile.BackupOf(path)));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_damaged_file_is_set_aside_and_the_backup_is_read()
    {
        string path = FileIn("settings.json");
        SafeFile.Write(path, "1");
        SafeFile.Write(path, "2");
        File.WriteAllText(path, ""); // lo que dejaba un apagón al guardar

        int value = SafeFile.Read(path, Parse, "Prueba");

        Assert.Equal(1, value);
        Assert.Single(Directory.GetFiles(_folder, "settings.invalid-*.json"));
    }

    [Fact]
    public void A_missing_file_reads_as_default()
    {
        Assert.Equal(0, SafeFile.Read(FileIn("nada.json"), Parse, "Prueba"));
    }
}
