using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Íconos (48 px, la lista grande del sistema) y nombres de tipo de los archivos. Los íconos se leen
/// en un hilo STA aparte, en orden y sin bloquear la ventana; los de tipos comunes se guardan por
/// extensión. Solo se usa SHGetFileInfo (como el resto de Strata UI): nada de extensiones de miniaturas
/// del Shell dentro del proceso (ver los diálogos de archivos en CLAUDE.md). Las miniaturas de
/// imágenes las decodifica WPF.
/// </summary>
public static class FileIcons
{
    /// <summary>Tipos con un ícono propio por archivo (programas, accesos directos, íconos).</summary>
    private static readonly HashSet<string> PerFile = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".url", ".msi", ".cpl", ".scr", ".appref-ms", ".cur", ".ani",
    };

    /// <summary>Imágenes que WPF sabe decodificar (con los códecs de Windows: también webp y heic).</summary>
    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".jfif",
    };

    private const long MaxThumbnailBytes = 40L << 20;

    private static readonly ConcurrentDictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<Action> Jobs = new();
    private static Thread? _worker;
    private static IntPtr _imageList;

    /// <summary>Carga los íconos (y, en la cuadrícula, las miniaturas) de una carpeta hasta que se cancele.</summary>
    public static void Load(IReadOnlyList<FileEntry> entries, bool thumbnails, Dispatcher ui, CancellationToken token)
    {
        EnsureWorker();
        Jobs.Add(() =>
        {
            var batch = new List<(FileEntry, ImageSource?, ImageSource?)>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var entry in entries)
            {
                if (token.IsCancellationRequested)
                    return;
                var icon = entry.Icon ?? IconOf(entry);
                ImageSource? thumbnail = null;
                if (thumbnails && entry.Thumbnail is null && IsPicture(entry))
                    thumbnail = AppIcons.Load(entry.FullPath, 192);
                batch.Add((entry, icon, thumbnail));
                // Se entregan de a poco: lo visible aparece enseguida y la ventana no se traba.
                if (watch.ElapsedMilliseconds > 60)
                {
                    Deliver(batch, ui, token);
                    batch = [];
                    watch.Restart();
                }
            }
            Deliver(batch, ui, token);
        });
    }

    /// <summary>Corre algo en el hilo de los íconos (lo que usa <see cref="IconOfPath"/> o <see cref="DisplayName"/>).</summary>
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        EnsureWorker();
        var result = new TaskCompletionSource<T>();
        Jobs.Add(() =>
        {
            try
            {
                result.SetResult(work());
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        });
        return result.Task;
    }

    public static bool IsPicture(FileEntry entry) =>
        !entry.IsFolder && Pictures.Contains(entry.Extension) && entry.Size is > 0 and < MaxThumbnailBytes;

    private static void Deliver(List<(FileEntry Entry, ImageSource? Icon, ImageSource? Thumbnail)> batch, Dispatcher ui, CancellationToken token)
    {
        if (batch.Count == 0)
            return;
        ui.BeginInvoke(() =>
        {
            if (token.IsCancellationRequested)
                return;
            foreach (var (entry, icon, thumbnail) in batch)
            {
                if (entry.Icon is null)
                    entry.Icon = icon;
                if (thumbnail != null)
                    entry.Thumbnail = thumbnail;
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>Ícono de cualquier ruta (unidades, carpetas especiales) en el hilo que llama.</summary>
    public static ImageSource? IconOfPath(string path, bool folder)
    {
        var info = new ShFileInfo();
        uint flags = ShgfiSysIconIndex;
        if (SHGetFileInfo(path, folder ? AttributeDirectory : AttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == IntPtr.Zero)
            return null;
        return FromImageList(info.IconIndex);
    }

    private static ImageSource? IconOf(FileEntry entry)
    {
        // Las carpetas con desktop.ini (solo lectura o de sistema) pueden tener su propio ícono.
        bool own = entry.IsFolder
            ? (entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0
            : PerFile.Contains(entry.Extension);
        string key = own ? entry.FullPath : entry.IsFolder ? "<carpeta>" : entry.Extension is { Length: > 0 } ext ? ext : "<sin extensión>";
        if (Icons.TryGetValue(key, out var cached))
            return cached;
        var info = new ShFileInfo();
        uint flags = ShgfiSysIconIndex | (own ? 0 : ShgfiUseFileAttributes);
        string query = own ? entry.FullPath : entry.IsFolder ? "carpeta" : "archivo" + entry.Extension;
        ImageSource? icon = null;
        if (SHGetFileInfo(query, entry.IsFolder ? AttributeDirectory : AttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) != IntPtr.Zero)
            icon = FromImageList(info.IconIndex);
        Icons[key] = icon;
        return icon;
    }

    /// <summary>Nombre del tipo ("Documento de texto", "Carpeta de archivos"), por extensión.</summary>
    public static string TypeName(string extension, bool folder)
    {
        string key = folder ? "<carpeta>" : extension;
        if (TypeNames.TryGetValue(key, out var cached))
            return cached;
        var info = new ShFileInfo();
        string name = SHGetFileInfo(folder ? "carpeta" : "archivo" + extension, folder ? AttributeDirectory : AttributeNormal,
                                    ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiTypeName | ShgfiUseFileAttributes) != IntPtr.Zero
                      && !string.IsNullOrEmpty(info.TypeName)
            ? info.TypeName
            : extension.TrimStart('.').ToUpperInvariant();
        TypeNames[key] = name;
        return name;
    }

    /// <summary>Nombre que muestra Windows ("Windows (C:)", "USB (E:)").</summary>
    public static string DisplayName(string path)
    {
        var info = new ShFileInfo();
        return SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiDisplayName) != IntPtr.Zero
               && !string.IsNullOrEmpty(info.DisplayName)
            ? info.DisplayName
            : path;
    }

    private static ImageSource? FromImageList(int index)
    {
        if (_imageList == IntPtr.Zero)
        {
            var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList (sirve como HIMAGELIST)
            if (SHGetImageList(ShilExtraLarge, ref iid, out _imageList) != 0)
                return null;
        }
        IntPtr hIcon = ImageList_GetIcon(_imageList, index, IldTransparent);
        if (hIcon == IntPtr.Zero)
            return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static void EnsureWorker()
    {
        if (_worker != null)
            return;
        _worker = new Thread(() =>
        {
            foreach (var job in Jobs.GetConsumingEnumerable())
            {
                try
                {
                    job();
                }
                catch (Exception ex)
                {
                    Log.Error("Archivos: no se pudieron leer los íconos", ex);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Strata UI: íconos de archivos",
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    private const uint ShgfiSysIconIndex = 0x4000, ShgfiUseFileAttributes = 0x10, ShgfiTypeName = 0x400, ShgfiDisplayName = 0x200;
    private const uint AttributeNormal = 0x80, AttributeDirectory = 0x10;
    private const int ShilExtraLarge = 2;
    private const uint IldTransparent = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int list, ref Guid iid, out IntPtr imageList);

    [DllImport("comctl32.dll")]
    private static extern IntPtr ImageList_GetIcon(IntPtr imageList, int index, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
