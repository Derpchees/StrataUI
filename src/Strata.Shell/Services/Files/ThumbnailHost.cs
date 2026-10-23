using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Carátulas de Windows (fotograma de un video, portada de un disco, primera página de un PDF o de un
/// documento de Office) en un proceso aparte (<c>StrataUI.exe --thumbnails</c>): las genera el
/// manejador de miniaturas de cada tipo, que se carga dentro de quien las pide; si uno falla, solo
/// se cae ese proceso (y se vuelve a abrir). Se le piden por la entrada estándar, una ruta por línea,
/// y contesta con un PNG en la carpeta temporal.
/// </summary>
public static class ThumbnailHost
{
    public const string Arg = "--thumbnails";

    private static readonly object Gate = new();
    private static Process? _process;
    private static int _restarts;
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "strataui-thumbs");

    /// <summary>
    /// Carátula de un archivo (null si su tipo no tiene). Bloquea: llamar desde el hilo de los íconos.
    /// <paramref name="cachedOnly"/>: solo la que Windows ya tenga guardada (un archivo solo en línea
    /// no se descarga para hacerla).
    /// </summary>
    public static ImageSource? Get(string path, int size, bool cachedOnly)
    {
        lock (Gate)
        {
            if (!EnsureProcess())
                return null;
            try
            {
                _process!.StandardInput.WriteLine($"{size}\t{(cachedOnly ? 1 : 0)}\t{path}");
                _process.StandardInput.Flush();
                // Una carátula de un video grande puede tardar; si no contesta, se reinicia.
                var line = _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(line))
                    return null;
                var image = AppIcons.Load(line, size);
                TryDelete(line);
                return image;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                Log.Info($"Carátulas: el proceso no respondió ({ex.Message.Trim()}); se reinicia");
                Kill();
                return null;
            }
        }
    }

    private static bool EnsureProcess()
    {
        if (_process is { HasExited: false })
            return true;
        if (_restarts++ > 5)
            return false; // algo falla siempre: sin carátulas en esta sesión
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                StandardInputEncoding = new System.Text.UTF8Encoding(false),
                StandardOutputEncoding = new System.Text.UTF8Encoding(false),
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(Arg);
            _process = Process.Start(start);
            return _process != null;
        }
        catch (Exception ex)
        {
            Log.Error("Carátulas: no se pudo abrir el proceso", ex);
            return false;
        }
    }

    private static void Kill()
    {
        try
        {
            _process?.Kill();
        }
        catch (Exception)
        {
        }
        _process?.Dispose();
        _process = null;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
    }

    // ── Proceso aparte ──────────────────────────────────────────────────────

    /// <summary>Atiende pedidos hasta que se cierra la entrada (al cerrarse Strata UI).</summary>
    internal static int Run()
    {
        Directory.CreateDirectory(Folder);
        // UTF-8 en los dos sentidos (rutas con acentos); el lector salta la marca BOM si la hay.
        using var input = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false), true);
        using var output = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        string? request;
        while ((request = input.ReadLine()) != null)
        {
            string answer = "";
            try
            {
                var parts = request.Split('\t', 3);
                if (parts.Length == 3 && int.TryParse(parts[0], out int size) && size is > 0 and <= 512
                    && Path.IsPathFullyQualified(parts[2]) && File.Exists(parts[2]))
                {
                    answer = Thumbnail(parts[2], size, parts[1] == "1") ?? "";
                }
            }
            catch (Exception ex)
            {
                // Un archivo que su manejador no sabe leer: sin carátula.
                Log.Info($"Carátulas: {request} ({ex.GetType().Name}: {ex.Message.Trim()})");
            }
            output.WriteLine(answer);
        }
        return 0;
    }

    /// <summary>La carátula como PNG en la carpeta temporal; null si el tipo no tiene.</summary>
    private static string? Thumbnail(string path, int size, bool cachedOnly)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        int created = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
        if (created < 0 || item is not IShellItemImageFactory factory)
        {
            Log.Info($"Carátulas: {Path.GetFileName(path)} sin elemento del Shell (0x{created:X8}, {item?.GetType().Name ?? "null"})");
            return null;
        }
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            uint flags = ThumbnailOnly | BiggerSizeOk | (cachedOnly ? InCacheOnly : 0);
            int hr = factory.GetImage(new NativeSize { Width = size, Height = size }, flags, out bitmap);
            if (hr < 0 || bitmap == IntPtr.Zero)
            {
                Log.Info($"Carátulas: {Path.GetFileName(path)} sin miniatura (0x{hr:X8})");
                return null;
            }
            var source = FromHBitmap(bitmap);
            if (source is null)
            {
                Log.Info($"Carátulas: {Path.GetFileName(path)}: no se pudo leer el mapa de bits (error {Marshal.GetLastWin32Error()})");
                return null;
            }
            string file = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".png");
            using (var stream = File.Create(file))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(stream);
            }
            return file;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>
    /// El mapa de bits con su transparencia (los de miniaturas son de 32 bits); si no trae canal alfa
    /// (todo en cero), opaco.
    /// </summary>
    private static BitmapSource? FromHBitmap(IntPtr bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out var info) == 0 || info.Width <= 0 || info.Height <= 0)
            return null;
        int width = info.Width, height = info.Height, stride = width * 4;
        var pixels = new byte[stride * height];
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // de arriba hacia abajo
            Planes = 1,
            BitCount = 32,
        };
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) == 0)
                return null;
        }
        finally
        {
            DeleteDC(dc);
        }
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length && !hasAlpha; i += 4)
            hasAlpha = pixels[i] != 0;
        var source = BitmapSource.Create(width, height, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private const uint BiggerSizeOk = 0x1, ThumbnailOnly = 0x8, InCacheOnly = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
                                                          [MarshalAs(UnmanagedType.Interface)] out object item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
}
