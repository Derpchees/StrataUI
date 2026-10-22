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
    [InlineData(@"C:\docs\factura.pdf", MediaKind.None)]
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
