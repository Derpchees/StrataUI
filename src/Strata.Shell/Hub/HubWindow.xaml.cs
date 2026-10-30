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
            // En otra se pliega: abajo, desplegada, le quitaba lugar a la lista de arriba (Widgets no se veía).
            ModulesItem.IsExpanded = e.Page is ModulesPage || e.Page?.GetType().Assembly != typeof(HubWindow).Assembly;
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

        // Vuelve a abrir donde quedó (mismo tamaño, monitor y maximizado); centrado solo la primera vez.
        if (Settings.Current.HubPlacement is { Length: 5 })
            WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) => RestorePlacement();
        Closing += (_, _) => SavePlacement();

        Search.Navigation = NavigationEntries;
        Search.Chosen += entry =>
        {
            NavigateTo(entry.Page);
            if (entry.Highlight is { } text)
                Highlight(text);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                Search.FocusBox();
                e.Handled = true;
            }
        };
    }

    // ── Lugar de la ventana ─────────────────────────────────────────────────
    // GetWindowPlacement guarda píxeles reales (vale con monitores a distinta escala) y, al restaurar,
    // Windows trae la ventana a la pantalla si ese monitor ya no está.

    private void RestorePlacement()
    {
        if (Settings.Current.HubPlacement is not { Length: 5 } saved)
            return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var placement = new WindowPlacement { Length = System.Runtime.InteropServices.Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref placement))
            return;
        placement.Normal = new NativeRect { Left = saved[0], Top = saved[1], Right = saved[2], Bottom = saved[3] };
        // Solo el lugar, todavía oculta (WPF la muestra después); maximizada la deja WPF al mostrarla.
        placement.ShowCommand = ShowHide;
        placement.Flags = 0;
        if (placement.Normal.Right - placement.Normal.Left > 200 && placement.Normal.Bottom - placement.Normal.Top > 200)
        {
            SetWindowPlacement(hwnd, ref placement);
            if (saved[4] == 1)
                WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var placement = new WindowPlacement { Length = System.Runtime.InteropServices.Marshal.SizeOf<WindowPlacement>() };
        if (hwnd == IntPtr.Zero || !GetWindowPlacement(hwnd, ref placement))
            return;
        var r = placement.Normal;
        Settings.Current.HubPlacement = [r.Left, r.Top, r.Right, r.Bottom, WindowState == WindowState.Maximized ? 1 : 0];
        Settings.Save();
    }

    private const int ShowHide = 0;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length, Flags, ShowCommand;
        public NativePoint Min, Max;
        public NativeRect Normal;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    /// <summary>Las entradas de la navegación con página (también las de dentro de Módulos opcionales).</summary>
    private IEnumerable<(string Title, Type Page)> NavigationEntries()
    {
        IEnumerable<NavigationViewItem> Flatten(System.Collections.IList items) =>
            items.OfType<NavigationViewItem>().SelectMany(item => Flatten(item.MenuItems).Prepend(item));
        return Flatten(Nav.MenuItems).Concat(Flatten(Nav.FooterMenuItems))
            .Where(item => item.TargetPageType != null && item.Content is string)
            .Select(item => ((string)item.Content, item.TargetPageType!));
    }

    /// <summary>
    /// Lleva a la vista la tarjeta con ese título (un resultado del buscador) y la hace parpadear. La
    /// página (y su pestaña) se arma un momento después de navegar: se busca durante un par de segundos.
    /// </summary>
    private async void Highlight(string title)
    {
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(100);
            if (FindText(Nav, title) is not { } text)
                continue;
            FrameworkElement target = text;
            for (DependencyObject? node = text; node != null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            {
                if (node is CardControl card)
                {
                    target = card;
                    break;
                }
            }
            target.BringIntoView();
            target.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
            });
            return;
        }
    }

    private static System.Windows.Controls.TextBlock? FindText(DependencyObject root, string text)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TextBlock { IsVisible: true } block && block.Text == text)
                return block;
            if (FindText(child, text) is { } found)
                return found;
        }
        return null;
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
