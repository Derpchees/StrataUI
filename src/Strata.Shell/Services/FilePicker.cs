using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Strata.Shell.Services;

/// <summary>
/// Diálogos de abrir archivo o carpeta en un proceso aparte (<c>StrataUI.exe --pick</c>). Dentro del
/// proceso de Strata UI el diálogo de Windows puede tumbarlo (pasó: corrupción del montón en ntdll al
/// abrirlo desde el menú Inicio): carga extensiones del shell y le habla a la barra, que aquí es
/// la de Strata UI. Si el proceso aparte falla, solo se pierde el diálogo.
/// </summary>
public static class FilePicker
{
    public const string Arg = "--pick";

    /// <summary>Archivos elegidos (vacío si se canceló).</summary>
    /// <param name="filter">Filtro de WPF ("Imágenes|*.png;*.jpg"), o null para todos.</param>
    /// <param name="keepLinks">Devuelve el acceso directo elegido en lugar de su destino.</param>
    public static Task<string[]> PickFilesAsync(string title, string? filter = null, bool multiselect = false, bool keepLinks = false) =>
        RunAsync("file", title, filter ?? "", (multiselect ? "m" : "") + (keepLinks ? "l" : ""));

    /// <summary>Carpetas elegidas (vacío si se canceló).</summary>
    public static Task<string[]> PickFoldersAsync(string title, bool multiselect = false) =>
        RunAsync("folder", title, "", multiselect ? "m" : "");

    private static async Task<string[]> RunAsync(string kind, string title, string filter, string flags)
    {
        var result = Path.Combine(Path.GetTempPath(), $"strataui-pick-{Guid.NewGuid():N}.txt");
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var arg in new[] { Arg, kind, result, title, filter, flags })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null)
                return [];
            await process.WaitForExitAsync();
            return File.Exists(result)
                ? File.ReadAllLines(result).Where(p => p.Length > 0).ToArray()
                : [];
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo abrir el diálogo de archivos", ex);
            return [];
        }
        finally
        {
            try
            {
                File.Delete(result);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Proceso aparte: muestra el diálogo y escribe lo elegido, una ruta por línea, en el archivo
    /// indicado (dentro de la carpeta temporal; nada más).
    /// </summary>
    internal static int RunPicker(string[] args)
    {
        if (args.Length < 6 || args[1] is not ("file" or "folder"))
            return 2;
        var result = Path.GetFullPath(args[2]);
        if (!result.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(result).StartsWith("strataui-pick-", StringComparison.Ordinal))
            return 2;
        string title = args[3], filter = args[4], flags = args[5];
        bool multiselect = flags.Contains('m');

        string[] paths = [];
        if (args[1] == "file")
        {
            var dialog = new OpenFileDialog { Title = title, Multiselect = multiselect, DereferenceLinks = !flags.Contains('l') };
            if (filter.Length > 0)
                dialog.Filter = filter;
            if (dialog.ShowDialog() == true)
                paths = dialog.FileNames;
        }
        else
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = multiselect };
            if (dialog.ShowDialog() == true)
                paths = dialog.FolderNames;
        }
        File.WriteAllLines(result, paths);
        return 0;
    }
}
