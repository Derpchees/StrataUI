using System.IO;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Xunit;

namespace Strata.Tests;

/// <summary>Comprimidos, permiso de administrador y formatear: lo que se puede probar sin la interfaz ni UAC.</summary>
public sealed class ArchivesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "strata-archives-" + Guid.NewGuid().ToString("N"));

    public ArchivesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(@"..\..\Windows\evil.dll", @"Windows\evil.dll")]
    [InlineData(@"C:\Windows\x.txt", @"C_\Windows\x.txt")]
    [InlineData("carpeta/sub/archivo.txt", @"carpeta\sub\archivo.txt")]
    [InlineData(@"\\servidor\recurso\a.txt", @"servidor\recurso\a.txt")]
    [InlineData("nombre. ", "nombre")]
    public void Archive_paths_never_leave_the_folder(string raw, string clean) =>
        Assert.Equal(clean, ArchiveHost.CleanPath(raw));

    [Fact]
    public void Locates_the_archive_and_the_folder_inside()
    {
        string archive = Path.Combine(_root, "fotos.zip");
        File.WriteAllBytes(archive, [0x50, 0x4B, 5, 6]);
        Assert.Equal((archive, ""), Archives.Locate(archive));
        Assert.Equal((archive, @"2024\verano"), Archives.Locate(Path.Combine(archive, "2024", "verano")));
        Assert.Null(Archives.Locate(_root));
        // Una carpeta que se llama como un comprimido es una carpeta.
        Directory.CreateDirectory(Path.Combine(_root, "falso.zip", "dentro"));
        Assert.Null(Archives.Locate(Path.Combine(_root, "falso.zip", "dentro")));
    }

    [Fact]
    public void Listing_adds_folders_that_only_appear_in_paths()
    {
        var listing = new ArchiveListing("x.zip", "zip",
        [
            new ArchiveItem(0, @"docs\a.txt", false, 1, DateTime.Now, false),
            new ArchiveItem(1, @"docs\sub\b.txt", false, 2, DateTime.Now, false),
            new ArchiveItem(2, "raiz.txt", false, 3, DateTime.Now, false),
        ], DateTime.UtcNow, 10);
        Assert.Equal(["docs", "raiz.txt"], listing.Children("").Select(c => c.Name).Order());
        Assert.True(listing.Children("").Single(c => c.Name == "docs").IsFolder);
        Assert.Equal(["a.txt", "sub"], listing.Children("docs").Select(c => c.Name).Order());
        Assert.True(listing.HasFolder(@"docs\sub"));
        Assert.False(listing.HasFolder("nada"));
    }

    private static ElevatedFiles.Job Job(string op, params string[] paths) => new() { Op = op, Paths = [.. paths] };

    [Fact]
    public void Elevated_process_rejects_what_it_should_never_do()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.NotNull(ElevatedFiles.Validate(Job("delete", @"C:\")));
        Assert.NotNull(ElevatedFiles.Validate(Job("delete", windows)));
        Assert.NotNull(ElevatedFiles.Validate(Job("delete", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))));
        Assert.NotNull(ElevatedFiles.Validate(Job("mkdir", @"\\?\C:\x")));
        Assert.NotNull(ElevatedFiles.Validate(Job("mkdir", @"C:\a\..\Windows\x")));
        Assert.NotNull(ElevatedFiles.Validate(Job("delete", @"C:\a.txt:stream")));
        Assert.NotNull(ElevatedFiles.Validate(Job("borrar-todo", @"C:\a")));
        Assert.NotNull(ElevatedFiles.Validate(new ElevatedFiles.Job { Op = "rename", Paths = [Path.Combine(_root, "a.txt")], NewName = @"..\b.txt" }));
        Assert.NotNull(ElevatedFiles.Validate(new ElevatedFiles.Job
        {
            Op = "copy", Conflict = "Replace", Folder = _root, Paths = [@"C:\a.txt"],
            Targets = new() { [@"C:\a.txt"] = Path.Combine(windows, "a.txt") },
        }));
        Assert.NotNull(ElevatedFiles.Validate(new ElevatedFiles.Job { Op = "format", Paths = [@"C:\"], FileSystem = "NTFS" }));
        Assert.NotNull(ElevatedFiles.Validate(new ElevatedFiles.Job { Op = "move", Conflict = "Replace", Folder = _root, Paths = [windows] }));
        // Copiarla sí se puede (no la quita de su lugar).
        Assert.Null(ElevatedFiles.Validate(new ElevatedFiles.Job { Op = "copy", Conflict = "Replace", Folder = _root, Paths = [windows] }));
    }

    [Fact]
    public void Elevated_process_accepts_normal_work()
    {
        string file = Path.Combine(_root, "a.txt");
        Assert.Null(ElevatedFiles.Validate(Job("delete", file)));
        Assert.Null(ElevatedFiles.Validate(Job("mkdir", Path.Combine(_root, "nueva"))));
        Assert.Null(ElevatedFiles.Validate(new ElevatedFiles.Job { Op = "rename", Paths = [file], NewName = "b.txt" }));
        Assert.Null(ElevatedFiles.Validate(new ElevatedFiles.Job
        {
            Op = "copy", Conflict = "KeepBoth", Folder = Path.Combine(_root, "destino"), Paths = [file],
            Targets = new() { [file] = Path.Combine(_root, "destino", "a (2).txt") },
        }));
    }

    [Fact]
    public void The_windows_drive_can_never_be_formatted()
    {
        Assert.False(DriveFormat.CanFormat(Path.GetPathRoot(Environment.SystemDirectory)!));
        Assert.False(DriveFormat.CanFormat(@"C:\Windows"));
        Assert.False(DriveFormat.CanFormat(@"\\servidor\recurso"));
    }

    [Theory]
    [InlineData("Mi USB", "FAT32", "Mi USB")]
    [InlineData("nombre.largo+raro", "exFAT", "nombrelargo")]
    [InlineData("Un nombre de unidad muy largo para NTFS", "NTFS", "Un nombre de unidad muy largo pa")]
    public void Volume_labels_fit_their_file_system(string label, string fileSystem, string expected) =>
        Assert.Equal(expected, DriveFormat.CleanLabel(label, fileSystem));

    [Theory]
    [InlineData("(3) Discord | #general", true)]
    [InlineData("• Discord | Friends", true)]
    [InlineData("Discord | Friends", false)]
    [InlineData("Discord (beta)", false)]
    public void Vesktop_unread_titles(string title, bool unread) => Assert.Equal(unread, AppIconOverrides.Unread(title));
}
