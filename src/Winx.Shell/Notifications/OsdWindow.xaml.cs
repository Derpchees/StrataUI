using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Winx.Shell.Services;
using Winx.Shell.Services.Brightness;
using Winx.Shell.Services.Localization;
using Winx.Shell.Services.Themes;
using Forms = System.Windows.Forms;

namespace Winx.Shell.Notifications;

/// <summary>
/// Aviso en pantalla (OSD) al cambiar el volumen, silenciar, cambiar el brillo o pulsar Bloq Mayús,
/// Bloq Num y Bloq Despl. Aparece junto a la barra principal de WINX, sin foco ni clics (no
/// interrumpe lo que se hace) y se va solo al poco tiempo.
/// </summary>
public partial class OsdWindow : Window
{
    private const int GapPx = 12;
    private static readonly TimeSpan Visible = TimeSpan.FromMilliseconds(1600);

    private readonly DispatcherTimer _hideTimer;
    private TaskbarPosition _edge = TaskbarPosition.Bottom;
    private bool _hiding;

    private OsdWindow()
    {
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

    private static OsdWindow? _instance;

    /// <summary>Muestra un nivel (0–100) con su ícono: volumen, brillo.</summary>
    public static void ShowLevel(string glyph, int percent, bool dimmed = false)
    {
        var osd = Prepare();
        osd.Glyph.Text = glyph;
        osd.LevelPanel.Visibility = Visibility.Visible;
        osd.Percent.Visibility = Visibility.Visible;
        osd.Message.Visibility = Visibility.Collapsed;
        percent = Math.Clamp(percent, 0, 100);
        osd.Percent.Text = percent.ToString();
        osd.LevelPanel.Opacity = dimmed ? 0.4 : 1;
        osd.Dispatcher.BeginInvoke(() => osd.LevelFill.Width = osd.LevelPanel.ActualWidth * percent / 100,
                                   DispatcherPriority.Loaded);
        osd.Present();
    }

    /// <summary>Muestra un mensaje con su ícono: Bloq Mayús activado…</summary>
    public static void ShowMessage(string glyph, string text)
    {
        var osd = Prepare();
        osd.Glyph.Text = glyph;
        osd.LevelPanel.Visibility = Visibility.Collapsed;
        osd.Percent.Visibility = Visibility.Collapsed;
        osd.Message.Visibility = Visibility.Visible;
        osd.Message.Text = text;
        osd.Present();
    }

    public static void CloseAll()
    {
        _instance?.Close();
        _instance = null;
    }

    private static OsdWindow Prepare() => _instance ??= new OsdWindow();

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

    // ── Posición: junto a la barra principal de WINX, centrado en su borde ─────────

    private (Forms.Screen Screen, TaskbarPosition Edge) Target()
    {
        if (App.Current.OsdTaskbar is { } bar)
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

    private static bool Enabled => Settings.Current.ShowOsd && DateTime.UtcNow > _quietUntil;

    private static void OnAudioChanged() => OnAudioChanged(force: false);

    private static void OnAudioChanged(bool force)
    {
        var audio = App.Current.Audio;
        int volume = (int)Math.Round(audio.Volume * 100);
        bool mute = audio.Mute;
        string? device = audio.Output.DeviceId;
        bool changed = force || device == _device && (volume != _volume || mute != _mute);
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
        OsdWindow.ShowLevel(glyph, volume, dimmed: mute);
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
        OsdWindow.ShowLevel("\uE706", monitor.Percent);
    }

    // ── Teclas de bloqueo ───────────────────────────────────────────────────

    private static void OnLockKey(int vk)
    {
        if (!Enabled)
            return;
        // El estado ya cambió cuando se suelta la tecla. GetKeyState lo da bien aunque WINX no
        // tenga el foco (el WPF de Keyboard.IsKeyToggled puede quedarse con el anterior).
        bool on = (GetKeyState(vk) & 1) != 0;
        switch (vk)
        {
            case 0x14:
                OsdWindow.ShowMessage("\uE84A", on ? Loc.T("Bloq Mayús activado") : Loc.T("Bloq Mayús desactivado"));
                break;
            case 0x90:
                OsdWindow.ShowMessage("\uE765", on ? Loc.T("Bloq Num activado") : Loc.T("Bloq Num desactivado"));
                break;
            case 0x91:
                OsdWindow.ShowMessage("\uE765", on ? Loc.T("Bloq Despl activado") : Loc.T("Bloq Despl desactivado"));
                break;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vk);
}
