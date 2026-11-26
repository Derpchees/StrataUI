using System.Diagnostics;
using System.IO;
using System.Windows;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Services.Notepad;

/// <summary>A qué Bloc de notas va cada archivo: el de Strata UI (si está y se eligió) o el de Windows.</summary>
public static class WindowsNotepad
{
    /// <summary>El Bloc de notas de Windows (la app de la tienda o el clásico), con esos archivos.</summary>
    public static void Open(IReadOnlyList<string> paths)
    {
        try
        {
            if (paths.Count == 0)
            {
                Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true })?.Dispose();
                return;
            }
            foreach (string path in paths)
                Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("No se pudo abrir el Bloc de notas de Windows", ex);
        }
    }

    /// <summary>
    /// Si este archivo de texto va al Bloc de notas de Strata UI, lo abre ahí y devuelve true; si no,
    /// quien llama lo abre como siempre.
    /// </summary>
    public static bool TryOpenInStrata(string path, bool anyFile = false)
    {
        if (!OptionalModules.Notepad.ReplacesNotepad || (!anyFile && !TextFile.IsTextFile(path)) || !File.Exists(path))
            return false;
        return TryOpenInStrata([path]);
    }

    /// <summary>Abre en el Bloc de notas de Strata UI (solo dentro de Strata UI: necesita la app WPF).</summary>
    public static bool TryOpenInStrata(IReadOnlyList<string> paths)
    {
        if (!OptionalModules.Notepad.ReplacesNotepad || Application.Current is not App app)
            return false;
        if (app.Dispatcher.CheckAccess())
            app.OpenNotepad(paths);
        else
            app.Dispatcher.BeginInvoke(() => app.OpenNotepad(paths));
        return true;
    }

    /// <summary>El Bloc de notas de Windows, como lo nombra Inicio (app de la tienda o notepad.exe).</summary>
    public static bool IsWindowsNotepad(string id) =>
        id.StartsWith("Microsoft.WindowsNotepad_", StringComparison.OrdinalIgnoreCase)
        || id.EndsWith(@"\notepad.exe", StringComparison.OrdinalIgnoreCase)
        || id.EndsWith(@"\Notepad.lnk", StringComparison.OrdinalIgnoreCase)
        || id.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase)
        || id.Equals("notepad", StringComparison.OrdinalIgnoreCase);
}
