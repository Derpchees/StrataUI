using System.IO;
using System.Text;
using Strata.Shell.Services.Notepad;
using Xunit;

namespace Strata.Tests;

public class TextFileTests
{
    [Fact]
    public void Utf8_sin_BOM_con_LF()
    {
        var content = TextFile.Decode(Encoding.UTF8.GetBytes("año\nñu"));
        Assert.Equal(TextEncodingKind.Utf8, content.Encoding);
        Assert.Equal(LineEnding.Lf, content.LineEnding);
        Assert.Equal("año\r\nñu", content.Text);
    }

    [Fact]
    public void Utf8_con_BOM()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("hola\r\n")).ToArray();
        var content = TextFile.Decode(bytes);
        Assert.Equal(TextEncodingKind.Utf8Bom, content.Encoding);
        Assert.Equal(LineEnding.CrLf, content.LineEnding);
        Assert.Equal("hola\r\n", content.Text);
    }

    [Theory]
    [InlineData(TextEncodingKind.Utf16Le)]
    [InlineData(TextEncodingKind.Utf16Be)]
    public void Utf16_ida_y_vuelta(TextEncodingKind kind)
    {
        var bytes = TextFile.Encode("línea 1\r\nlínea 2", kind, LineEnding.CrLf);
        var content = TextFile.Decode(bytes);
        Assert.Equal(kind, content.Encoding);
        Assert.Equal("línea 1\r\nlínea 2", content.Text);
    }

    [Fact]
    public void Ansi_cuando_no_es_utf8_valido()
    {
        var bytes = TextFile.Ansi.GetBytes("canción");
        var content = TextFile.Decode(bytes);
        Assert.Equal(TextEncodingKind.Ansi, content.Encoding);
        Assert.Equal("canción", content.Text);
    }

    [Fact]
    public void Guarda_con_el_salto_de_linea_elegido()
    {
        var bytes = TextFile.Encode("a\r\nb\r\n", TextEncodingKind.Utf8, LineEnding.Lf);
        Assert.Equal("a\nb\n", Encoding.UTF8.GetString(bytes));
        bytes = TextFile.Encode("a\r\nb", TextEncodingKind.Utf8, LineEnding.Cr);
        Assert.Equal("a\rb", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Saltos_mezclados_se_normalizan()
    {
        Assert.Equal("a\r\nb\r\nc\r\nd", TextFile.NormalizeLineEndings("a\nb\rc\r\nd"));
    }

    [Fact]
    public void Detecta_binarios()
    {
        Assert.True(TextFile.LooksBinary(new byte[] { 0x4D, 0x5A, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00 }));
        Assert.False(TextFile.LooksBinary(Encoding.UTF8.GetBytes("texto normal\r\n")));
        Assert.False(TextFile.LooksBinary(TextFile.Encode("texto", TextEncodingKind.Utf16Le, LineEnding.CrLf)));
    }

    [Fact]
    public void FitsAnsi_detecta_emoji()
    {
        Assert.True(TextFile.FitsAnsi("acción"));
        Assert.False(TextFile.FitsAnsi("hola 🙂"));
    }

    [Theory]
    [InlineData(@"C:\a\notas.txt", true)]
    [InlineData(@"C:\a\app.LOG", true)]
    [InlineData(@"C:\a\.gitignore", true)]
    [InlineData(@"C:\a\foto.png", false)]
    [InlineData(@"C:\a\programa.exe", false)]
    public void Reconoce_extensiones(string path, bool expected) => Assert.Equal(expected, TextFile.IsTextFile(path));

    [Fact]
    public void Escribe_y_lee_el_archivo()
    {
        string path = Path.Combine(Path.GetTempPath(), $"strata-notepad-{Guid.NewGuid():N}.txt");
        try
        {
            TextFile.Write(path, "uno\r\ndos", TextEncodingKind.Utf8Bom, LineEnding.Lf);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            // Sobrescribir (reemplazo atómico) conserva el contenido nuevo.
            TextFile.Write(path, "tres", TextEncodingKind.Utf8, LineEnding.CrLf);
            var content = TextFile.Read(path);
            Assert.Equal("tres", content.Text);
            Assert.Equal(TextEncodingKind.Utf8, content.Encoding);
            Assert.False(File.Exists(path + ".strata-tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
