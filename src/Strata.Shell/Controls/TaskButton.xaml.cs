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
        LaunchItem.Visibility = Slot != null ? Visibility.Visible : Visibility.Collapsed;
        LaunchItem.Header = running ? Loc.T("Abrir otra ventana") : Loc.T("Abrir");
        PinItem.Visibility = pinId != null ? Visibility.Visible : Visibility.Collapsed;
        PinItem.Header = pinned ? Loc.T("Desanclar de la barra de tareas") : Loc.T("Anclar a la barra de tareas");
        PinItem.Tag = pinId;
        WindowSeparator.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CloseItem.Visibility = ForceCloseItem.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Launch_Click(object sender, RoutedEventArgs e) => Slot?.Launch();

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
            AppIcon.Source = slot.Icon ?? Window?.Icon;
            BrowserBadge.Visibility = Visibility.Collapsed;
            return;
        }
        if (Window is not { } window)
            return;
        var site = TaskGroups.Site(window);
        var siteIcon = site is null ? null : WebSites.Icon(site.Domain);
        AppIcon.Source = siteIcon ?? window.Icon;
        BrowserBadge.Source = siteIcon != null ? window.Icon : null;
        BrowserBadge.Visibility = siteIcon != null ? Visibility.Visible : Visibility.Collapsed;
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
        bool grouped = members.Count > 1;
        StackEdge.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
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
        StackEdge.Opacity = ActiveBackground.Opacity * 0.6;
        ActiveBackground.Margin = _vertical ? new Thickness(0, 0, 0, StackOffset) : new Thickness(0, 0, StackOffset, 0);
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

    /// <summary>
    /// La tarjeta de atrás: del mismo tamaño que la placa, corrida hacia el lado (abajo en vertical) y
    /// un poco más baja; solo se dibuja lo que asoma, pegado a la placa, para que se lea como una pila.
    /// </summary>
    private void UpdateStackShape()
    {
        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (StackEdge.Visibility != Visibility.Visible || w <= 0 || h <= 0)
            return;
        double r = ActiveBackground.CornerRadius.TopLeft;
        const double inset = 5;
        var front = _vertical ? new Rect(0, 0, w, h - StackOffset) : new Rect(0, 0, w - StackOffset, h);
        var back = _vertical
            ? new Rect(inset, StackOffset, w - 2 * inset, h - StackOffset)
            : new Rect(StackOffset, inset, w - StackOffset, h - 2 * inset);
        StackEdge.Data = new System.Windows.Media.CombinedGeometry(System.Windows.Media.GeometryCombineMode.Exclude,
            new System.Windows.Media.RectangleGeometry(back, r, r), new System.Windows.Media.RectangleGeometry(front, r, r));
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

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _pressedAt = e.GetPosition(this);
    }

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
        // Varias ventanas agrupadas: el clic muestra todas para elegir (otro clic las oculta).
        var members = Members();
        if (members.Count > 1)
        {
            if (TaskPreviewWindow.IsShowingFor(this))
                TaskPreviewWindow.Dismiss();
            else
                TaskPreviewWindow.ShowNow(this, members);
            return;
        }
        TaskPreviewWindow.Dismiss();

        // Como en Windows: clic en la ventana activa la minimiza; en otra, la trae al frente.
        if (window.State == ApplicationWindow.WindowState.Active && !window.IsMinimized)
            window.Minimize();
        else
            window.BringToFront();
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
