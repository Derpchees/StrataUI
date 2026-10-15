using Strata.Shell.Hub.Pages;
using System.Windows;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
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
    private readonly List<NavigationViewItem> _optimizationItems = new();

    public HubWindow()
    {
        InitializeComponent();
        ApplyBackdrop();
        ThemeManager.ThemeChanged += ApplyBackdrop;
        Closed += (_, _) => ThemeManager.ThemeChanged -= ApplyBackdrop;
        // Las animaciones del Hub son las elegidas en Apariencia, no la transición fija de WPF-UI.
        Nav.Transition = Wpf.Ui.Animations.Transition.None;
        FillOptimization();
        OptimizationModule.Changed += OnOptimizationChanged;
        Closed += (_, _) => OptimizationModule.Changed -= OnOptimizationChanged;
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

    /// <summary>
    /// Las entradas del grupo Optimización: las páginas del módulo o, si no está instalado (o no se
    /// pudo cargar), la que permite instalarlo.
    /// </summary>
    private void FillOptimization()
    {
        foreach (var item in _optimizationItems)
            Nav.MenuItems.Remove(item);
        _optimizationItems.Clear();
        IReadOnlyList<HubModulePage> pages = OptimizationModule.Load()?.Pages
            ?? [new HubModulePage(Loc.T("Instalar"), Wpf.Ui.Controls.SymbolRegular.ArrowDownload24, typeof(OptimizationSetupPage))];
        int index = Nav.MenuItems.IndexOf(OptimizationHeader) + 1;
        foreach (var page in pages)
        {
            var item = new NavigationViewItem
            {
                Content = page.Title,
                Icon = new SymbolIcon { Symbol = page.Icon },
                TargetPageType = page.PageType,
            };
            Nav.MenuItems.Insert(index++, item);
            _optimizationItems.Add(item);
        }
    }

    /// <summary>
    /// Se instaló o se quitó el módulo: cambian las entradas. Al instalarlo se abre su primera página
    /// (cuando la navegación ya registró las entradas nuevas, para que quede marcada).
    /// </summary>
    private void OnOptimizationChanged() => Dispatcher.BeginInvoke(() =>
    {
        bool showsModule = _optimizationItems.Any(i => i.TargetPageType != typeof(OptimizationSetupPage));
        bool installed = OptimizationModule.Load() != null;
        if (OptimizationModule.IsDownloading || showsModule == installed)
            return;
        FillOptimization();
        if (installed && _optimizationItems.FirstOrDefault()?.TargetPageType is { } first)
            Dispatcher.BeginInvoke(() => Nav.Navigate(first), System.Windows.Threading.DispatcherPriority.Loaded);
    });

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

    /// <summary>
    /// Navega a una página por nombre: "Privacy", "PrivacyPage", "Apps"… Las del módulo de
    /// optimización, si no está instalado, llevan a la página para instalarlo.
    /// </summary>
    public void NavigateTo(string page)
    {
        var assemblies = new List<System.Reflection.Assembly> { typeof(TaskbarPage).Assembly };
        if (OptimizationModule.Load() is { } module)
            assemblies.Add(module.GetType().Assembly);
        var type = assemblies.SelectMany(a => a.GetTypes()).FirstOrDefault(t =>
            t.Namespace == typeof(TaskbarPage).Namespace && !t.IsAbstract && typeof(System.Windows.Controls.Page).IsAssignableFrom(t) &&
            (t.Name.Equals(page, StringComparison.OrdinalIgnoreCase) ||
             t.Name.Equals(page + "Page", StringComparison.OrdinalIgnoreCase)));
        if (type is null && !OptimizationModule.IsInstalled)
            type = typeof(OptimizationSetupPage);
        if (type is null)
            return;
        _startPage = type;
        if (IsLoaded)
            Nav.Navigate(type);
    }
}
