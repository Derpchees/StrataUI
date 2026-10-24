using System.IO;
using Strata.Shell.Services.Files;
using Xunit;

namespace Strata.Tests;

/// <summary>Archivos: qué abre el visor, estado en la nube y rutas de red.</summary>
public class FilesTests
{
    [Theory]
    [InlineData(@"C:\fotos\IMG_0001.JPG", MediaKind.Image)]
    [InlineData(@"C:\fotos\captura.webp", MediaKind.Image)]
    [InlineData(@"C:\videos\viaje.mkv", MediaKind.Video)]
    [InlineData(@"C:\videos\clip.MP4", MediaKind.Video)]
    [InlineData(@"C:\música\canción.flac", MediaKind.Audio)]
    [InlineData(@"C:\docs\informe.docx", MediaKind.None)]
    [InlineData(@"C:\docs\sin extensión", MediaKind.None)]
    public void Media_kind_by_extension(string path, MediaKind expected) =>
        Assert.Equal(expected, MediaKinds.Of(path));

    [Theory]
    [InlineData(0x400000, true, CloudState.OnlineOnly)]   // RecallOnDataAccess (OneDrive "solo en línea")
    [InlineData(0x401000, false, CloudState.OnlineOnly)]  // también fuera de una carpeta de la nube conocida
    [InlineData(0x80020, true, CloudState.Pinned)]        // Pinned + Archive
    [InlineData(0x20, true, CloudState.Local)]
    [InlineData(0x20, false, CloudState.None)]
    public void Cloud_state_from_attributes(int attributes, bool inCloud, CloudState expected) =>
        Assert.Equal(expected, FileEntry.CloudOf((FileAttributes)attributes, inCloud));

    [Theory]
    [InlineData(@"C:\docs\factura.PDF", MediaKind.None)]
    [InlineData(@"C:\fotos\IMG_0001.CR3", MediaKind.Image)]
    [InlineData(@"C:\logos\ACTUS BLACK.svg", MediaKind.Image)]
    [InlineData(@"C:\videos\clip.webm", MediaKind.Video)]
    [InlineData(@"C:\música\voz.opus", MediaKind.Audio)]
    public void New_media_kinds(string path, MediaKind expected) =>
        Assert.Equal(expected, MediaKinds.Of(path));

    /// <summary>Corre en un hilo STA, como el de los íconos (WPF lo pide para dibujar).</summary>
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
        return result;
    }

    [Theory]
    [InlineData("C:", @"C:\")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData("d:", @"D:\")]
    [InlineData(@"C:\Windows\", @"C:\Windows")]
    public void Drive_root_is_not_the_current_folder(string input, string expected) =>
        Assert.Equal(expected, Places.Normalize(input));

    [Fact]
    public void Svg_is_drawn_at_the_requested_size()
    {
        string svg = Path.Combine(Path.GetTempPath(), $"strata-{Guid.NewGuid():N}.svg");
        File.WriteAllText(svg, """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100" viewBox="0 0 200 100">
              <rect width="200" height="100" fill="#3f6fdc"/>
            </svg>
            """);
        try
        {
            var size = OnSta(() => Previews.Svg(svg, 192) is { } bitmap ? (bitmap.PixelWidth, bitmap.PixelHeight) : (0, 0));
            Assert.Equal(192, size.Item1);
            Assert.InRange(size.Item2, 90, 100);
        }
        finally
        {
            File.Delete(svg);
        }
    }

    [Fact]
    public void Animated_gif_keeps_every_frame()
    {
        string gif = Path.Combine(Path.GetTempPath(), $"strata-{Guid.NewGuid():N}.gif");
        OnSta(() =>
        {
            var encoder = new System.Windows.Media.Imaging.GifBitmapEncoder();
            foreach (var color in new[] { System.Windows.Media.Colors.Red, System.Windows.Media.Colors.Blue, System.Windows.Media.Colors.Green })
            {
                var pixels = Enumerable.Repeat(new[] { color.B, color.G, color.R, (byte)255 }, 16 * 16).SelectMany(p => p).ToArray();
                var frame = System.Windows.Media.Imaging.BitmapSource.Create(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 64);
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));
            }
            using var stream = File.Create(gif);
            encoder.Save(stream);
            return 0;
        });
        try
        {
            var frames = OnSta(() => Previews.Gif(gif)?.Frames.Count ?? 0);
            Assert.Equal(3, frames);
        }
        finally
        {
            File.Delete(gif);
        }
    }

    [Fact]
    public void Recycle_bin_info_file_version_2_is_read()
    {
        const string original = @"C:\Users\ana\Documents\factura.pdf";
        var when = new DateTime(2026, 10, 22, 12, 30, 0, DateTimeKind.Local);
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(2L));
        data.AddRange(BitConverter.GetBytes(4096L));
        data.AddRange(BitConverter.GetBytes(when.ToFileTime()));
        data.AddRange(BitConverter.GetBytes(original.Length + 1));
        data.AddRange(System.Text.Encoding.Unicode.GetBytes(original + "\0"));

        var parsed = RecycleBin.Parse(data.ToArray());

        Assert.NotNull(parsed);
        Assert.Equal(original, parsed.Value.Path);
        Assert.Equal(4096L, parsed.Value.Size);
        Assert.Equal(when, parsed.Value.Deleted);
        Assert.Null(RecycleBin.Parse(new byte[10]));
    }

    [Theory]
    [InlineData(@"\\nas", true)]
    [InlineData(@"\\nas\fotos", false)]
    [InlineData(@"C:\", false)]
    [InlineData(@"\\", false)]
    public void Server_paths_list_their_shares(string path, bool expected) =>
        Assert.Equal(expected, Places.IsServer(path));

    [Theory]
    [InlineData(@"\\nas\fotos\2026\enero", @"\\nas\fotos")]
    [InlineData(@"\\nas\fotos", @"\\nas\fotos")]
    [InlineData(@"\\nas", null)]
    [InlineData(@"C:\Users", null)]
    public void Share_root_of_network_paths(string path, string? expected) =>
        Assert.Equal(expected, Places.ShareRoot(path));
}
