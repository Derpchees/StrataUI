using System.IO;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Sonidos de notificación importados (Hub › Importar): archivos de audio en Modules\Sounds. El elegido
/// suena en lugar del tono de Strata UI (<see cref="NotificationSound"/>); viajan con la
/// configuración exportada.
/// </summary>
public static class SoundLibrary
{
    public static readonly string[] Extensions = [".wav", ".mp3", ".m4a", ".wma"];

    /// <summary>Más grande que esto no es un sonido de aviso.</summary>
    private const long MaxBytes = 5 * 1024 * 1024;

    public static string Folder => Path.Combine(ModuleStore.Root, "Sounds");

    /// <summary>Se importó, se quitó o se eligió otro sonido.</summary>
    public static event Action? Changed;

    public static bool IsSound(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static IReadOnlyList<string> All() =>
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder).Where(IsSound).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList()
            : [];

    /// <summary>El sonido elegido (null: el tono de Strata UI, también si el archivo ya no está).</summary>
    public static string? Current =>
        Settings.Current.NotificationSoundFile is { Length: > 0 } name && File.Exists(Path.Combine(Folder, name))
            ? Path.Combine(Folder, name)
            : null;

    public static async Task<(int Imported, List<string> Errors)> ImportAsync()
    {
        var files = await FilePicker.PickFilesAsync(Loc.T("Importar sonido"),
            Loc.T("Sonidos (*.wav, *.mp3, *.m4a, *.wma)") + "|*.wav;*.mp3;*.m4a;*.wma", multiselect: true);
        int imported = 0;
        var errors = new List<string>();
        foreach (var file in files)
        {
            try
            {
                if (!IsSound(file))
                    errors.Add(Loc.F("{0}: no es un sonido", Path.GetFileName(file)));
                else if (new FileInfo(file).Length > MaxBytes)
                    errors.Add(Loc.F("{0}: muy grande (máximo 5 MB)", Path.GetFileName(file)));
                else
                {
                    Directory.CreateDirectory(Folder);
                    File.Copy(file, Path.Combine(Folder, Path.GetFileName(file)), overwrite: true);
                    imported++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        if (imported > 0)
            Changed?.Invoke();
        return (imported, errors);
    }

    /// <summary>Usa ese sonido (null: el tono de Strata UI).</summary>
    public static void Use(string? file)
    {
        Settings.Update(s => s.NotificationSoundFile = file is null ? null : Path.GetFileName(file));
        Changed?.Invoke();
    }

    public static void Delete(string file)
    {
        if (string.Equals(Path.GetFileName(file), Settings.Current.NotificationSoundFile, StringComparison.OrdinalIgnoreCase))
            Settings.Update(s => s.NotificationSoundFile = null);
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("No se pudo quitar el sonido " + file, ex);
        }
        Changed?.Invoke();
    }
}
