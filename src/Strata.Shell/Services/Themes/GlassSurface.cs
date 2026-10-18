using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Vidrio de las ventanas pequeñas (avisos, OSD) con los temas translúcidos: el desenfoque del tema
/// detrás y las esquinas redondeadas por Windows. El desenfoque de DWM cubre toda la ventana (una
/// región no lo recorta), así que con vidrio la ventana mide lo mismo que su tarjeta, sin márgenes
/// ni sombra, y las esquinas las redondea DWM, que sí recorta el desenfoque. Con los demás temas la
/// ventana queda transparente y la tarjeta (opaca, con su margen) se ve como siempre.
/// </summary>
/// <remarks>
/// Solo en ventanas normales (AllowsTransparency="False"): los menús contextuales y los Popup son
/// ventanas en capas y no llevan vidrio.
/// </remarks>
public static class GlassSurface
{
    /// <summary>El tema pide vidrio en los paneles (Translúcido, Transparente total o de vidrio).</summary>
    public static bool Active => ThemeManager.Theme.Backdrop is Backdrop.Acrylic or Backdrop.Blur;

    /// <summary>Pone o quita el vidrio de la ventana según el tema (llamar al crearla y al cambiar de tema).</summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        if (Active)
        {
            WindowEffects.SetBlur(hwnd, ThemeManager.Theme.Backdrop);
            int round = CornerRound;
            DwmSetWindowAttribute(hwnd, CornerPreference, ref round, sizeof(int));
        }
        else
        {
            WindowEffects.SetBlur(hwnd, Backdrop.Solid, clear: true);
            int none = CornerDoNotRound;
            DwmSetWindowAttribute(hwnd, CornerPreference, ref none, sizeof(int));
        }
    }

    private const int CornerPreference = 33, CornerDoNotRound = 1, CornerRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
