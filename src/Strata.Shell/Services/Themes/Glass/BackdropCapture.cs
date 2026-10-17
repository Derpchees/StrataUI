using System.Runtime.InteropServices;
using System.Windows;

namespace Strata.Shell.Services.Themes.Glass;

/// <summary>
/// Lo que hay en pantalla detrás de un rectángulo, reducido a escala: la barra transparente lo usa
/// para elegir texto claro u oscuro según su fondo (Taskbar.Transparency), con la barra excluida de
/// la captura un momento (<see cref="ExcludeFromCapture"/>).
/// </summary>
public static class BackdropCapture
{
    /// <summary>Copia un rectángulo de la pantalla (píxeles físicos) reducido a w×h en BGRA.</summary>
    internal static bool Grab(Int32Rect rect, int w, int h, byte[] buffer)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        var info = new BitmapInfo
        {
            Size = Marshal.SizeOf<BitmapInfo>(),
            Width = w,
            Height = -h, // de arriba abajo
            Planes = 1,
            BitCount = 32,
        };
        IntPtr bitmap = CreateDIBSection(memory, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = SelectObject(memory, bitmap);
        try
        {
            SetStretchBltMode(memory, Halftone);
            if (!StretchBlt(memory, 0, 0, w, h, screen, rect.X, rect.Y, rect.Width, rect.Height, SrcCopy))
                return false;
            Marshal.Copy(bits, buffer, 0, buffer.Length);
            return true;
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>Excluye (o vuelve a incluir) una ventana de capturas de pantalla y grabaciones.</summary>
    public static void ExcludeFromCapture(IntPtr hwnd, bool exclude) =>
        SetWindowDisplayAffinity(hwnd, exclude ? WdaExcludeFromCapture : WdaNone);

    /// <summary>La ventana ya está excluida de capturas.</summary>
    public static bool IsExcludedFromCapture(IntPtr hwnd) =>
        GetWindowDisplayAffinity(hwnd, out uint affinity) && affinity == WdaExcludeFromCapture;

    private const uint WdaNone = 0, WdaExcludeFromCapture = 0x11;
    private const int Halftone = 4;
    private const uint SrcCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
}
