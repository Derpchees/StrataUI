using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Strata.Shell.Services;

namespace Strata.Shell.Tweaks;

/// <summary>
/// Acciones de administrador sin UAC: con Strata UI instalado para todos los usuarios (en Archivos de
/// programa, donde solo escriben administradores), el proceso elevado de las lecturas avanzadas
/// (<see cref="Sensors"/>, que el instalador deja listo) ejecuta por Strata UI lo que antes pedía UAC.
/// </summary>
/// <remarks>
/// Solo una lista fija (<see cref="IsAllowed"/>), con sus argumentos comprobados: nunca comandos
/// libres. Lo que cualquier programa del usuario podría pedir así es lo mismo que puede hacer Strata
/// UI (aplicar o deshacer sus ajustes, limpiar, crear un punto de restauración…), no cualquier cosa.
/// El respaldo de los ajustes vive en una carpeta del usuario: al restaurar, el proceso elevado solo
/// escribe claves del catálogo (ver TweakBackup.Restore). Lo que no está en la lista se pide con UAC.
/// </remarks>
public static class ElevatedBroker
{
    /// <summary>StrataUI.exe --remove-appx nombre…: quita apps de todos los usuarios (ver AppxManager).</summary>
    public const string RemoveAppxArg = "--remove-appx";

    /// <summary>Respuesta del ayudante a una acción que no está en la lista (quien la pidió usa UAC).</summary>
    public const int Rejected = -2;

    private static readonly Regex PackageName = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$");

    /// <summary>Nombre de paquete de la Store (letras, números, puntos, guiones): nada que PowerShell interprete.</summary>
    internal static bool IsPackageName(string name) => PackageName.IsMatch(name);

    /// <summary>Lo que el ayudante elevado ejecuta sin preguntar.</summary>
    internal static bool IsAllowed(string[] args)
    {
        if (args.Length == 0)
            return false;
        return args[0] switch
        {
            "--apply-tweak" => args.Length == 4
                               && TweakCatalog.All.FirstOrDefault(t => t.Id == args[1]) is { } tweak
                               && int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int state)
                               && state < tweak.Choices.Length
                               && SamePath(args[3], TweakEngine.Backup.FilePath),
            "--restore-tweaks" => args.Length == 2 && SamePath(args[1], TweakEngine.Backup.FilePath),
            "--restore-point" => args.Length == 1,
            // Estos validan sus argumentos en el proceso elevado (solo sus propiedades, categorías o ajustes).
            Services.Audio.AudioDeviceOptions.ElevatedArg => args.Length == 6,
            Services.Cleanup.JunkCleaner.ElevatedArg => args.Length == 3,
            Services.Vendors.NvidiaSettings.Arg => args.Length >= 2 && args[1] == "set",
            RemoveAppxArg => args.Length >= 2 && args.Skip(1).All(IsPackageName),
            SensorsTask.RemoveArg => args.Length == 2 && args[1] == SensorsTask.UserSid,
            _ => false,
        };
    }

    /// <summary>
    /// En el proceso elevado: decodifica el pedido (los argumentos en JSON y Base64), lo comprueba y
    /// lo ejecuta (StrataUI.exe con esos argumentos, que hereda los permisos). Devuelve su código de
    /// salida o <see cref="Rejected"/>.
    /// </summary>
    internal static int Run(string payload)
    {
        string[]? args;
        try
        {
            args = JsonSerializer.Deserialize<string[]>(Convert.FromBase64String(payload));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Rejected;
        }
        if (args is null || !IsAllowed(args))
        {
            Log.Info("Ayudante elevado: pedido rechazado: " + (args is null ? "(vacío)" : string.Join(' ', args.Take(2))));
            return Rejected;
        }
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            process.WaitForExit();
            Log.Info($"Ayudante elevado: {args[0]} → {process.ExitCode}");
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("Ayudante elevado: no se pudo ejecutar " + args[0], ex);
            return 1;
        }
    }

    /// <summary>Pedido para el ayudante: los argumentos en JSON y Base64 (una sola línea, sin espacios).</summary>
    internal static string Encode(string[] args) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(args));

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
