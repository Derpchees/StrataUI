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

    /// <summary>Algo de la Papelera: su nombre de antes; la fecha es la de borrado y el "tipo", la carpeta de donde salió.</summary>
    public FileEntry(RecycledItem item)
    {
        Name = item.Name;
        FullPath = item.DataPath;
        IsFolder = item.IsFolder;
        Size = item.IsFolder ? -1 : item.Size;
        Modified = item.Deleted;
        Attributes = item.IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
        TypeName = item.OriginalFolder;
        Recycled = item;
    }

    /// <summary>Algo de adentro de un comprimido: su ruta es la del comprimido más la de adentro ("C:\a.zip\fotos\1.jpg").</summary>
    public FileEntry(ArchiveListing.Child child, string archive, DateTime archiveDate, string typeName)
    {
        Name = child.Name;
        FullPath = Path.Combine(archive, child.Path);
        IsFolder = child.IsFolder;
        Size = child.IsFolder ? -1 : child.Item?.Size ?? 0;
        Modified = child.Item?.Modified ?? archiveDate;
        Attributes = child.IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
        TypeName = typeName;
        ArchivePath = child.Path;
    }

    /// <summary>Su ruta dentro del comprimido (null si es un archivo normal).</summary>
    public string? ArchivePath { get; }
    public bool InArchive => ArchivePath != null;

    /// <summary>Si está en la Papelera, de dónde salió (para restaurarlo).</summary>
    public RecycledItem? Recycled { get; }

    /// <summary>Lo encontró la búsqueda en una subcarpeta (no es de la carpeta que se ve).</summary>
    public bool FromSearch { get; init; }

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

    /// <summary>Lo que leen la automatización y los lectores de pantalla de cada fila (si no, el nombre de la clase).</summary>
    public override string ToString() => Name;

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

    /// <summary>Ya se buscó su carátula (tenga o no): no se vuelve a pedir.</summary>
    public bool ThumbnailTried { get; set; }
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
