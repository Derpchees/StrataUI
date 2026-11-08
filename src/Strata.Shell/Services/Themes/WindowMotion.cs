using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Strata.Shell.Services.Themes;

/// <summary>
/// Minimizar, restaurar, maximizar y desmaximizar las ventanas de las apps con la animación elegida
/// en el Hub. Windows no deja cambiar cómo anima una ventana ajena: se apaga la suya
/// (<see cref="WindowsAnimations"/>) y Strata UI dibuja encima una miniatura en vivo de la ventana
/// (DWM) que va de su lugar al botón de la barra, o del tamaño anterior al nuevo.
/// </summary>
/// <remarks>
/// Al restaurar o maximizar, la ventana real ya está en su lugar final: se vuelve invisible (en capas,
/// opacidad 0) mientras corre la miniatura y se devuelve tal cual al terminar. Las que ya son en capas
/// no se tocan (su opacidad es suya); solo se anima la miniatura encima.
/// </remarks>
public static class WindowMotion
{
    private const uint EventMinimizeStart = 0x0016, EventMinimizeEnd = 0x0017, EventObjectShow = 0x8002, EventLocationChange = 0x800B;

    private static WinEventProc? _callback;
    private static IntPtr _minimizeHook, _locationHook, _showHook;

    /// <summary>Último lugar (sin minimizar) de cada ventana, y si estaba maximizada.</summary>
    private static readonly Dictionary<IntPtr, (NativeRect Rect, bool Zoomed)> Known = new();

    /// <summary>Animación en curso de cada ventana (una nueva corta la anterior).</summary>
    private static readonly Dictionary<IntPtr, Ghost> Running = new();

    /// <summary>Strata UI anima las ventanas (con "Sin animación" no se anima nada).</summary>
    public static bool Active => Settings.Current.PanelAnimation != PanelAnimation.None;

    /// <summary>Empieza a seguir las ventanas (en el hilo de la interfaz, que tiene bucle de mensajes).</summary>
    public static void Start()
    {
        if (_callback != null)
            return;
        _callback = OnEvent;
        _minimizeHook = SetWinEventHook(EventMinimizeStart, EventMinimizeEnd, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        _locationHook = SetWinEventHook(EventLocationChange, EventLocationChange, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        _showHook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        // Las ventanas que ya estaban abiertas no son "nuevas".
        EnumWindows((hwnd, _) =>
        {
            if (Animatable(hwnd) && GetWindowRect(hwnd, out var rect))
                Known[hwnd] = (rect, IsZoomed(hwnd));
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Al salir: ninguna ventana debe quedar invisible a medio animar.</summary>
    public static void Stop()
    {
        foreach (var ghost in Running.Values.ToList())
            ghost.Finish();
        Running.Clear();
        if (_minimizeHook != IntPtr.Zero)
            UnhookWinEvent(_minimizeHook);
        if (_locationHook != IntPtr.Zero)
            UnhookWinEvent(_locationHook);
        if (_showHook != IntPtr.Zero)
            UnhookWinEvent(_showHook);
        _minimizeHook = _locationHook = _showHook = IntPtr.Zero;
        Known.Clear();
        _callback = null;
    }

    private static void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero)
            return;
        try
        {
            switch (evt)
            {
                case EventLocationChange:
                    OnMoved(hwnd);
                    break;
                case EventObjectShow:
                    OnShown(hwnd);
                    break;
                case EventMinimizeStart:
                    OnMinimized(hwnd);
                    break;
                case EventMinimizeEnd:
                    OnRestored(hwnd);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo animar la ventana", ex);
        }
    }

    /// <summary>Ventana de app con barra de título (no menús, tooltips ni ventanas de Strata UI).</summary>
    private static bool Animatable(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetAncestor(hwnd, GaRoot) != hwnd)
            return false;
        int style = GetWindowLong(hwnd, GwlStyle);
        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        if ((style & WsCaption) != WsCaption || (exStyle & WsExToolWindow) != 0)
            return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid != Environment.ProcessId;
    }

    /// <summary>Ventana en capas con transparencia total o que deja pasar los clics: no está para verse.</summary>
    private static bool Invisible(IntPtr hwnd)
    {
        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        if ((exStyle & WsExLayeredFlag) == 0)
            return false;
        if ((exStyle & WsExTransparentFlag) != 0)
            return true;
        return GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags) && (flags & LwaAlphaFlag) != 0 && alpha == 0;
    }

    /// <summary>
    /// Carpetas del Explorador y cuadros de Windows que Archivos puede reemplazar: aparecen y se ocultan en
    /// milisegundos, y la animación los dejaría ver (la miniatura de DWM ignora su transparencia).
    /// </summary>
    private static bool TakenByFiles(IntPtr hwnd)
    {
        var name = new System.Text.StringBuilder(64);
        if (GetClassName(hwnd, name, name.Capacity) == 0)
            return false;
        return name.ToString() switch
        {
            "CabinetWClass" => Files.ExplorerTakeover.Active,
            "#32770" => Files.DialogTakeover.Active,
            _ => false,
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

    private const int WsExLayeredFlag = 0x80000;
    private const int WsExTransparentFlag = 0x20;
    private const uint LwaAlphaFlag = 0x2;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);

    private static void OnMoved(IntPtr hwnd)
    {
        if (IsIconic(hwnd) || Running.ContainsKey(hwnd) || !Animatable(hwnd))
            return;
        GetWindowRect(hwnd, out var rect);
        Known[hwnd] = (rect, IsZoomed(hwnd));
        if (Known.Count > 300)
            foreach (var gone in Known.Keys.Where(h => !IsWindow(h)).ToList())
                Known.Remove(gone);
    }

    /// <summary>Una ventana que se abre (la primera vez que aparece) entra con la animación elegida.</summary>
    private static void OnShown(IntPtr hwnd)
    {
        if (!Active || IsIconic(hwnd) || Known.ContainsKey(hwnd) || !Animatable(hwnd))
            return;
        GetWindowRect(hwnd, out var rect);
        Known[hwnd] = (rect, IsZoomed(hwnd));
        // Invisible a propósito (p. ej. el Explorador que Archivos reemplaza): la miniatura de DWM la mostraría igual.
        if (rect.Width <= 0 || rect.Height <= 0 || Invisible(hwnd) || TakenByFiles(hwnd))
            return;
        Run(hwnd, rect, rect, Kind.Open);
    }

    private static void OnMinimized(IntPtr hwnd)
    {
        if (!Active || !Animatable(hwnd))
            return;
        var from = Known.TryGetValue(hwnd, out var known) ? known.Rect : NormalRect(hwnd);
        if (from.Width <= 0)
            return;
        Run(hwnd, from, IconRect(hwnd, from), Kind.Minimize);
    }

    private static void OnRestored(IntPtr hwnd)
    {
        if (!Active || !Animatable(hwnd))
            return;
        GetWindowRect(hwnd, out var to);
        if (to.Width <= 0)
            return;
        Known[hwnd] = (to, IsZoomed(hwnd));
        Run(hwnd, IconRect(hwnd, to), to, Kind.Restore);
    }

    /// <summary>El botón de la ventana en la barra o, si no tiene, el centro de abajo de su monitor.</summary>
    private static NativeRect IconRect(IntPtr hwnd, NativeRect window)
    {
        if (Controls.TaskButton.ScreenRectOf(hwnd) is { } button)
            return new NativeRect { Left = button.Left, Top = button.Top, Right = button.Right, Bottom = button.Bottom };
        var screen = System.Windows.Forms.Screen.FromRectangle(System.Drawing.Rectangle.FromLTRB(window.Left, window.Top, window.Right, window.Bottom)).Bounds;
        int x = screen.Left + screen.Width / 2, y = screen.Bottom - 24;
        return new NativeRect { Left = x - 24, Top = y - 24, Right = x + 24, Bottom = y + 24 };
    }

    private static NativeRect NormalRect(IntPtr hwnd)
    {
        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        return GetWindowPlacement(hwnd, ref placement) ? placement.Normal : default;
    }

    private static void Run(IntPtr hwnd, NativeRect from, NativeRect to, Kind kind)
    {
        if (Running.Remove(hwnd, out var old))
            old.Finish();
        var ghost = new Ghost(hwnd, from, to, kind);
        if (!ghost.Begin())
            return;
        Running[hwnd] = ghost;
        ghost.Done += () =>
        {
            if (Running.TryGetValue(hwnd, out var current) && current == ghost)
                Running.Remove(hwnd);
        };
    }

    private enum Kind { Minimize, Restore, Open }

    /// <summary>La miniatura que hace el recorrido, en una ventana encima de todo que no se activa.</summary>
    private sealed class Ghost
    {
        private readonly IntPtr _target;
        private readonly NativeRect _from, _to, _box;
        private readonly Kind _kind;
        private readonly PanelAnimation _style = Settings.Current.PanelAnimation;
        private readonly TimeSpan _duration;
        private readonly Stopwatch _clock = new();
        private Window? _window;
        private IntPtr _thumb;
        private bool _hidden, _finished;

        public event Action? Done;

        public Ghost(IntPtr target, NativeRect from, NativeRect to, Kind kind)
        {
            _target = target;
            _from = from;
            _to = to;
            _kind = kind;
            // Una ventana recorre más pantalla que un panel: un poco más larga.
            _duration = TimeSpan.FromMilliseconds(Motion.Duration(kind == Kind.Minimize).TotalMilliseconds * 1.4);
            _box = new NativeRect
            {
                Left = Math.Min(from.Left, to.Left) - 40,
                Top = Math.Min(from.Top, to.Top) - 40,
                Right = Math.Max(from.Right, to.Right) + 40,
                Bottom = Math.Max(from.Bottom, to.Bottom) + 40,
            };
        }

        public bool Begin()
        {
            _window = new Window
            {
                // Sin el estilo implícito de las ventanas de la app (cambia AllowsTransparency al iniciarse,
                // y eso falla después de crear el identificador).
                Style = new Style(typeof(Window)),
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = Brushes.Transparent,
                Title = "Strata UI animación",
                Left = -32000,
                Top = -32000,
                Width = 1,
                Height = 1,
            };
            var helper = new WindowInteropHelper(_window);
            var hwnd = helper.EnsureHandle();
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
                target.BackgroundColor = Colors.Transparent;
            // Transparente fuera de la miniatura (sin capas: DWM no dibuja miniaturas en ventanas en capas).
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow | WsExTransparent);
            if (DwmRegisterThumbnail(hwnd, _target, out _thumb) != 0)
            {
                _window.Close();
                return false;
            }
            Apply(0);
            _window.Show();
            SetWindowPos(hwnd, HwndTopmost, _box.Left, _box.Top, _box.Width, _box.Height, SwpNoActivate | SwpShowWindow);
            Hide();
            _clock.Start();
            CompositionTarget.Rendering += OnFrame;
            return true;
        }

        /// <summary>Al restaurar o cambiar de tamaño, la ventana real ya está ahí: se oculta mientras.</summary>
        private void Hide()
        {
            if (_kind == Kind.Minimize)
                return;
            int exStyle = GetWindowLong(_target, GwlExStyle);
            if ((exStyle & WsExLayered) != 0)
                return;
            SetWindowLong(_target, GwlExStyle, exStyle | WsExLayered);
            _hidden = SetLayeredWindowAttributes(_target, 0, 0, LwaAlpha);
            if (!_hidden)
                SetWindowLong(_target, GwlExStyle, exStyle);
        }

        private void Show()
        {
            if (!_hidden)
                return;
            _hidden = false;
            // Otra parte de Strata UI la volvió invisible mientras tanto (p. ej. al reemplazarla): se deja así.
            if ((GetWindowLong(_target, GwlExStyle) & WsExTransparentFlag) != 0)
                return;
            SetLayeredWindowAttributes(_target, 0, 255, LwaAlpha);
            SetWindowLong(_target, GwlExStyle, GetWindowLong(_target, GwlExStyle) & ~WsExLayered);
            RedrawWindow(_target, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwFrame | RdwAllChildren);
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            double t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / _duration.TotalMilliseconds);
            if (!IsWindow(_target) || !IsWindowVisible(_target) || (GetWindowLong(_target, GwlExStyle) & WsExTransparentFlag) != 0)
            {
                Finish();
                return;
            }
            Apply(t);
            if (t >= 1)
                Finish();
        }

        /// <summary>Lugar y opacidad de la miniatura en el momento <paramref name="t"/> (0 a 1).</summary>
        private void Apply(double t)
        {
            bool closing = _kind == Kind.Minimize;
            double px = Ease(t, closing), py = px;
            if (_style == PanelAnimation.Unfold)
            {
                // Desplegar: primero a lo alto, después a lo ancho (al minimizar, al revés).
                double first = Math.Clamp(t / 0.6, 0, 1), second = Math.Clamp((t - 0.4) / 0.6, 0, 1);
                py = Ease(closing ? second : first, closing);
                px = Ease(closing ? first : second, closing);
            }
            var from = _from;
            var to = _to;
            if (_kind == Kind.Open)
            {
                // Abrir: desde un poco más chica, más abajo o más angosta, según la animación.
                from = _style switch
                {
                    PanelAnimation.Fade => Scale(_to, 0.96),
                    PanelAnimation.Zoom or PanelAnimation.Expand or PanelAnimation.Pop or PanelAnimation.Spring => Scale(_to, 0.82),
                    PanelAnimation.Unfold => Scale(_to, 0.6),
                    PanelAnimation.Drift => Shift(_to, 60, 0),
                    _ => Shift(Scale(_to, 0.96), 0, 50),
                };
            }
            else if (_style == PanelAnimation.Fade)
            {
                // Fundido: sin recorrido, la ventana se encoge apenas en su lugar.
                var whole = closing ? _from : _to;
                var small = Scale(whole, 0.94);
                (from, to) = closing ? (whole, small) : (small, whole);
            }
            else
            {
                // Hacia el ícono: con el ancho y alto de la ventana, encogida a lo que cabe en el botón.
                var icon = closing ? _to : _from;
                var window = closing ? _from : _to;
                var fitted = Fit(window, icon);
                (from, to) = closing ? (window, fitted) : (fitted, window);
            }
            double left = Lerp(from.Left, to.Left, px), right = Lerp(from.Right, to.Right, px);
            double top = Lerp(from.Top, to.Top, py), bottom = Lerp(from.Bottom, to.Bottom, py);
            double opacity = _kind switch
            {
                Kind.Minimize => 1 - Math.Clamp((t - 0.35) / 0.65, 0, 1),
                _ => Math.Clamp(t / 0.4, 0, 1),
            };
            var props = new DwmThumbnailProperties
            {
                Flags = DwmTnpVisible | DwmTnpRectDestination | DwmTnpOpacity,
                Destination = new NativeRect
                {
                    Left = (int)Math.Round(left - _box.Left),
                    Top = (int)Math.Round(top - _box.Top),
                    Right = (int)Math.Round(right - _box.Left),
                    Bottom = (int)Math.Round(bottom - _box.Top),
                },
                Opacity = (byte)Math.Round(opacity * 255),
                Visible = true,
            };
            DwmUpdateThumbnailProperties(_thumb, ref props);
        }

        /// <summary>Curva según la animación elegida (al minimizar, la que acelera hacia el final).</summary>
        private double Ease(double t, bool closing)
        {
            switch (_style)
            {
                case PanelAnimation.Spring or PanelAnimation.Pop:
                    {
                        // Se pasa un poco y vuelve (al minimizar, toma impulso antes de irse).
                        double s = _style == PanelAnimation.Spring ? 2.2 : 1.4;
                        if (closing)
                            return t * t * ((s + 1) * t - s);
                        double u = t - 1;
                        return u * u * ((s + 1) * u + s) + 1;
                    }
                case PanelAnimation.Zoom or PanelAnimation.Expand:
                    return closing ? t * t : 1 - (1 - t) * (1 - t);
                default:
                    return closing ? t * t * t : 1 - Math.Pow(1 - t, 3);
            }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static NativeRect Shift(NativeRect rect, int dx, int dy) =>
            new() { Left = rect.Left + dx, Top = rect.Top + dy, Right = rect.Right + dx, Bottom = rect.Bottom + dy };

        private static NativeRect Scale(NativeRect rect, double factor)
        {
            double cx = (rect.Left + rect.Right) / 2.0, cy = (rect.Top + rect.Bottom) / 2.0;
            double w = rect.Width * factor / 2, h = rect.Height * factor / 2;
            return new NativeRect { Left = (int)(cx - w), Top = (int)(cy - h), Right = (int)(cx + w), Bottom = (int)(cy + h) };
        }

        /// <summary>La ventana encogida (sin deformar) para caber en el botón, centrada en él.</summary>
        private static NativeRect Fit(NativeRect window, NativeRect icon)
        {
            double factor = Math.Min(icon.Width / (double)Math.Max(1, window.Width), icon.Height / (double)Math.Max(1, window.Height));
            double cx = (icon.Left + icon.Right) / 2.0, cy = (icon.Top + icon.Bottom) / 2.0;
            double w = window.Width * factor / 2, h = window.Height * factor / 2;
            return new NativeRect { Left = (int)(cx - w), Top = (int)(cy - h), Right = (int)(cx + w), Bottom = (int)(cy + h) };
        }

        public void Finish()
        {
            if (_finished)
                return;
            _finished = true;
            CompositionTarget.Rendering -= OnFrame;
            Show();
            if (_thumb != IntPtr.Zero)
                DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
            _window?.Close();
            _window = null;
            Done?.Invoke();
        }
    }

    // ── Win32 / DWM ─────────────────────────────────────────────────────────

    private const uint WinEventOutOfContext = 0x0000;
    private const int GwlStyle = -16, GwlExStyle = -20;
    private const int WsCaption = 0x00C00000;
    private const int WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000, WsExTransparent = 0x00000020, WsExLayered = 0x00080000;
    private const uint GaRoot = 2, LwaAlpha = 0x2;
    private const uint SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;
    private const uint RdwInvalidate = 0x0001, RdwAllChildren = 0x0080, RdwFrame = 0x0400;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const int DwmTnpRectDestination = 0x1, DwmTnpOpacity = 0x4, DwmTnpVisible = 0x8;

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length, Flags, ShowCmd;
        public int MinX, MinY, MaxX, MaxY;
        public NativeRect Normal;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public int Flags;
        public NativeRect Destination;
        public NativeRect Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DwmThumbnailProperties properties);
}
