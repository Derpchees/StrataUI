using System.Globalization;
using Microsoft.Win32;
using Winx.Shell.Tweaks;

namespace Winx.Shell.Services.Audio;

/// <summary>
/// Opciones de Windows de un dispositivo de audio que viven en sus propiedades: mejoras de audio
/// y modo exclusivo (las mismas de Configuración y del panel de sonido clásico).
/// Leerlas no requiere permisos; escribirlas solo pueden el servicio de audio o un administrador
/// (IPolicyConfig.SetPropertyValue sin elevar no guarda nada), así que se escriben desde un
/// proceso elevado de corta vida.
/// </summary>
public static class AudioDeviceOptions
{
    public const string ElevatedArg = "--audio-property";

    private const string Root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio";

    // PKEY_AudioEndpoint_Disable_SysFx (mmdeviceapi.h), en FxProperties: 1 = mejoras desactivadas.
    private const string DisableSysFx = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";
    // Pestaña "Opciones avanzadas" del panel clásico, en Properties.
    private const string AllowExclusive = "{b3f8fa53-0004-438e-9003-51a46e139bfc},3";
    private const string ExclusivePriority = "{b3f8fa53-0004-438e-9003-51a46e139bfc},4";

    private const string FxStore = "FxProperties", PropertyStore = "Properties";

    /// <summary>"Mejoras de audio": true/false, o null si el dispositivo no tiene efectos.</summary>
    public static bool? GetEnhancements(AudioDevice device) =>
        Read(device, FxStore, DisableSysFx) is { } disabled ? disabled == 0 : null;

    public static Task<bool> SetEnhancementsAsync(AudioDevice device, bool enabled) =>
        WriteAsync(device, FxStore, DisableSysFx, enabled ? 0 : 1);

    /// <summary>Permitir que una app tome el control exclusivo (sin valor guardado: sí).</summary>
    public static bool GetExclusiveAllowed(AudioDevice device) => Read(device, PropertyStore, AllowExclusive) is not 0;

    public static Task<bool> SetExclusiveAllowedAsync(AudioDevice device, bool allowed) =>
        WriteAsync(device, PropertyStore, AllowExclusive, allowed ? 1 : 0);

    /// <summary>Dar prioridad a la app en modo exclusivo (sin valor guardado: sí).</summary>
    public static bool GetExclusivePriority(AudioDevice device) => Read(device, PropertyStore, ExclusivePriority) is not 0;

    public static Task<bool> SetExclusivePriorityAsync(AudioDevice device, bool priority) =>
        WriteAsync(device, PropertyStore, ExclusivePriority, priority ? 1 : 0);

    // ── Registro ────────────────────────────────────────────────────────────

    /// <summary>"{0.0.0.00000000}.{guid}" → ruta de su clave en MMDevices.</summary>
    internal static string KeyPath(AudioDevice device, string store) =>
        KeyPath(device.IsInput ? "Capture" : "Render", EndpointGuid(device.Id), store);

    internal static string EndpointGuid(string deviceId) =>
        deviceId[(deviceId.LastIndexOf('.') + 1)..];

    private static string KeyPath(string flow, string endpointGuid, string store) =>
        $@"{Root}\{flow}\{endpointGuid}\{store}";

    private static int? Read(AudioDevice device, string store, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath(device, store));
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Escribe desde un proceso elevado (UAC). False si se canceló o falló.</summary>
    private static async Task<bool> WriteAsync(AudioDevice device, string store, string name, int value)
    {
        int code = await Elevation.RunSelfAsync(ElevatedArg, device.IsInput ? "Capture" : "Render",
                                               EndpointGuid(device.Id), store, name,
                                               value.ToString(CultureInfo.InvariantCulture));
        if (code != 0 && code != Elevation.Cancelled)
            Log.Error($"No se pudo cambiar {name} de {device.Name} (código {code})");
        return code == 0;
    }

    /// <summary>Punto de entrada del proceso elevado: flujo, GUID del dispositivo, almacén, nombre y valor.</summary>
    public static int RunElevated(string[] args)
    {
        try
        {
            string flow = args[1], endpoint = args[2], store = args[3], name = args[4];
            int value = int.Parse(args[5], CultureInfo.InvariantCulture);
            if (flow is not ("Render" or "Capture") || store is not (FxStore or PropertyStore)
                || name is not (DisableSysFx or AllowExclusive or ExclusivePriority)
                || !Guid.TryParse(endpoint, out _))
                return 2; // solo estas propiedades: el proceso elevado no escribe lo que le pidan

            using var key = Registry.LocalMachine.OpenSubKey(KeyPath(flow, endpoint, store), writable: true);
            if (key is null)
                return 3;
            key.SetValue(name, value, RegistryValueKind.DWord);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error($"Comando elevado de audio falló: {string.Join(' ', args)}", ex);
            return 1;
        }
    }
}
