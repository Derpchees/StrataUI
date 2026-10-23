using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files;

/// <summary>Estado de un archivo en una carpeta de la nube (OneDrive, Google Drive…).</summary>
public enum CloudState { None, OnlineOnly, Local, Pinned }

/// <summary>Un archivo o carpeta del explorador de Strata UI. El ícono y la miniatura llegan después.</summary>
public sealed class FileEntry : INotifyPropertyChanged
{
    public FileEntry(FileSystemInfo info, string typeName, bool inCloud = false)
    {
        Name = info.Name;
        FullPath = info.FullName;
        IsFolder = info is DirectoryInfo;
        Size = info is FileInfo file ? file.Length : -1;
        Modified = info.LastWriteTime;
        Attributes = info.Attributes;
        TypeName = typeName;
        Cloud = CloudOf(Attributes, inCloud);
    }

    /// <summary>Una carpeta compartida de un servidor (\\servidor\recurso): sin fecha ni tamaño.</summary>
    public FileEntry(string name, string path, string typeName)
    {
        Name = name;
        FullPath = path;
        IsFolder = true;
        Size = -1;
        Attributes = FileAttributes.Directory;
        TypeName = typeName;
    }

    // Atributos de los archivos de la nube (los mismos que mira el Explorador).
    private const FileAttributes Pinned = (FileAttributes)0x80000, RecallOnOpen = (FileAttributes)0x40000,
                                 RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>
    /// Solo en línea si leerlo lo descargaría; "siempre en este dispositivo" si está fijado; si no, y
    /// la carpeta es de la nube, disponible en el equipo.
    /// </summary>
    internal static CloudState CloudOf(FileAttributes attributes, bool inCloud) =>
        (attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0 ? CloudState.OnlineOnly
        : (attributes & Pinned) != 0 ? CloudState.Pinned
        : inCloud ? CloudState.Local
        : CloudState.None;

    public CloudState Cloud { get; }
    /// <summary>Leerlo lo descargaría: no se leen su miniatura ni su ícono propio.</summary>
    public bool IsOnlineOnly => Cloud == CloudState.OnlineOnly;
    public bool HasCloudState => Cloud != CloudState.None;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsFolder { get; }
    /// <summary>Tamaño en bytes; -1 en las carpetas.</summary>
    public long Size { get; }
    public DateTime Modified { get; }
    public FileAttributes Attributes { get; }
    public string TypeName { get; }
    public string Extension => IsFolder ? "" : Path.GetExtension(Name);
    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
    /// <summary>Video: su carátula lleva el botón de reproducir.</summary>
    public bool IsVideo => !IsFolder && MediaKinds.Of(Name) == MediaKind.Video;

    /// <summary>Mostrar las extensiones (".pdf") en los nombres; lo cambia el botón de Archivos.</summary>
    public static bool ShowExtensions { get; set; } = true;

    /// <summary>El nombre como se ve: sin extensión si se ocultan (los accesos directos nunca la muestran).</summary>
    public string DisplayName =>
        IsFolder || (ShowExtensions && !Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                     && !Extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            ? Name
            : Path.GetFileNameWithoutExtension(Name);

    /// <summary>Cambió <see cref="ShowExtensions"/>: el nombre se vuelve a mostrar.</summary>
    public void RefreshName() => Changed(nameof(DisplayName));

    public string SizeText => IsFolder ? "" : FormatSize(Size);
    public string ModifiedText => Modified == default ? "" : Modified.ToString("g", CultureInfo.CurrentCulture);

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; Changed(); Changed(nameof(Preview)); }
    }

    private ImageSource? _thumbnail;
    /// <summary>Miniatura de una imagen (solo en la cuadrícula).</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; Changed(); Changed(nameof(Preview)); Changed(nameof(HasThumbnail)); }
    }

    public bool HasThumbnail => _thumbnail != null;
    public ImageSource? Preview => _thumbnail ?? _icon;

    private bool _isCut;
    /// <summary>Está en el portapapeles para moverse: se ve tenue, como en el Explorador.</summary>
    public bool IsCut
    {
        get => _isCut;
        set { _isCut = value; Changed(); }
    }

    private bool _isRenaming;
    public bool IsRenaming
    {
        get => _isRenaming;
        set { _isRenaming = value; Changed(); }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 40 => Loc.F("{0:0.0} TB", bytes / (double)(1L << 40)),
        >= 1L << 30 => Loc.F("{0:0.0} GB", bytes / (double)(1L << 30)),
        >= 1L << 20 => Loc.F("{0:0} MB", bytes / (double)(1L << 20)),
        // Como el Explorador: un archivo pequeño dice 1 KB, no 0.
        _ => Loc.F("{0:0} KB", Math.Ceiling(bytes / 1024.0)),
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
