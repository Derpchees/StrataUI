using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedShell.WindowsTasks;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;
using Winx.Shell.Services.WebApps;

namespace Winx.Shell.Controls;

public partial class TaskButton : UserControl
{
    public TaskButton()
    {
        InitializeComponent();
        MouseEnter += (_, _) =>
        {
            // La vista previa ya muestra el título: el tooltip la taparía.
            ToolTipService.SetIsEnabled(MainButton, !Settings.Current.ShowTaskPreviews);
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
            UpdateIcon();
            UpdateGroup();
        };
        Loaded += (_, _) =>
        {
            _bar = System.Windows.Window.GetWindow(this) as Taskbar;
            if (_bar != null)
            {
                FitTo(_bar.Position);
                _bar.TasksChanged += UpdateGroup;
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
            WebSites.IconsChanged -= UpdateIcon;
        };
    }

    private Taskbar? _bar;

    private void OnWindowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApplicationWindow.Icon) or nameof(ApplicationWindow.Title))
            Dispatcher.BeginInvoke(UpdateIcon);
    }

    /// <summary>Ícono de la app o, si es una pestaña de un sitio conocido, el del sitio con el navegador en la esquina.</summary>
    private void UpdateIcon()
    {
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
        StackEdge.Opacity = active ? 0.7 : 0.4;
        ActiveBackground.Margin = _vertical ? new Thickness(0, 0, 0, 4) : new Thickness(0, 0, 4, 0);
        IconHost.Margin = ActiveBackground.Margin; // centrado sobre la placa
        Indicator.Width = active || flashing ? 16 : 6;
        if (flashing && !active)
            Indicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xA3, 0x3D));
        else
            Indicator.SetResourceReference(Border.BackgroundProperty, active ? "WinxAccentBrush" : "WinxInactiveIndicatorBrush");
    }

    /// <summary>
    /// El indicador va junto al borde de la pantalla, como en Windows; en barras verticales se gira y el
    /// botón toma un alto fijo (en las horizontales lo da el alto de la barra).
    /// </summary>
    private bool _vertical;

    private void FitTo(TaskbarPosition position)
    {
        _vertical = position is TaskbarPosition.Left or TaskbarPosition.Right;
        if (_vertical)
        {
            // En vertical las ventanas se apilan hacia abajo.
            StackEdge.Width = double.NaN;
            StackEdge.Height = 3;
            StackEdge.Margin = new Thickness(6, 0, 6, 0);
            StackEdge.HorizontalAlignment = HorizontalAlignment.Stretch;
            StackEdge.VerticalAlignment = VerticalAlignment.Bottom;
            StackEdge.CornerRadius = new CornerRadius(0, 0, 3, 3);
        }
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

    private ApplicationWindow? Window => DataContext as ApplicationWindow;

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
}
