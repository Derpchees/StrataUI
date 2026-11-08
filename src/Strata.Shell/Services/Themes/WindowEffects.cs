using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Strata.Shell.Services.Themes;

/// <summary>Efectos de ventana de DWM: desenfoque Acrylic, Mica y modo oscuro.</summary>
public static class WindowEffects
{
    /// <summary>
    /// Desenfoque detrás de una ventana mediante la política de acento de DWM.
    /// Acrylic = el de Windows (con ruido y luminosidad); Clear = desenfoque limpio, base del
    /// vidrio estilo iOS. WPF debe dibujar con fondo transparente para que se vea.
    /// </summary>
    /// <remarks>Con <paramref name="clear"/> no hay desenfoque ni fondo: lo transparente de WPF deja
    /// ver tal cual lo que hay detrás (barra totalmente transparente).</remarks>
    public static void SetBlur(Window window, Backdrop backdrop, bool clear = false) =>
        SetBlur(new WindowInteropHelper(window).Handle, backdrop, clear);

    /// <summary>Lo mismo para cualquier ventana de WPF (también la de un Popup o un menú contextual).</summary>
    public static void SetBlur(IntPtr hwnd, Backdrop backdrop, bool clear = false)
    {
        if (hwnd == IntPtr.Zero)
            return;
        bool enabled = clear || backdrop is Backdrop.Acrylic or Backdrop.Blur;
        // Translúcido es vidrio esmerilado (lo de detrás desenfocado). Transparente total deja ver lo
        // de detrás tal cual, nítido, solo con el tinte de WPF.
        if (enabled && ThemeManager.Theme.ClearBar)
            clear = true;

        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = enabled ? Colors.Transparent : SystemColors.WindowColor;

        var accent = new AccentPolicy
        {
            // Transparente: sin acento. (El "gradiente transparente" de DWM pinta el color de acento
            // de Windows o, con su bandera y alfa 0, esconde también el contenido de WPF.)
            // Translúcido también usa el desenfoque limpio: el Acrylic de DWM agrega una capa de
            // luminosidad y ruido que dejaba los menús y avisos grises y casi opacos.
            AccentState = clear ? AccentDisabled : backdrop switch
            {
                Backdrop.Acrylic or Backdrop.Blur => AccentEnableBlurBehind,
                _ => AccentDisabled,
            },
            // Tinte casi nulo: el tinte real lo pinta WPF con la paleta del tema.
            GradientColor = enabled && !clear ? 0x01000000u : 0u,
        };
        int size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        // Sin esto DWM ignora el canal alfa de WPF y pinta un color plano detrás de la ventana:
        // extender el marco a todo el cliente hace que lo transparente muestre el efecto. Va después
        // del acento: al quitar el Acrylic, DWM deshace la extensión anterior y quedaría negro.
        var margins = enabled ? new Margins(-1) : new Margins(0);
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    /// <summary>
    /// Quita el estilo de barra de título (WS_CAPTION) que deja WindowChrome. Con él, DWM pinta el
    /// color gris del marco detrás de lo transparente de WPF y el vidrio se veía opaco.
    /// </summary>
    public static void RemoveCaption(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        int style = GetWindowLongW(hwnd, GwlStyle);
        if ((style & WsCaption) == 0)
            return;
        SetWindowLongW(hwnd, GwlStyle, style & ~WsCaption);
        // Sin esto Windows no vuelve a calcular el marco con el estilo nuevo.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLongW(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>
    /// Recorta la ventana (y su desenfoque) a un rectángulo redondeado, en coordenadas de
    /// cliente y píxeles físicos. Con <paramref name="bounds"/> null se quita el recorte.
    /// </summary>
    public static void SetRoundedRegion(Window window, Int32Rect? bounds, int radius)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        if (bounds is not { } r)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }
        // +1: CreateRoundRectRgn excluye el borde derecho e inferior.
        var region = CreateRoundRectRgn(r.X, r.Y, r.X + r.Width + 1, r.Y + r.Height + 1, radius * 2, radius * 2);
        SetWindowRgn(hwnd, region, true); // el sistema pasa a ser dueño de la región
    }

    /// <summary>Material de fondo de DWM para una ventana (propia o de otro proceso).</summary>
    public static int SetSystemBackdrop(IntPtr hwnd, int type)
    {
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
    }

    public static int SetDarkMode(IntPtr hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        return DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    public const int BackdropAuto = 0;
    public const int BackdropNone = 1;
    public const int BackdropMica = 2;
    public const int BackdropAcrylic = 3;

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int all)
    {
        public int Left = all, Right = all, Top = all, Bottom = all;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
