using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Strata.Shell.Services;
using Strata.Shell.Services.Brightness;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Notifications;

/// <summary>Qué aviso en pantalla es (cada monitor elige cuáles muestra en Hub › Paneles).</summary>
public enum OsdKind { Volume, Mute, Brightness, CapsLock, NumLock, ScrollLock }

/// <summary>
/// Aviso en pantalla (OSD) al cambiar el volumen, silenciar, cambiar el brillo o pulsar Bloq Mayús,
/// Bloq Num y Bloq Despl. Cada monitor con barra tiene el suyo, junto a su barra, y muestra solo los
/// avisos que tenga activados; sin foco ni clics (no interrumpe lo que se hace) y se va solo.
/// </summary>
public partial class OsdWindow : Window
{
    private const int GapPx = 12;
    private static readonly TimeSpan Visible = TimeSpan.FromMilliseconds(1600);

    private readonly DispatcherTimer _hideTimer;
    private readonly string _device;
    private TaskbarPosition _edge = TaskbarPosition.Bottom;
    private bool _hiding;

    private OsdWindow(string device)
    {
        _device = device;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            // Sin activar, fuera de Alt+Tab y transparente a los clics.
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow | WsExTransparent);
        };
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(Place, DispatcherPriority.Render);
        _hideTimer = new DispatcherTimer { Interval = Visible };
        _hideTimer.Tick += (_, _) => HideAnimated();
    }

    private static readonly Dictionary<string, OsdWindow> Instances = new();

    /// <summary>
    /// Muestra un nivel (0–100) con su ícono (volumen, brillo) en los monitores que tienen activado
    /// ese aviso; <paramref name="device"/> lo limita a un monitor (el brillo de ese monitor).
    /// </summary>
    public static void ShowLevel(OsdKind kind, string glyph, int percent, bool dimmed = false, string? device = null)
    {
        foreach (var osd in Targets(kind, device))
        {
            osd.Glyph.Text = glyph;
            osd.LevelPanel.Visibility = Visibility.Visible;
            osd.Percent.Visibility = Visibility.Visible;
            osd.Message.Visibility = Visibility.Collapsed;
            percent = Math.Clamp(percent, 0, 100);
            osd.Percent.Text = percent.ToString();
            osd.LevelPanel.Opacity = dimmed ? 0.4 : 1;
            var target = osd;
            osd.Dispatcher.BeginInvoke(() => target.LevelFill.Width = target.LevelPanel.ActualWidth * percent / 100,
                                       DispatcherPriority.Loaded);
            osd.Present();
        }
    }

    /// <summary>Muestra un mensaje con su ícono (Bloq Mayús activado…) en los monitores que tienen activado ese aviso.</summary>
    public static void ShowMessage(OsdKind kind, string glyph, string text)
    {
        foreach (var osd in Targets(kind, null))
            osd.ShowText(glyph, text);
    }

    /// <summary>Mensaje en el monitor dado, sin importar los avisos elegidos (respuesta a algo que pidió el usuario).</summary>
    public static void ShowMessage(string glyph, string text, string device) => Prepare(device).ShowText(glyph, text);

    private void ShowText(string glyph, string text)
    {
        Glyph.Text = glyph;
        LevelPanel.Visibility = Visibility.Collapsed;
        Percent.Visibility = Visibility.Collapsed;
        Message.Visibility = Visibility.Visible;
        Message.Text = text;
        Present();
    }

    public static void CloseAll()
    {
        foreach (var osd in Instances.Values.ToList())
            osd.Close();
        Instances.Clear();
    }

    /// <summary>Un aviso por monitor con barra que tenga activado ese tipo.</summary>
    private static IEnumerable<OsdWindow> Targets(OsdKind kind, string? device) =>
        App.Current.Taskbars.Select(t => t.Device).Distinct()
            .Where(d => (device is null || d == device) && Wants(Settings.Current.For(d), kind))
            .Select(Prepare)
            .ToList();

    internal static bool Wants(ScreenSettings s, OsdKind kind) => kind switch
    {
        OsdKind.Volume => s.OsdVolume,
        OsdKind.Mute => s.OsdMute,
        OsdKind.Brightness => s.OsdBrightness,
        OsdKind.CapsLock => s.OsdCapsLock,
        OsdKind.NumLock => s.OsdNumLock,
        _ => s.OsdScrollLock,
    };

    private static OsdWindow Prepare(string device)
    {
        if (!Instances.TryGetValue(device, out var osd))
            Instances[device] = osd = new OsdWindow(device);
        return osd;
    }

    private void Present()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
        bool appearing = !IsVisible || _hiding;
        _hiding = false;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        MoveToScreen();
        // Mientras ya se ve, solo cambia el contenido: animar de nuevo cada paso de volumen marearía.
        if (appearing)
            Dispatcher.BeginInvoke(() => Motion.In(this, Card, _edge), DispatcherPriority.Loaded);
    }

    private void HideAnimated()
    {
        _hideTimer.Stop();
        if (!IsVisible)
            return;
        _hiding = true;
        Motion.Out(this, Card, _edge, () =>
        {
            if (_hiding)
            {
                _hiding = false;
                Hide();
            }
        });
    }

    // ── Posición: junto a la barra de su monitor, centrado en su borde ──────────────

    /// <summary>La barra de este monitor junto a la que va: la del reloj, la principal o la primera.</summary>
    private Taskbar? Bar()
    {
        var bars = App.Current.Taskbars.Where(t => t.Device == _device).ToList();
        return bars.FirstOrDefault(t => t.Has(BarItem.Clock)) ?? bars.FirstOrDefault(t => t.IsMain) ?? bars.FirstOrDefault();
    }

    private (Forms.Screen Screen, TaskbarPosition Edge) Target()
    {
        if (Bar() is { } bar)
        {
            var center = bar.PointToScreen(new Point(bar.ActualWidth / 2, bar.ActualHeight / 2));
            return (Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y)), bar.Position);
        }
        return (Forms.Screen.PrimaryScreen!, TaskbarPosition.Bottom);
    }

    private void MoveToScreen()
    {
        var (screen, edge) = Target();
        _edge = edge;
        var work = screen.WorkingArea;
        // Primero al monitor (WPF toma su escala) y luego a su lugar, con el tamaño ya definitivo.
        SetWindowPos(Handle, IntPtr.Zero, work.Left + GapPx, work.Top + GapPx, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        Dispatcher.BeginInvoke(Place, DispatcherPriority.Render);
    }

    private void Place()
    {
        if (!IsVisible)
            return;
        var (screen, edge) = Target();
        var work = screen.WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width = (int)Math.Ceiling(ActualWidth * scale), height = (int)Math.Ceiling(ActualHeight * scale);
        // La ventana ya trae margen para la sombra: con eso basta de separación de la barra.
        int gap = (int)(8 * scale);
        int x = edge switch
        {
            TaskbarPosition.Left => work.Left + gap,
            TaskbarPosition.Right => work.Right - width - gap,
            _ => work.Left + (work.Width - width) / 2,
        };
        int y = edge switch
        {
            TaskbarPosition.Top => work.Top + gap,
            TaskbarPosition.Bottom => work.Bottom - height - gap * 4,
            _ => work.Top + (work.Height - height) / 2,
        };
        SetWindowPos(Handle, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    // ── Win32 ───────────────────────────────────────────────────────────────

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080, WsExTransparent = 0x00000020;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

/// <summary>
/// Qué dispara el OSD: el volumen y el silencio del dispositivo de salida, el brillo de cada
/// monitor y las teclas de bloqueo. No se muestra si el panel correspondiente ya está abierto
/// (ahí se ve el cambio) ni con la opción desactivada en el Hub.
/// </summary>
public static class Osd
{
    private static int _volume = -1;
    private static bool _mute;
    private static string? _device;
    private static DateTime _quietUntil;

    public static void Initialize()
    {
        // Al arrancar se leen los valores actuales: no son cambios del usuario.
        _quietUntil = DateTime.UtcNow.AddSeconds(3);
        App.Current.Audio.Changed += OnAudioChanged;
        OnAudioChanged();
        BrightnessService.Monitors.CollectionChanged += (_, e) => WatchBrightness();
        WatchBrightness();
        Services.StartMenu.WinKeyHook.LockKeyPressed += OnLockKey;
        Services.StartMenu.WinKeyHook.VolumeKeyPressed += OnVolumeKey;
    }

    public static void Shutdown()
    {
        App.Current.Audio.Changed -= OnAudioChanged;
        Services.StartMenu.WinKeyHook.LockKeyPressed -= OnLockKey;
        Services.StartMenu.WinKeyHook.VolumeKeyPressed -= OnVolumeKey;
        OsdWindow.CloseAll();
    }

    /// <summary>Fuera de los primeros segundos (al arrancar se leen los valores, no son cambios). Qué
    /// monitores lo muestran lo decide cada uno (<see cref="OsdWindow.Wants"/>).</summary>
    private static bool Enabled => DateTime.UtcNow > _quietUntil;

    private static void OnAudioChanged() => OnAudioChanged(force: false);

    private static void OnAudioChanged(bool force)
    {
        var audio = App.Current.Audio;
        int volume = (int)Math.Round(audio.Volume * 100);
        bool mute = audio.Mute;
        string? device = audio.Output.DeviceId;
        bool changed = force || device == _device && (volume != _volume || mute != _mute);
        var kind = mute != _mute ? OsdKind.Mute : OsdKind.Volume;
        _volume = volume;
        _mute = mute;
        _device = device; // cambiar de dispositivo no es cambiar el volumen
        if (!changed || !audio.HasDevice || !Enabled || Flyouts.FlyoutManager.IsOpen<Flyouts.VolumeFlyout>())
            return;
        string glyph = mute ? "\uE74F"
            : volume == 0 ? "\uE992"
            : volume < 33 ? "\uE993"
            : volume < 66 ? "\uE994"
            : "\uE995";
        OsdWindow.ShowLevel(kind, glyph, volume, dimmed: mute);
    }

    /// <summary>Teclas de volumen: lo mismo que hace Windows (pasos de 2, subir quita el silencio).</summary>
    private static void OnVolumeKey(int vk)
    {
        var audio = App.Current.Audio;
        if (!audio.HasDevice)
            return;
        _quietUntil = DateTime.MinValue; // pulsar una tecla siempre muestra el aviso
        if (vk == 0xAD)
        {
            audio.Mute = !audio.Mute;
            return;
        }
        int step = vk == 0xAF ? 2 : -2;
        int percent = (int)Math.Round(audio.Volume * 100);
        int next = Math.Clamp((int)Math.Round((percent + step) / 2.0) * 2, 0, 100);
        if (step > 0 && audio.Mute)
            audio.Mute = false;
        audio.Volume = next / 100f;
        if (next == percent)
            OnAudioChanged(force: true); // ya en 0 o en 100: el aviso igual aparece
    }

    // ── Brillo ──────────────────────────────────────────────────────────────

    private static readonly HashSet<BrightnessMonitor> Watched = new();

    private static void WatchBrightness()
    {
        foreach (var monitor in BrightnessService.Monitors)
        {
            if (Watched.Add(monitor))
                monitor.PropertyChanged += OnBrightnessChanged;
        }
    }

    private static void OnBrightnessChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrightnessMonitor.Percent) || sender is not BrightnessMonitor monitor
            || !Enabled || Flyouts.FlyoutManager.IsOpen<Flyouts.BrightnessFlyout>())
            return;
        // Solo en el monitor cuyo brillo cambi\u00F3 (si tiene barra y el aviso activado).
        OsdWindow.ShowLevel(OsdKind.Brightness, "\uE706", monitor.Percent, device: monitor.Device);
    }

    // ── Teclas de bloqueo ───────────────────────────────────────────────────

    private static void OnLockKey(int vk)
    {
        if (!Enabled)
            return;
        // El estado ya cambió cuando se suelta la tecla. GetKeyState lo da bien aunque Strata UI no
        // tenga el foco (el WPF de Keyboard.IsKeyToggled puede quedarse con el anterior).
        bool on = (GetKeyState(vk) & 1) != 0;
        switch (vk)
        {
            case 0x14:
                OsdWindow.ShowMessage(OsdKind.CapsLock, "\uE84A", on ? Loc.T("Bloq Mayús activado") : Loc.T("Bloq Mayús desactivado"));
                break;
            case 0x90:
                OsdWindow.ShowMessage(OsdKind.NumLock, "\uE765", on ? Loc.T("Bloq Num activado") : Loc.T("Bloq Num desactivado"));
                break;
            case 0x91:
                OsdWindow.ShowMessage(OsdKind.ScrollLock, "\uE765", on ? Loc.T("Bloq Despl activado") : Loc.T("Bloq Despl desactivado"));
                break;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vk);
}
