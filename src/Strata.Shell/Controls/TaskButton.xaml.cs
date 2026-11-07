using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedShell.WindowsTasks;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Strata.Shell.Services.WebApps;

namespace Strata.Shell.Controls;

public partial class TaskButton : UserControl
{
    public TaskButton()
    {
        InitializeComponent();
        Root.SizeChanged += (_, _) => UpdateStackShape();
        MouseEnter += (_, _) =>
        {
            // La vista previa ya muestra el título: el tooltip la taparía.
            ToolTipService.SetIsEnabled(MainButton, !TaskPreviewWindow.Enabled(this));
            if (Window != null && Mouse.LeftButton != MouseButtonState.Pressed)
                TaskPreviewWindow.HoverStart(this, Members);
        };
        MouseLeave += (_, _) => TaskPreviewWindow.HoverEnd();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old)
                old.PropertyChanged -= OnWindowChanged;
            if (IsLoaded && e.NewValue is INotifyPropertyChanged now)
                now.PropertyChanged += OnWindowChanged;
            SyncSlot();
            UpdateIcon();
            UpdateGroup();
        };
        MainButton.ContextMenuOpening += (_, _) => UpdateMenu();
        Loaded += (_, _) =>
        {
            _bar = System.Windows.Window.GetWindow(this) as Taskbar;
            if (_bar != null)
            {
                FitTo(_bar.Position);
                _bar.TasksChanged += UpdateGroup;
                AnimateIfNew();
            }
            if (DataContext is INotifyPropertyChanged window)
                window.PropertyChanged += OnWindowChanged;
            WebSites.IconsChanged += UpdateIcon;
            AppIconOverrides.Changed += UpdateIcon;
            UpdateIcon();
            UpdateGroup();
        };
        Unloaded += (_, _) =>
        {
            if (_bar != null)
                _bar.TasksChanged -= UpdateGroup;
            if (DataContext is INotifyPropertyChanged window)
                window.PropertyChanged -= OnWindowChanged;
            if (_watchedLeader != null)
                _watchedLeader.PropertyChanged -= OnWindowChanged;
            _watchedLeader = null;
            WebSites.IconsChanged -= UpdateIcon;
            AppIconOverrides.Changed -= UpdateIcon;
            WatchButtonRect([]);
        };
    }

    private Taskbar? _bar;

    /// <summary>
    /// Cuándo tuvo botón cada ventana por primera vez. La lista se regenera seguido (al agrupar, justo
    /// después de que llega una ventana nueva): el botón que reemplaza al recién creado también se
    /// anima si la ventana llegó hace poco; después, ya no.
    /// </summary>
    private static readonly Dictionary<IntPtr, DateTime> FirstSeen = new();
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    /// <summary>
    /// Una ventana recién abierta entra con la animación elegida, desde el borde de la barra (no al
    /// arrancar Strata UI, cuando aparecen todas juntas). Al cerrarse, la barra hace la salida
    /// (<see cref="Taskbar"/>, con una imagen del botón).
    /// </summary>
    private void AnimateIfNew()
    {
        if (Window is not { } window)
            return;
        var now = DateTime.UtcNow;
        if (!FirstSeen.TryGetValue(window.Handle, out var first))
        {
            FirstSeen[window.Handle] = first = now;
            // Las ventanas cerradas no se borran solas: se limpian de vez en cuando.
            if (FirstSeen.Count > 500)
                foreach (var old in FirstSeen.Where(p => now - p.Value > TimeSpan.FromMinutes(5)).Select(p => p.Key).ToList())
                    FirstSeen.Remove(old);
        }
        if (now - first > TimeSpan.FromMilliseconds(400) || now - StartedAt < TimeSpan.FromSeconds(3))
            return;
        Motion.In(Root, IconHost, _bar?.Position ?? TaskbarPosition.Bottom, 0.8);
    }

    private void OnWindowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApplicationWindow.Icon) or nameof(ApplicationWindow.Title))
            Dispatcher.BeginInvoke(UpdateIcon);
        else if (e.PropertyName == nameof(PinSlot.Leader))
            Dispatcher.BeginInvoke(() =>
            {
                SyncSlot();
                UpdateIcon();
                UpdateGroup();
            });
    }

    /// <summary>Botón de una app anclada (DataContext <see cref="PinSlot"/>); null en las ventanas sueltas.</summary>
    public PinSlot? Slot => DataContext as PinSlot;

    /// <summary>
    /// Un anclado se ve como su primera ventana si está abierta (las plantillas leen su estado y su
    /// título) y como lanzador si no: sin indicador y con el nombre de la app.
    /// </summary>
    private void SyncSlot()
    {
        if (Slot is not { } slot)
        {
            MainButton.ClearValue(DataContextProperty);
            Indicator.ClearValue(VisibilityProperty);
            return;
        }
        MainButton.DataContext = (object?)slot.Leader ?? slot;
        Indicator.Visibility = slot.Leader is null ? Visibility.Collapsed : Visibility.Visible;
        if (!ReferenceEquals(slot.Leader, _watchedLeader))
        {
            if (_watchedLeader != null)
                _watchedLeader.PropertyChanged -= OnWindowChanged;
            _watchedLeader = slot.Leader;
            if (_watchedLeader != null)
                _watchedLeader.PropertyChanged += OnWindowChanged;
        }
    }

    private INotifyPropertyChanged? _watchedLeader;

    /// <summary>Opciones del clic derecho según sea un anclado, una ventana o las dos cosas.</summary>
    private void UpdateMenu()
    {
        bool running = Window != null;
        string? pinId = Slot?.Id ?? (Window is { } window ? TaskbarPins.IdFor(window) : null);
        bool pinned = pinId != null && TaskbarPins.IsPinned(pinId);
        // Sin anclar también se puede abrir otra ventana: con su ejecutable.
        LaunchItem.Visibility = Slot != null || Exe() != null ? Visibility.Visible : Visibility.Collapsed;
        LaunchItem.Header = running ? Loc.T("Abrir otra ventana") : Loc.T("Abrir");
        var privateWindow = WebSites.PrivateWindow(Exe());
        PrivateItem.Visibility = privateWindow != null ? Visibility.Visible : Visibility.Collapsed;
        PrivateItem.Header = privateWindow?.Label;
        PinItem.Visibility = pinId != null ? Visibility.Visible : Visibility.Collapsed;
        PinItem.Header = pinned ? Loc.T("Desanclar de la barra de tareas") : Loc.T("Anclar a la barra de tareas");
        PinItem.Tag = pinId;
        WindowSeparator.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CloseItem.Visibility = ForceCloseItem.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Ejecutable de la app: el de su ventana o, anclada y cerrada, el de su acceso directo.</summary>
    private string? Exe() =>
        Window?.WinFileName is { Length: > 0 } path ? (Launchable(path) ? path : null)
        : Slot is { } slot ? TaskbarPins.TargetExe(slot.Id) : null;

    /// <summary>
    /// Si abrir el ejecutable de una ventana abre la app: no el de Strata UI (sus ventanas de
    /// Archivos), ni el anfitrión de las apps de la Tienda, ni uno empaquetado (Windows no deja).
    /// </summary>
    private static bool Launchable(string path) =>
        !string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(System.IO.Path.GetFileName(path), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)
        && !path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (Slot != null)
            Slot.Launch();
        else if (Exe() is { } exe)
            Start(exe, WebSites.IsBrowserExe(exe) ? "--new-window" : "");
    }

    private void PrivateWindow_Click(object sender, RoutedEventArgs e)
    {
        if (Exe() is { } exe && WebSites.PrivateWindow(exe) is { } privateWindow)
            Start(exe, privateWindow.Argument);
    }

    private static void Start(string exe, string arguments)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Info($"No se pudo abrir {exe}: {ex.Message}");
        }
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (PinItem.Tag is not string id)
            return;
        if (TaskbarPins.IsPinned(id))
            TaskbarPins.Unpin(id);
        else
            TaskbarPins.Pin(id);
    }

    /// <summary>Ícono de la app o, si es una pestaña de un sitio conocido, el del sitio con el navegador en la esquina.</summary>
    private void UpdateIcon()
    {
        if (Slot is { } slot)
        {
            // Anclado: el ícono de la app (el elegido en Inicio, si se cambió), abierta o no.
            AppIcon.Source = AppIconOverrides.For(Window) ?? (Window is null ? AppIconOverrides.ForPin(slot.Id) : null) ?? slot.Icon ?? Window?.Icon;
            BrowserBadge.Visibility = Visibility.Collapsed;
            return;
        }
        if (Window is not { } window)
            return;
        var site = TaskGroups.Site(window);
        var siteIcon = site is null ? null : WebSites.Icon(site.Domain);
        AppIcon.Source = siteIcon ?? AppIconOverrides.For(window) ?? window.Icon;
        BrowserBadge.Source = siteIcon != null ? window.Icon : null;
        BrowserBadge.Visibility = siteIcon != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private readonly List<ApplicationWindow> _rectWatched = [];

    /// <summary>
    /// Al minimizar o restaurar una ventana, Windows pregunta a la barra dónde está su botón y la anima
    /// hacia él (o desde él). Sin esto, la ventana se iba a una esquina de la pantalla.
    /// </summary>
    private void WatchButtonRect(IReadOnlyList<ApplicationWindow> members)
    {
        foreach (var window in _rectWatched.Except(members).ToList())
        {
            window.GetButtonRect -= OnGetButtonRect;
            _rectWatched.Remove(window);
        }
        foreach (var window in members.Except(_rectWatched).ToList())
        {
            window.GetButtonRect += OnGetButtonRect;
            _rectWatched.Add(window);
        }
    }

    private void OnGetButtonRect(ref ManagedShell.Interop.NativeMethods.ShortRect rect)
    {
        if (!IsVisible || PresentationSource.FromVisual(this) is null)
            return;
        try
        {
            var topLeft = PointToScreen(new Point(0, 0));
            var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            // Con una barra en cada monitor, la del monitor de la ventana manda (si ya hay otra, no se pisa).
            bool filled = rect.Right > rect.Left;
            if (filled && _bar != null && Window is { } window
                && System.Windows.Forms.Screen.FromHandle(window.Handle).DeviceName
                   != System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)topLeft.X, (int)topLeft.Y)).DeviceName)
                return;
            rect.Left = (short)topLeft.X;
            rect.Top = (short)topLeft.Y;
            rect.Right = (short)bottomRight.X;
            rect.Bottom = (short)bottomRight.Y;
        }
        catch (InvalidOperationException)
        {
            // El botón dejó la ventana justo ahora: Windows usa su posición de siempre.
        }
    }

    /// <summary>Ventanas de este botón: la suya o, con las ventanas agrupadas, todas las de la app.</summary>
    private IReadOnlyList<ApplicationWindow> Members() =>
        Window is not { } window ? [] : _bar?.GroupMembers(window) ?? [window];

    /// <summary>
    /// Con varias ventanas en el botón: se ve apilado como en Windows (la placa del botón con el borde
    /// de otra asomando detrás, igual desde 2 ventanas) y el indicador sigue a la que esté activa (o
    /// pida atención). Con una sola, el indicador sigue el estado de la ventana (estilo del XAML).
    /// </summary>
    private void UpdateGroup()
    {
        var members = Members();
        WatchButtonRect(IsLoaded ? members : []);
        bool grouped = members.Count > 1;
        _layers = Math.Min(members.Count - 1, 2);
        StackEdge.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
        StackEdge2.Visibility = _layers == 2 ? Visibility.Visible : Visibility.Collapsed;
        CloseItem.Header = grouped ? Loc.T("Cerrar todas las ventanas") : Loc.T("Cerrar ventana");
        if (!grouped)
        {
            ActiveBackground.ClearValue(VisibilityProperty);
            ActiveBackground.ClearValue(OpacityProperty);
            ActiveBackground.ClearValue(MarginProperty);
            IconHost.ClearValue(MarginProperty);
            Indicator.ClearValue(WidthProperty);
            Indicator.ClearValue(Border.BackgroundProperty);
            return;
        }
        bool active = members.Any(w => w.State == ApplicationWindow.WindowState.Active && !w.IsMinimized);
        bool flashing = members.Any(w => w.State == ApplicationWindow.WindowState.Flashing);
        // La placa siempre a la vista (más tenue si ninguna está activa), dejando sitio al borde de atrás.
        ActiveBackground.Visibility = Visibility.Visible;
        ActiveBackground.Opacity = active ? 1 : 0.5;
        // Del color del texto: bien visible, la de más atrás un poco más tenue.
        StackEdge.Opacity = active ? 0.45 : 0.32;
        StackEdge2.Opacity = StackEdge.Opacity * 0.6;
        double offset = StackOffset * _layers;
        ActiveBackground.Margin = _vertical ? new Thickness(0, 0, 0, offset) : new Thickness(0, 0, offset, 0);
        IconHost.Margin = ActiveBackground.Margin; // centrado sobre la placa
        UpdateStackShape();
        Indicator.Width = active || flashing ? 16 : 6;
        if (flashing && !active)
            Indicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xA3, 0x3D));
        else
            Indicator.SetResourceReference(Border.BackgroundProperty, active ? "StrataAccentBrush" : "StrataInactiveIndicatorBrush");
    }

    private const double StackOffset = 4;
    private bool _vertical;
    // Tarjetas que asoman detrás de la placa: 1 con dos ventanas, 2 con tres o más.
    private int _layers;

    /// <summary>
    /// Las tarjetas de atrás: del tamaño de la placa, cada una corrida hacia el lado (abajo en vertical)
    /// y un poco más baja que la de adelante; solo se dibuja lo que asoma, para que se lea como una pila.
    /// </summary>
    private void UpdateStackShape()
    {
        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (StackEdge.Visibility != Visibility.Visible || w <= 0 || h <= 0)
            return;
        double r = ActiveBackground.CornerRadius.TopLeft;
        const double inset = 4;
        double front = StackOffset * _layers;
        Rect Card(int layer)
        {
            double shift = StackOffset * layer, squeeze = inset * layer;
            return _vertical
                ? new Rect(squeeze, shift, w - 2 * squeeze, h - front)
                : new Rect(shift, squeeze, w - front, h - 2 * squeeze);
        }
        System.Windows.Media.Geometry Edge(int layer) => new System.Windows.Media.CombinedGeometry(
            System.Windows.Media.GeometryCombineMode.Exclude,
            new System.Windows.Media.RectangleGeometry(Card(layer), r, r),
            new System.Windows.Media.RectangleGeometry(Card(layer - 1), r, r));
        StackEdge.Data = Edge(1);
        StackEdge2.Data = _layers == 2 ? Edge(2) : null;
    }

    /// <summary>
    /// El indicador va junto al borde de la pantalla, como en Windows; en barras verticales se gira y el
    /// botón toma un alto fijo (en las horizontales lo da el alto de la barra).
    /// </summary>
    private void FitTo(TaskbarPosition position)
    {
        _vertical = position is TaskbarPosition.Left or TaskbarPosition.Right;
        switch (position)
        {
            case TaskbarPosition.Top:
                Indicator.VerticalAlignment = VerticalAlignment.Top;
                Indicator.Margin = new Thickness(0, 2, 0, 0);
                break;
            case TaskbarPosition.Left or TaskbarPosition.Right:
                Root.Height = 40;
                // Girado 90° el indicador pasa de horizontal a vertical.
                Indicator.LayoutTransform = new System.Windows.Media.RotateTransform(90);
                Indicator.VerticalAlignment = VerticalAlignment.Center;
                Indicator.HorizontalAlignment = position == TaskbarPosition.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                Indicator.Margin = position == TaskbarPosition.Left ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 2, 0);
                break;
        }
    }

    private ApplicationWindow? Window => DataContext as ApplicationWindow ?? Slot?.Leader;

    /// <summary>Se empezó a arrastrar el botón (para ordenar las ventanas).</summary>
    public static event Action<TaskButton>? DragStarted;

    private Point? _pressedAt;
    // La ventana estaba al frente al presionar: al soltar puede que ya no (el clic en la barra le
    // quita el primer plano) y el botón la traería al frente en vez de minimizarla.
    private bool _activeAtPress;
    private IntPtr _foregroundAtPress;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _pressedAt = e.GetPosition(this);
        _foregroundAtPress = GetForegroundWindow();
        _activeAtPress = Window is { IsMinimized: false } window
                         && (window.State == ApplicationWindow.WindowState.Active || window.Handle == GetForegroundWindow());
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedAt = null;
            return;
        }
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _pressedAt = null;
            TaskPreviewWindow.Dismiss();
            DragStarted?.Invoke(this);
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        // Anclado cerrado (o Mayús+clic, como en Windows): abre la app.
        if (Slot is { } slot && (Window is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            TaskPreviewWindow.Dismiss();
            slot.Launch();
            return;
        }
        if (Window is not { } window)
            return;
        var members = Members();
        TaskPreviewWindow.Dismiss();
        if (members.Count > 1)
        {
            SwitchWithin(members);
            return;
        }

        // Como en Windows: clic en la ventana activa la minimiza; en otra, la trae al frente.
        bool active = _activeAtPress || window.State == ApplicationWindow.WindowState.Active && !window.IsMinimized;
        _activeAtPress = false;
        if (active)
            window.Minimize();
        else
            window.BringToFront();
    }

    /// <summary>
    /// Varias ventanas en el botón: un clic va directo a una, sin pasar por la vista previa (que sigue
    /// al pasar el mouse, para elegir). Si ninguna está al frente, trae la última que se usó; si una
    /// lo está, la siguiente del botón, en el orden de la vista previa, y así en cada clic.
    /// </summary>
    private void SwitchWithin(IReadOnlyList<ApplicationWindow> members)
    {
        IntPtr foreground = _foregroundAtPress;
        _foregroundAtPress = IntPtr.Zero;
        _activeAtPress = false;
        int current = -1;
        for (int i = 0; i < members.Count && current < 0; i++)
            if (!members[i].IsMinimized && (members[i].Handle == foreground || members[i].State == ApplicationWindow.WindowState.Active
                                            || Taskbar.WasLastActive(members[i])))
                current = i;
        var target = current < 0 ? Taskbar.MostRecent(members) : members[(current + 1) % members.Count];
        target.BringToFront();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        foreach (var window in Members())
            window.Close();
    }

    /// <summary>Como "Finalizar tarea" de Windows: termina los procesos de las ventanas del botón.</summary>
    private void ForceClose_Click(object sender, RoutedEventArgs e)
    {
        foreach (var window in Members())
        {
            // El Explorador es también el escritorio: sus ventanas solo se cierran.
            if (TaskActions.ForceClose(window.Handle) == false)
                window.Close();
        }
    }
}
