using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Strata.Shell.Services;

/// <summary>Fondo de pantalla del escritorio (vía SystemParametersInfo).</summary>
public static class Wallpaper
{
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    public static string Current
    {
        get
        {
            var buffer = new char[520];
            SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)buffer.Length, buffer, 0);
            return new string(buffer).TrimEnd('\0');
        }
    }

    public static void Set(string path)
    {
        if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, char[] value, uint flags);
}
