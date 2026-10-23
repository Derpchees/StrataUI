using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Widgets;
using Forms = System.Windows.Forms;

namespace Strata.Shell.Widgets;

/// <summary>
/// Ventana de un widget: transparente, sin borde, fuera de la barra y de Alt+Tab, y siempre debajo
/// de las demás ventanas (encima del fondo de pantalla). Se mueve arrastrándola (salvo que esté
/// bloqueada) y guarda su lugar como fracción del área de trabajo de su monitor.
/// </summary>
public sealed class WidgetWindow : Window
{
    public WidgetConfig Config { get; private set; }
    private readonly FrameworkElement _content;
    private Point? _dragStart;
    private bool _dragged;

    public WidgetWindow(WidgetConfig config, FrameworkElement content)
    {
        Config = config;
        _content = content;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Strata UI widget";
        // Fondo casi invisible (no del todo): en una ventana con transparencia, los píxeles con alfa 0
        // dejan pasar el clic, y el widget no se podría arrastrar ni abrir su menú entre las letras.
        var hit = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        hit.Freeze();
        Content = new Border { Background = hit, Padding = new Thickness(12), Child = content };
        ContextMenu = BuildMenu();
        SourceInitialized += (_, _) => OnSourceInitialized();
        SizeChanged += (_, _) => PlaceFromConfig();
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        // Aparece con un fundido suave (al agregarlo o al abrir Strata UI).
        Loaded += (_, _) =>
        {
            if (Settings.Current.PanelAnimation != PanelAnimation.None)
                BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
        };
    }

    public void Update(WidgetConfig config)
    {
        Config = config;
        if (_content is IWidgetContent widget)
            widget.Apply(config);
        ContextMenu = BuildMenu();
        PlaceFromConfig();
    }

    private void OnSourceInitialized()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Ventana de herramientas sin activarse: no sale en Alt+Tab ni roba el foco al hacer clic.
        int style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        AttachToDesktop();
        PlaceFromConfig();
        SendToBottom();
    }

    /// <summary>
    /// La ventana del escritorio pasa a ser su dueña: una ventana con dueño siempre queda encima de
    /// él, así el widget sigue a la vista con "Mostrar escritorio" (Win+D) y debajo de las demás
    /// ventanas. Con el escritorio de Strata UI, la dueña es su capa en ese monitor (así el widget
    /// queda encima de los íconos). Se repite si el Explorador se reinicia o cambian las capas.
    /// </summary>
    public void AttachToDesktop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var owner = Services.Desktop.DesktopManager.OwnerFor(Config.Device);
        if (owner == IntPtr.Zero)
            owner = DesktopWindow();
        if (owner != IntPtr.Zero)
            SetWindowLongPtr(hwnd, GwlpHwndParent, owner);
    }

    /// <summary>La ventana que tiene los íconos del escritorio (Progman o, en algunas versiones, un WorkerW).</summary>
    internal static IntPtr DesktopWindow()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            return progman;
        IntPtr worker = IntPtr.Zero;
        while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            if (FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return worker;
        }
        return progman;
    }

    /// <summary>Cualquier intento de subirla la deja abajo de todo.</summary>
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmWindowPosChanging)
        {
            var pos = Marshal.PtrToStructure<WindowPos>(lParam);
            pos.InsertAfter = HwndBottom;
            pos.Flags &= ~SwpNoZOrder;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return IntPtr.Zero;
    }

    private void SendToBottom() =>
        SetWindowPos(new WindowInteropHelper(this).Handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    // ── Posición ────────────────────────────────────────────────────────────

    /// <summary>Pone la ventana donde dice su configuración (en píxeles del monitor, dentro del área de trabajo).</summary>
    public void PlaceFromConfig()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Config.Device) ?? Forms.Screen.PrimaryScreen!;
        var work = screen.WorkingArea;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int x = work.Left + (int)Math.Round(Math.Clamp(Config.X, 0, 1) * Math.Max(0, work.Width - width));
        int y = work.Top + (int)Math.Round(Math.Clamp(Config.Y, 0, 1) * Math.Max(0, work.Height - height));
        SetWindowPos(hwnd, HwndBottom, x, y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    /// <summary>Lee dónde quedó al arrastrarla y lo guarda (monitor y fracción del área de trabajo).</summary>
    private void SavePlacement()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(hwnd, out var rect))
            return;
        var center = new System.Drawing.Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        var screen = Forms.Screen.FromPoint(center);
        var work = screen.WorkingArea;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        double fx = work.Width > width ? (rect.Left - work.Left) / (double)(work.Width - width) : 0;
        double fy = work.Height > height ? (rect.Top - work.Top) / (double)(work.Height - height) : 0;
        WidgetManager.Save(Config.Id, c =>
        {
            c.Device = screen.DeviceName;
            c.X = Math.Clamp(fx, 0, 1);
            c.Y = Math.Clamp(fy, 0, 1);
        });
    }

    // ── Arrastrar ───────────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Config.Locked)
            return;
        _dragStart = e.GetPosition(this);
        _dragged = false;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            return;
        var now = e.GetPosition(this);
        var delta = now - start;
        if (!_dragged && Math.Abs(delta.X) < 3 && Math.Abs(delta.Y) < 3)
            return;
        _dragged = true;
        Left += delta.X;
        Top += delta.Y;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null)
            return;
        _dragStart = null;
        ReleaseMouseCapture();
        if (_dragged)
            SavePlacement();
    }

    // ── Menú ────────────────────────────────────────────────────────────────

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        if (Config.Kind == WidgetKind.NetSpeed)
        {
            // La prueba de velocidad también desde el widget (se ve en él mientras corre).
            var test = new MenuItem
            {
                Header = SpeedTest.Running ? Loc.T("Cancelar la prueba") : Loc.T("Probar velocidad"),
                FontWeight = FontWeights.SemiBold,
            };
            test.Click += (_, _) =>
            {
                if (SpeedTest.Running)
                    SpeedTest.Cancel();
                else
                    SpeedTest.Start();
            };
            // El menú se arma una vez: el texto se pone al abrirlo.
            menu.Opened += (_, _) => test.Header = SpeedTest.Running ? Loc.T("Cancelar la prueba") : Loc.T("Probar velocidad");
            menu.Items.Add(test);
            menu.Items.Add(new Separator());
        }
        var size = new MenuItem { Header = Loc.T("Tamaño") };
        foreach (var (name, scale) in new[] { (Loc.T("Chico"), 0.75), (Loc.T("Mediano"), 1.0), (Loc.T("Grande"), 1.5) })
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = Math.Abs(Config.Scale - scale) < 0.01 };
            item.Click += (_, _) => WidgetManager.Save(Config.Id, c => c.Scale = scale);
            size.Items.Add(item);
        }
        menu.Items.Add(size);
        if (Config.Kind == WidgetKind.Clock)
        {
            var date = new MenuItem { Header = Loc.T("Mostrar la fecha"), IsCheckable = true, IsChecked = Config.ShowDate };
            date.Click += (_, _) => WidgetManager.Save(Config.Id, c => c.ShowDate = !c.ShowDate);
            menu.Items.Add(date);
        }
        var locked = new MenuItem { Header = Loc.T("Bloquear posición"), IsCheckable = true, IsChecked = Config.Locked };
        locked.Click += (_, _) => WidgetManager.Save(Config.Id, c => c.Locked = !c.Locked);
        menu.Items.Add(locked);
        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = Loc.T("Widgets en Strata UI") };
        settings.Click += (_, _) => App.Current.OpenHub("Widgets");
        menu.Items.Add(settings);
        var remove = new MenuItem { Header = Loc.T("Quitar") };
        remove.Click += (_, _) => WidgetManager.Remove(Config.Id);
        menu.Items.Add(remove);
        return menu;
    }

    // ── Win32 ───────────────────────────────────────────────────────────────

    private const int GwlExStyle = -20, GwlpHwndParent = -8;
    private const int WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    private const int WmWindowPosChanging = 0x0046;
    private static readonly IntPtr HwndBottom = new(1);
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

/// <summary>Contenido de un widget que se rehace con su configuración (tamaño, fecha…).</summary>
public interface IWidgetContent
{
    void Apply(WidgetConfig config);
}
