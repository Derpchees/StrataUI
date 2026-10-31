using System.IO;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Files;

/// <summary>
/// Lo último que se hizo en Archivos, para deshacerlo con Ctrl+Z (como el Explorador): cambiar el
/// nombre, mover, copiar, crear una carpeta o enviar a la Papelera. Es uno para todas las ventanas.
/// </summary>
internal static class FileUndo
{
    private sealed record Step(string Text, Func<IntPtr, Task<bool>> Undo);

    private const int Max = 30;
    private static readonly LinkedList<Step> Steps = new();
    private static bool _busy;

    private static void Push(string text, Func<IntPtr, Task<bool>> undo)
    {
        Steps.AddLast(new Step(text, undo));
        if (Steps.Count > Max)
            Steps.RemoveFirst();
    }

    public static void Renamed(string oldPath, string newPath, bool folder) =>
        Push(Loc.T("Cambiar nombre"), async owner =>
            await FileOperations.RenameAsync(newPath, Path.GetFileName(oldPath), folder, owner) is null);

    public static void Created(string path) =>
        Push(Loc.T("Nueva carpeta"), owner => FileOperations.RecycleAsync([path], owner));

    /// <summary>Deshacer una copia manda las copias a la Papelera (de ahí se pueden recuperar).</summary>
    public static void Copied(List<(string From, string To)> placed)
    {
        if (placed.Count > 0)
            Push(Loc.T("Copiar"), owner => FileOperations.RecycleAsync(placed.Select(p => p.To).ToList(), owner));
    }

    /// <summary>Deshacer un movimiento devuelve cada cosa a su carpeta de antes.</summary>
    public static void Moved(List<(string From, string To)> placed)
    {
        if (placed.Count == 0)
            return;
        Push(Loc.T("Mover"), async owner =>
        {
            bool ok = true;
            foreach (var group in placed.GroupBy(p => Path.GetDirectoryName(p.From) ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var back = group.Select(p => p.To).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                if (group.Key.Length == 0 || back.Count == 0 || !await FileOperations.MoveAsync(back, group.Key, owner))
                    ok = false;
            }
            return ok;
        });
    }

    /// <summary>Deshacer el envío a la Papelera restaura lo que se mandó desde <paramref name="since"/>.</summary>
    public static void Recycled(IReadOnlyList<string> paths, DateTime since)
    {
        if (paths.Count > 0)
            Push(Loc.T("Enviar a la Papelera"), _ => Task.Run(() => RestoreFromBin(paths, since)));
    }

    private static bool RestoreFromBin(IReadOnlyList<string> paths, DateTime since)
    {
        var wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        // Si se mandó a la Papelera dos veces algo con la misma ruta, se restaura lo más reciente.
        var items = RecycleBin.Read()
            .Where(i => wanted.Contains(i.OriginalPath) && i.Deleted >= since.AddSeconds(-5))
            .GroupBy(i => i.OriginalPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(i => i.Deleted)!)
            .ToList();
        int restored = 0;
        foreach (var item in items)
        {
            try
            {
                RecycleBin.Restore(item);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Archivos: no se pudo restaurar al deshacer", ex);
            }
        }
        return restored > 0;
    }

    /// <summary>Deshace lo último; devuelve el aviso para mostrar (null si no había nada).</summary>
    public static async Task<(bool Ok, string Message)?> UndoAsync(IntPtr owner)
    {
        if (_busy || Steps.Last is not { } last)
            return null;
        Steps.RemoveLast();
        _busy = true;
        try
        {
            bool ok = await last.Value.Undo(owner);
            return (ok, ok ? Loc.F("Se deshizo: {0}", last.Value.Text) : Loc.F("No se pudo deshacer: {0}", last.Value.Text));
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: falló al deshacer", ex);
            return (false, Loc.F("No se pudo deshacer: {0}", last.Value.Text));
        }
        finally
        {
            _busy = false;
        }
    }
}
