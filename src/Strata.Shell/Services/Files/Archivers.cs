using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Strata.Shell.Services.Files;

/// <summary>Un compresor instalado: su programa para abrir comprimidos y, si tiene, el de extraer con progreso.</summary>
public sealed record Archiver(string Name, string Manager, string? Extractor);

/// <summary>
/// Comprimidos en Archivos: se abren con el compresor instalado (NanaZip, 7-Zip, WinRAR, PeaZip,
/// Bandizip) y no con la carpeta comprimida de Windows (que abría el Explorador). Sin compresor, un
/// .zip se extrae con .NET y lo demás se abre con su app.
/// </summary>
public static class Archivers
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".zst", ".lz", ".lzma",
        ".cab", ".iso", ".wim", ".arj", ".lzh", ".z", ".001",
    };

    private static Archiver? _found;
    private static bool _searched;

    public static bool IsArchive(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>El primero que esté instalado (se busca una vez).</summary>
    public static Archiver? Installed
    {
        get
        {
            if (!_searched)
            {
                _found = Find();
                _searched = true;
            }
            return _found;
        }
    }

    private static Archiver? Find()
    {
        // NanaZip (de la Tienda) deja sus alias de ejecución en WindowsApps.
        string aliases = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        if (File.Exists(Path.Combine(aliases, "NanaZip.exe")))
        {
            string extractor = Path.Combine(aliases, "NanaZipG.exe");
            return new Archiver("NanaZip", Path.Combine(aliases, "NanaZip.exe"), File.Exists(extractor) ? extractor : null);
        }
        if (AppPath("7zFM.exe") is { } sevenZip)
        {
            string extractor = Path.Combine(Path.GetDirectoryName(sevenZip)!, "7zG.exe");
            return new Archiver("7-Zip", sevenZip, File.Exists(extractor) ? extractor : null);
        }
        if (AppPath("WinRAR.exe") is { } winRar)
            return new Archiver("WinRAR", winRar, null);
        if (AppPath("PeaZip.exe") is { } peaZip)
            return new Archiver("PeaZip", peaZip, null);
        if (AppPath("Bandizip.exe") is { } bandizip)
            return new Archiver("Bandizip", bandizip, null);
        return null;
    }

    private static string? AppPath(string exe)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"')))
                return path.Trim('"');
        }
        return null;
    }

    /// <summary>Abre el comprimido con el compresor; false si no hay ninguno.</summary>
    public static bool Open(string archive)
    {
        if (Installed is not { } archiver)
            return false;
        return Start(archiver.Manager, [archive]);
    }

    /// <summary>
    /// Extrae en una carpeta nueva junto al comprimido, con el nombre del comprimido ("fotos (2)" si
    /// ya existe). Con el compresor (y su ventana de progreso) o, para un .zip sin compresor, con .NET.
    /// </summary>
    public static async Task<string?> ExtractAsync(string archive)
    {
        string parent = Path.GetDirectoryName(archive)!;
        string name = Path.GetFileNameWithoutExtension(archive);
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name);
        string target = Path.Combine(parent, name);
        for (int i = 2; Directory.Exists(target) || File.Exists(target); i++)
            target = Path.Combine(parent, $"{name} ({i})");

        if (Installed is { Extractor: { } extractor })
            return Start(extractor, ["x", archive, "-o" + target, "-y"], parent) ? target : null;
        if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(archive, target));
            return target;
        }
        // Un compresor sin extractor de línea de comandos (WinRAR, PeaZip…): se abre y se extrae ahí.
        Open(archive);
        return null;
    }

    private static bool Start(string exe, string[] arguments, string? folder = null)
    {
        try
        {
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = folder ?? Path.GetDirectoryName(arguments[^1]) ?? "",
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Archivos: no se pudo abrir {Path.GetFileName(exe)}", ex);
            return false;
        }
    }
}
