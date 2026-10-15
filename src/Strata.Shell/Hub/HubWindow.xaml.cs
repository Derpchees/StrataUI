using Strata.Shell.Hub.Pages;
using System.Windows;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;

namespace Strata.Shell.Hub;

/// <summary>
/// Ventana central de configuración de Strata UI.
/// Las páginas declaran ScrollViewer.CanContentScroll="False" y traen su propio ScrollViewer:
/// así WPF-UI no las envuelve en uno con altura infinita (que impedía desplazar con la rueda).
/// </summary>
public partial class HubWindow : FluentWindow
{
    private Type _startPage = typeof(HomePage);

    public HubWindow()
    {
        InitializeComponent();
        ApplyBackdrop();
        ThemeManager.ThemeChanged += ApplyBackdrop;
        Closed += (_, _) => ThemeManager.ThemeChanged -= ApplyBackdrop;
        // Las animaciones del Hub son las elegidas en Apariencia, no la transición fija de WPF-UI.
        Nav.Transition = Wpf.Ui.Animations.Transition.None;
        Nav.Navigated += (_, e) =>
        {
            if (e.Page is FrameworkElement page)
            {
                Motion.In(page, page, TaskbarPosition.Bottom, 0.6);
                // Las partes que se arman al cargar (o el foco de un control) la dejaban desplazada:
                // cada página abre desde arriba.
                Dispatcher.BeginInvoke(() => FindScroll(page)?.ScrollToTop(),
                                       System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        };
        Loaded += (_, _) =>
        {
            Motion.In(Root, Root, TaskbarPosition.Bottom, 0.8);
            Nav.Navigate(_startPage);
        };
    }

    private void ApplyBackdrop() => WindowBackdropType = ThemeManager.WindowBackdrop;

    private static System.Windows.Controls.ScrollViewer? FindScroll(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.ScrollViewer scroll)
                return scroll;
            if (FindScroll(child) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>Navega a una página por nombre: "Privacy", "PrivacyPage", "Apps"…</summary>
    public void NavigateTo(string page)
    {
        var type = typeof(TaskbarPage).Assembly.GetTypes().FirstOrDefault(t =>
            t.Namespace == typeof(TaskbarPage).Namespace && !t.IsAbstract &&
            (t.Name.Equals(page, StringComparison.OrdinalIgnoreCase) ||
             t.Name.Equals(page + "Page", StringComparison.OrdinalIgnoreCase)));
        if (type is null)
            return;
        _startPage = type;
        if (IsLoaded)
            Nav.Navigate(type);
    }
}
