using Strata.Shell.Hub.Pages;
using System.Windows;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;
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
    private object? _currentPage;
    private bool _showsOptimization;

    public HubWindow()
    {
        InitializeComponent();
        FluentBackdrop.Follow(this);
        // Las animaciones del Hub son las elegidas en Apariencia, no la transición fija de WPF-UI.
        Nav.Transition = Wpf.Ui.Animations.Transition.None;
        FillModules();
        OptionalModules.Changed += OnModuleChanged;
        Closed += (_, _) => OptionalModules.Changed -= OnModuleChanged;
        Nav.Navigated += (_, e) =>
        {
            _currentPage = e.Page;
            // Una página de Módulos opcionales (o de Optimización): su entrada se despliega y queda marcada.
            if (e.Page is ModulesPage || e.Page?.GetType().Assembly != typeof(HubWindow).Assembly)
                ModulesItem.IsExpanded = true;
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
    /// Módulos opcionales: la página para instalarlos y, si Optimización está instalado, sus páginas
    /// debajo. Van al mismo nivel: el menú del Hub solo despliega un nivel (una entrada dentro de otra
    /// desplegable no mostraba sus páginas).
    /// </summary>
    private void FillModules()
    {
        ModulesItem.MenuItems.Clear();
        ModulesItem.MenuItems.Add(new NavigationViewItem
        {
            Content = Loc.T("Instalar y quitar"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload24 },
            TargetPageType = typeof(ModulesPage),
        });
        _showsOptimization = false;
        if (OptionalModules.Optimization.Load() is { } module)
        {
            foreach (var page in module.Pages)
                ModulesItem.MenuItems.Add(new NavigationViewItem
                {
                    Content = page.Title,
                    Icon = new SymbolIcon { Symbol = page.Icon },
                    TargetPageType = page.PageType,
                });
            _showsOptimization = true;
        }
    }

    /// <summary>Se instaló o se quitó Optimización: aparecen o se van sus páginas.</summary>
    private void OnModuleChanged(OptionalModule changed) => Dispatcher.BeginInvoke(() =>
    {
        if (changed != OptionalModules.Optimization || changed.IsDownloading)
            return;
        bool installed = OptionalModules.Optimization.Load() != null;
        if (installed == _showsOptimization)
            return;
        FillModules();
        ModulesItem.IsExpanded = true;
    });

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
    /// Navega a una página por nombre: "Privacy", "PrivacyPage", "Tray", "Dashboard"… Las que van en
    /// pestañas abren su entrada en esa pestaña. Las del módulo de optimización, si no está instalado,
    /// llevan a la página para instalarlo.
    /// </summary>
    public void NavigateTo(string page)
    {
        if (page.Equals("Dashboard", StringComparison.OrdinalIgnoreCase))
            page = nameof(HomePage);
        var assemblies = new List<System.Reflection.Assembly> { typeof(TaskbarPage).Assembly };
        if (OptionalModules.Optimization.Load() is { } module)
            assemblies.Add(module.GetType().Assembly);
        var type = assemblies.SelectMany(a => a.GetTypes()).FirstOrDefault(t =>
            t.Namespace is "Strata.Shell.Hub.Pages" or "Strata.Shell.Hub" && !t.IsAbstract && typeof(System.Windows.Controls.Page).IsAssignableFrom(t) &&
            (t.Name.Equals(page, StringComparison.OrdinalIgnoreCase) ||
             t.Name.Equals(page + "Page", StringComparison.OrdinalIgnoreCase)));
        // Una página de un módulo que no está instalado: la de módulos, para instalarlo.
        if (type is null && !OptionalModules.Optimization.IsInstalled)
            type = typeof(ModulesPage);
        if (type is null)
            return;
        if (TabbedPage.Hosts.TryGetValue(type, out var host))
        {
            // Ya abierta: solo cambia de pestaña. Si no, la entrada abre en esa pestaña al crearse.
            if (IsLoaded && _currentPage?.GetType() == host && ((TabbedPage)_currentPage).Select(type))
                return;
            TabbedPage.Request(type);
            type = host;
        }
        _startPage = type;
        if (IsLoaded)
            Nav.Navigate(type);
    }
}
