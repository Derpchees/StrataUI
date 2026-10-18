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
    private NavigationViewItem? _optimizationItem;
    private object? _currentPage;

    public HubWindow()
    {
        InitializeComponent();
        ApplyBackdrop();
        ThemeManager.ThemeChanged += ApplyBackdrop;
        Closed += (_, _) => ThemeManager.ThemeChanged -= ApplyBackdrop;
        // Las animaciones del Hub son las elegidas en Apariencia, no la transición fija de WPF-UI.
        Nav.Transition = Wpf.Ui.Animations.Transition.None;
        FillModules();
        OptimizationModule.Changed += OnOptimizationChanged;
        Closed += (_, _) => OptimizationModule.Changed -= OnOptimizationChanged;
        Nav.Navigated += (_, e) =>
        {
            _currentPage = e.Page;
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
    /// La entrada del módulo de optimización en Módulos: sus páginas dentro (se despliegan) o, si no
    /// está instalado (o no se pudo cargar), la página para instalarlo.
    /// </summary>
    private void FillModules()
    {
        if (_optimizationItem != null)
            Nav.MenuItems.Remove(_optimizationItem);
        _optimizationItem = new NavigationViewItem
        {
            Content = Loc.T("Optimización"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Flash24 },
        };
        if (OptimizationModule.Load() is { } module)
        {
            foreach (var page in module.Pages)
                _optimizationItem.MenuItems.Add(new NavigationViewItem
                {
                    Content = page.Title,
                    Icon = new SymbolIcon { Symbol = page.Icon },
                    TargetPageType = page.PageType,
                });
        }
        else
        {
            _optimizationItem.TargetPageType = typeof(OptimizationSetupPage);
        }
        Nav.MenuItems.Insert(Nav.MenuItems.IndexOf(ImportItem) + 1, _optimizationItem);
    }

    /// <summary>
    /// Se instaló o se quitó el módulo: cambia su entrada. Al instalarlo se despliega y se abre su
    /// primera página (cuando la navegación ya registró las entradas nuevas, para que quede marcada).
    /// </summary>
    private void OnOptimizationChanged() => Dispatcher.BeginInvoke(() =>
    {
        bool showsModule = _optimizationItem?.MenuItems.Count > 0;
        bool installed = OptimizationModule.Load() != null;
        if (OptimizationModule.IsDownloading || showsModule == installed)
            return;
        FillModules();
        if (installed && _optimizationItem?.MenuItems.OfType<NavigationViewItem>().FirstOrDefault()?.TargetPageType is { } first)
        {
            _optimizationItem.IsExpanded = true;
            Dispatcher.BeginInvoke(() => Nav.Navigate(first), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    });

    private bool _blurred;

    /// <summary>
    /// El material del tema, también al cambiarlo con el Hub abierto. El desenfoque limpio (tema
    /// Transparente total) lo pone la ventana: WPF-UI pinta su fondo sólido y se deja transparente.
    /// </summary>
    private void ApplyBackdrop()
    {
        bool blur = ThemeManager.Theme.Backdrop == Services.Themes.Backdrop.Blur;
        if (blur)
        {
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
            Background = System.Windows.Media.Brushes.Transparent;
            if (IsLoaded)
                Services.Themes.WindowEffects.SetBlur(this, Services.Themes.Backdrop.Blur);
            else
                SourceInitialized += (_, _) => Services.Themes.WindowEffects.SetBlur(this, Services.Themes.Backdrop.Blur);
        }
        else
        {
            if (_blurred)
            {
                Services.Themes.WindowEffects.SetBlur(this, Services.Themes.Backdrop.Solid);
                ClearValue(BackgroundProperty);
            }
            WindowBackdropType = ThemeManager.WindowBackdrop;
        }
        _blurred = blur;
    }

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
        if (OptimizationModule.Load() is { } module)
            assemblies.Add(module.GetType().Assembly);
        var type = assemblies.SelectMany(a => a.GetTypes()).FirstOrDefault(t =>
            t.Namespace is "Strata.Shell.Hub.Pages" or "Strata.Shell.Hub" && !t.IsAbstract && typeof(System.Windows.Controls.Page).IsAssignableFrom(t) &&
            (t.Name.Equals(page, StringComparison.OrdinalIgnoreCase) ||
             t.Name.Equals(page + "Page", StringComparison.OrdinalIgnoreCase)));
        if (type is null && !OptimizationModule.IsInstalled)
            type = typeof(OptimizationSetupPage);
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
