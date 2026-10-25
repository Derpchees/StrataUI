using System.IO;
using Strata.Shell.Services.Files;
using Xunit;

namespace Strata.Tests;

/// <summary>Copiar y mover con el motor propio: nombres repetidos, carpetas, orígenes que faltan.</summary>
public sealed class FileTransfersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "strata-transfer-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "origen");
    private string Target => Path.Combine(_root, "destino");

    public FileTransfersTests()
    {
        Directory.CreateDirectory(Path.Combine(Source, "carpeta", "sub"));
        Directory.CreateDirectory(Path.Combine(Source, "carpeta", "vacía"));
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Source, "nota.txt"), "nueva");
        File.WriteAllText(Path.Combine(Source, "carpeta", "sub", "dentro.txt"), "dentro");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task<bool> Run(TransferKind kind, TransferConflict conflict, string folder, params string[] names) =>
        FileTransfers.Start(kind, names.Select(n => Path.Combine(Source, n)).ToList(), folder, conflict);

    [Fact]
    public async Task Copies_files_and_folders_with_empty_ones()
    {
        Assert.True(await Run(TransferKind.Copy, TransferConflict.KeepBoth, Target, "nota.txt", "carpeta"));
        Assert.Equal("nueva", File.ReadAllText(Path.Combine(Target, "nota.txt")));
        Assert.Equal("dentro", File.ReadAllText(Path.Combine(Target, "carpeta", "sub", "dentro.txt")));
        Assert.True(Directory.Exists(Path.Combine(Target, "carpeta", "vacía")));
        Assert.True(File.Exists(Path.Combine(Source, "nota.txt"))); // copiar deja el original
    }

    [Fact]
    public async Task Keep_both_numbers_the_new_one()
    {
        File.WriteAllText(Path.Combine(Target, "nota.txt"), "vieja");
        Assert.True(await Run(TransferKind.Copy, TransferConflict.KeepBoth, Target, "nota.txt"));
        Assert.Equal("vieja", File.ReadAllText(Path.Combine(Target, "nota.txt")));
        Assert.Equal("nueva", File.ReadAllText(Path.Combine(Target, "nota (2).txt")));
    }

    [Fact]
    public async Task Replace_overwrites_even_read_only()
    {
        string old = Path.Combine(Target, "nota.txt");
        File.WriteAllText(old, "vieja");
        File.SetAttributes(old, FileAttributes.ReadOnly);
        Assert.True(await Run(TransferKind.Copy, TransferConflict.Replace, Target, "nota.txt"));
        Assert.Equal("nueva", File.ReadAllText(old));
    }

    [Fact]
    public async Task Pasting_in_the_same_folder_makes_a_copy()
    {
        Assert.True(await Run(TransferKind.Copy, TransferConflict.Duplicate, Source, "nota.txt", "carpeta"));
        Assert.True(File.Exists(Path.Combine(Source, "nota - copia.txt")) || File.Exists(Path.Combine(Source, "nota - Copy.txt")));
        Assert.Equal(2, Directory.GetDirectories(Source).Length);
    }

    [Fact]
    public async Task Move_takes_everything_and_leaves_nothing_behind()
    {
        Assert.True(await Run(TransferKind.Move, TransferConflict.KeepBoth, Target, "nota.txt", "carpeta"));
        Assert.True(File.Exists(Path.Combine(Target, "carpeta", "sub", "dentro.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(Source));
    }

    [Fact]
    public async Task Moving_into_an_existing_folder_merges_it()
    {
        Directory.CreateDirectory(Path.Combine(Target, "carpeta"));
        File.WriteAllText(Path.Combine(Target, "carpeta", "ya.txt"), "ya");
        Assert.True(await Run(TransferKind.Move, TransferConflict.Replace, Target, "carpeta"));
        Assert.True(File.Exists(Path.Combine(Target, "carpeta", "ya.txt")));
        Assert.True(File.Exists(Path.Combine(Target, "carpeta", "sub", "dentro.txt")));
        Assert.False(Directory.Exists(Path.Combine(Source, "carpeta")));
    }

    [Fact]
    public async Task A_missing_source_does_not_stop_the_rest()
    {
        Assert.False(await Run(TransferKind.Copy, TransferConflict.KeepBoth, Target, "no-existe.txt", "nota.txt"));
        Assert.True(File.Exists(Path.Combine(Target, "nota.txt")));
    }

    [Fact]
    public async Task A_folder_cannot_go_inside_itself()
    {
        Assert.False(await Run(TransferKind.Copy, TransferConflict.KeepBoth, Path.Combine(Source, "carpeta", "sub"), "carpeta"));
        Assert.False(Directory.Exists(Path.Combine(Source, "carpeta", "sub", "carpeta")));
    }
}
