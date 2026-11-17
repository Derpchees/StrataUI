using Strata.Shell.Services.StartMenu;

namespace Strata.Tests;

/// <summary>Búsqueda de archivos del menú Inicio: el texto va literal en LIKE y se descartan las copias de compilación.</summary>
public sealed class FileSearchTests
{
    [Theory]
    [InlineData("plan", "plan")]
    [InlineData("50%_[a]", "50[%][_][[]a]")]
    [InlineData("O'Neil", "O''Neil")]
    public void EscapesLike(string query, string expected) => Assert.Equal(expected, FileSearch.Like(query));

    [Theory]
    [InlineData(@"C:\Users\a\Documents\proyecto\bin\Debug\app.dll", true)]
    [InlineData(@"C:\Users\a\Documents\web\node_modules\x\index.js", true)]
    [InlineData(@"C:\Users\a\Documents\binario.txt", false)]
    [InlineData(@"C:\Users\a\Downloads\informe.pdf", false)]
    public void SkipsBuildOutput(string path, bool skipped) => Assert.Equal(skipped, FileSearch.IsBuildOutput(path));
}
