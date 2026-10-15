using System.IO;
using Strata.Shell.Services.Cleanup;

namespace Strata.Tests;

/// <summary>El limpiador borra solo lo viejo de sus carpetas (en una carpeta de prueba, no las reales).</summary>
public sealed class JunkCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "strataui-test-" + Guid.NewGuid().ToString("N"));

    public JunkCleanerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string File(string relative, TimeSpan age)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, new string('x', 1000));
        System.IO.File.SetLastWriteTime(path, DateTime.Now - age);
        return path;
    }

    [Fact]
    public void Only_old_files_are_deleted_and_empty_folders_removed()
    {
        var old = File(@"a\old.tmp", TimeSpan.FromDays(3));
        var oldNested = File(@"b\c\old.tmp", TimeSpan.FromDays(3));
        var fresh = File(@"a\new.tmp", TimeSpan.FromMinutes(5));
        var category = new CleanupCategory("test", "Prueba", "", false, true,
            () => [new CleanupTarget(_root, MinAge: TimeSpan.FromDays(1))]);

        var measured = JunkCleaner.Measure(category);
        Assert.Equal(2, measured.Files);
        Assert.Equal(2000, measured.Bytes);

        var freed = JunkCleaner.Clean(category);
        Assert.Equal(2, freed.Files);
        Assert.False(System.IO.File.Exists(old));
        Assert.False(System.IO.File.Exists(oldNested));
        Assert.True(System.IO.File.Exists(fresh));
        Assert.False(Directory.Exists(Path.Combine(_root, "b"))); // quedó vacía
        Assert.True(Directory.Exists(_root));                      // la carpeta de la categoría se queda
    }

    [Fact]
    public void Pattern_and_non_recursive_targets_are_respected()
    {
        File("thumbcache_1.db", TimeSpan.FromDays(3));
        var other = File("keep.db", TimeSpan.FromDays(3));
        var nested = File(@"sub\thumbcache_2.db", TimeSpan.FromDays(3));
        var category = new CleanupCategory("test", "Prueba", "", false, true,
            () => [new CleanupTarget(_root, "thumbcache_*.db", Recursive: false)]);

        Assert.Equal(1, JunkCleaner.Clean(category).Files);
        Assert.True(System.IO.File.Exists(other));
        Assert.True(System.IO.File.Exists(nested));
    }

    [Theory]
    [InlineData("--clean-junk")]
    [InlineData("--clean-junk", "no-existe", @"C:\Temp\strataui-clean-1.txt")]
    [InlineData("--clean-junk", "temp", @"C:\Windows\System32\strataui-clean-1.txt")]
    [InlineData("--clean-junk", "temp", @"C:\Temp\otro.txt")]
    public void Elevated_command_rejects_bad_arguments(params string[] args) =>
        Assert.Equal(2, JunkCleaner.RunElevated(args));

    [Fact]
    public void Every_category_has_targets_and_text()
    {
        foreach (var category in JunkCleaner.Categories)
        {
            Assert.NotEmpty(category.Name);
            Assert.NotEmpty(category.Description);
            Assert.NotNull(category.Targets());
        }
        // Lo que tiene costo no viene marcado.
        Assert.False(JunkCleaner.Find("browsers")!.DefaultOn);
        Assert.False(JunkCleaner.Find("shaders")!.DefaultOn);
    }
}
