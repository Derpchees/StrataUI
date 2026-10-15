using System.IO;
using Microsoft.Win32;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.StartMenu;

/// <summary>
/// "Anclar a Inicio de Strata UI" en el menú contextual del Explorador: un verbo del usuario
/// (HKCU, sin administrador) para archivos, accesos directos y carpetas. Sale en el menú clásico
/// ("Mostrar más opciones" en Windows 11) y en los que lo reemplazan, como Nilesoft Shell. Llama a
/// <c>StrataUI.exe --pin-start "ruta"</c>, que se lo pasa a la instancia abierta.
/// </summary>
public static class ExplorerMenu
{
    public const string PinArg = "--pin-start";

    private const string VerbKey = @"Software\Classes\AllFilesystemObjects\shell\StrataUI.PinToStart";
    private const string PinEventName = @"Local\StrataUI.Shell.PinToStart";

    private static readonly string RequestsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrataUI", "pin-requests.txt");

    /// <summary>Pone o quita el verbo según la opción (con la ruta del ejecutable actual).</summary>
    public static void Update()
    {
        try
        {
            if (!Settings.Current.StartExplorerMenu)
            {
                Registry.CurrentUser.DeleteSubKeyTree(VerbKey, throwOnMissingSubKey: false);
                return;
            }
            string exe = Environment.ProcessPath ?? "";
            using var key = Registry.CurrentUser.CreateSubKey(VerbKey);
            key.SetValue("MUIVerb", Loc.T("Anclar a Inicio de Strata UI"));
            key.SetValue("Icon", $"\"{exe}\",0");
            using var command = key.CreateSubKey("command");
            command.SetValue("", $"\"{exe}\" {PinArg} \"%1\"");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error("No se pudo actualizar el menú contextual del Explorador", ex);
        }
    }

    // ── Petición desde el Explorador ────────────────────────────────────────

    /// <summary>
    /// StrataUI.exe --pin-start ruta: si Strata UI está abierto le deja la ruta y lo avisa; si no, la
    /// ancla en la configuración (se verá al abrirlo).
    /// </summary>
    internal static int RunRequest(string[] args)
    {
        if (args.Length != 2 || !StartMenuService.IsPathId(args[1]) || !(File.Exists(args[1]) || Directory.Exists(args[1])))
            return 2;
        if (EventWaitHandle.TryOpenExisting(PinEventName, out var running))
        {
            using (running)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RequestsPath)!);
                // Con varios archivos elegidos el Explorador abre un proceso por archivo: se reintenta si el archivo está ocupado.
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        File.AppendAllLines(RequestsPath, [args[1]]);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(50);
                    }
                }
                running.Set();
            }
            return 0;
        }
        PinLayout.Pin(args[1]);
        return 0;
    }

    private static EventWaitHandle? _event;
    private static RegisteredWaitHandle? _wait;

    /// <summary>La instancia principal atiende las peticiones (y las que quedaron de antes de abrirse).</summary>
    public static void Listen()
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, PinEventName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_event,
            (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(HandleRequests), null, -1, executeOnlyOnce: false);
        HandleRequests();
    }

    public static void Stop()
    {
        _wait?.Unregister(null);
        _event?.Dispose();
    }

    private static void HandleRequests()
    {
        if (!File.Exists(RequestsPath))
            return;
        try
        {
            var paths = File.ReadAllLines(RequestsPath).Where(p => p.Length > 0).Distinct().ToList();
            File.Delete(RequestsPath);
            Log.Info($"Anclar a Inicio desde el Explorador: {string.Join(", ", paths)}");
            StartMenuService.PinPaths(paths);
        }
        catch (IOException)
        {
            // Otro proceso está escribiendo: llegará otro aviso.
        }
    }
}
