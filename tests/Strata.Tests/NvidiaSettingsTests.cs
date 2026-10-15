using System.IO;
using Strata.Shell.Services.Vendors;

namespace Strata.Tests;

/// <summary>El proceso aparte de NVIDIA valida sus argumentos (y, si hay tarjeta NVIDIA, solo lee).</summary>
public sealed class NvidiaSettingsTests
{
    [Theory]
    [InlineData("--nvidia")]
    [InlineData("--nvidia", "set", "12345=1")]
    [InlineData("--nvidia", "set", "274066289=abc")]
    [InlineData("--nvidia", "get", "274066289", @"C:\Windows\x.txt")]
    [InlineData("--nvidia", "borrar", "274066289")]
    public void Bad_arguments_are_rejected(params string[] args) =>
        Assert.Equal(2, NvidiaSettings.RunHelper(args));

    [Fact]
    public void Reading_writes_one_line_per_setting()
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll")))
            return; // sin tarjeta NVIDIA no hay nada que leer
        var file = Path.Combine(Path.GetTempPath(), $"strataui-nvidia-{Guid.NewGuid():N}.txt");
        try
        {
            var ids = string.Join(',', NvidiaSettings.All.Select(s => s.Id));
            Assert.Equal(0, NvidiaSettings.RunHelper(["--nvidia", "get", ids, file]));
            Assert.Equal(NvidiaSettings.All.Count, File.ReadAllLines(file).Length);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
