using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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
            WindowEffects.RemoveCaption(hwnd);
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

    /// <summary>
    /// Vidrio en un popup (menú contextual, bandeja oculta) al abrirse: el desenfoque del tema detrás
    /// de su ventana y el tinte de los paneles de fondo. Con los demás temas no cambia nada.
    /// </summary>
    public static void ApplyPopup(FrameworkElement element, System.Windows.Controls.Control? tinted = null)
    {
        if (!Active)
        {
            // Volvió a un tema opaco: el menú recupera el margen de su sombra.
            foreach (var part in new[] { element, Card(element) })
                if (part is not null && Margins.TryGetValue(part, out var saved))
                {
                    part.Margin = saved.Margin;
                    Margins.Remove(part);
                }
            return;
        }
        if (PresentationSource.FromVisual(element) is not HwndSource source)
            return;
        WindowEffects.SetBlur(source.Handle, ThemeManager.Theme.Backdrop);
        int round = CornerRound;
        DwmSetWindowAttribute(source.Handle, CornerPreference, ref round, sizeof(int));
        tinted?.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "StrataGlassPopupBrush");
        // El desenfoque cubre toda la ventana del popup y una región no lo recorta: sin el margen que
        // la plantilla deja para la sombra, la ventana mide lo mismo que la tarjeta y DWM redondea
        // sus esquinas (y dibuja su sombra).
        element.UpdateLayout();
        // El margen puede estar en el propio menú (ContextMenu deja 30 px) o en la tarjeta de dentro.
        foreach (var part in new[] { element, Card(element) })
            if (part is not null && part.Margin != default)
            {
                if (!Margins.TryGetValue(part, out _))
                    Margins.Add(part, new SavedMargin(part.Margin));
                part.Margin = default;
            }
    }

    private sealed record SavedMargin(Thickness Margin);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, SavedMargin> Margins = new();

    /// <summary>El primer borde con fondo dentro del popup: la tarjeta que se ve (fuera queda el margen de la sombra).</summary>
    private static FrameworkElement? Card(DependencyObject node)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is System.Windows.Controls.Border { Background: not null } border
                && border.ActualWidth > 0 && border.Background is not SolidColorBrush { Color.A: 0 })
                return border;
            if (Card(child) is { } found)
                return found;
        }
        return null;
    }


    private const int CornerPreference = 33, CornerDoNotRound = 1, CornerRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
