using System.Diagnostics;
using Microsoft.Win32;

namespace Strata.Shell.Services.Brightness;

/// <summary>
/// Luz nocturna de Windows (colores cálidos para descansar la vista). No tiene API pública: el estado
/// vive en un valor binario de CloudStore que Configuración también escribe, y Windows lo aplica en
/// cuanto cambia la marca de tiempo de ese valor.
/// Formato (encendida / apagada): byte 18 = 0x15 / 0x13 y, encendida, dos bytes "10 00" en la
/// posición 23; los bytes 10–14 son la hora del cambio (segundos Unix en varint de 7 bits).
/// </summary>
public static class NightLight
{
    private const string StateKey =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
        @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private const int FlagIndex = 18, ExtraIndex = 23, TimeIndex = 10, TimeLength = 5;
    private const byte On = 0x15, Off = 0x13;

    /// <summary>True si Windows tiene el valor (en algunos equipos o escritorios remotos no está).</summary>
    public static bool IsAvailable => Read() is { } data && IsEnabled(data) != null;

    /// <summary>Encendida, apagada o null si no se pudo leer.</summary>
    public static bool? IsOn => Read() is { } data ? IsEnabled(data) : null;

    /// <summary>Enciende o apaga la luz nocturna. Devuelve false si no se pudo.</summary>
    public static bool Set(bool on)
    {
        try
        {
            if (Read() is not { } data || With(data, on, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) is not { } changed)
                return false;
            using var key = Registry.CurrentUser.OpenSubKey(StateKey, writable: true);
            if (key is null)
                return false;
            key.SetValue("Data", changed, RegistryValueKind.Binary);
            Log.Info(on ? "Luz nocturna encendida" : "Luz nocturna apagada");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Log.Error("No se pudo cambiar la luz nocturna", ex);
            return false;
        }
    }

    /// <summary>Abre Configuración › Pantalla › Luz nocturna (intensidad y horario).</summary>
    public static void OpenSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:nightlight") { UseShellExecute = true });

    private static byte[]? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StateKey);
        return key?.GetValue("Data") as byte[];
    }

    /// <summary>Estado del valor: encendida, apagada o null si no tiene el formato conocido.</summary>
    internal static bool? IsEnabled(byte[] data)
    {
        if (data.Length <= ExtraIndex + 1)
            return null;
        return data[FlagIndex] switch
        {
            On when data[ExtraIndex] == 0x10 && data[ExtraIndex + 1] == 0x00 => true,
            Off => false,
            _ => null,
        };
    }

    /// <summary>
    /// El valor con la luz nocturna encendida o apagada y la hora <paramref name="unixSeconds"/>;
    /// el mismo contenido si ya estaba así (con la hora nueva), o null si el formato no es el conocido.
    /// </summary>
    internal static byte[]? With(byte[] data, bool on, long unixSeconds)
    {
        if (IsEnabled(data) is not { } current)
            return null;
        var result = new List<byte>(data);
        if (on && !current)
        {
            result.InsertRange(ExtraIndex, new byte[] { 0x10, 0x00 });
            result[FlagIndex] = On;
        }
        else if (!on && current)
        {
            result.RemoveRange(ExtraIndex, 2);
            result[FlagIndex] = Off;
        }
        // La hora: Windows aplica el cambio cuando ve una marca nueva, en segundos. Dos cambios en el
        // mismo segundo llevaban la misma marca y Windows ignoraba el segundo (encender y apagar
        // rápido dejaba el interruptor y la pantalla desparejos): siempre posterior a la anterior.
        if (ReadVarint(data, TimeIndex) is { } previous && unixSeconds <= previous)
            unixSeconds = previous + 1;
        var time = Varint(unixSeconds);
        if (time.Length != TimeLength)
            return null;
        for (int i = 0; i < TimeLength; i++)
            result[TimeIndex + i] = time[i];
        return result.ToArray();
    }

    /// <summary>Lee un varint de <see cref="TimeLength"/> bytes como mucho; null si no termina ahí.</summary>
    internal static long? ReadVarint(byte[] data, int index)
    {
        long value = 0;
        for (int i = 0; i < TimeLength && index + i < data.Length; i++)
        {
            value |= (long)(data[index + i] & 0x7F) << (7 * i);
            if ((data[index + i] & 0x80) == 0)
                return value;
        }
        return null;
    }

    /// <summary>Entero en varint de 7 bits (el bit alto marca que sigue otro byte).</summary>
    internal static byte[] Varint(long value)
    {
        var bytes = new List<byte>();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            bytes.Add(b);
        }
        while (value != 0);
        return bytes.ToArray();
    }
}
