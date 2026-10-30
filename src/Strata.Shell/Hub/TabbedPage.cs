using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Navigation;
using Strata.Shell.Hub.Pages;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;

namespace Strata.Shell.Hub;

/// <summary>
/// Una entrada del Hub que junta varias páginas en pestañas (Barra de tareas: barra, bandeja y
/// multimedia). Arriba quedan fijos el título, el monitor que se edita y las pestañas; cada
/// página se desplaza debajo. Las páginas son las de siempre: su cabecera y su selector de monitor se
/// ocultan solos dentro de una pestaña (<see cref="IsEmbedded"/>).
/// </summary>
public abstract class TabbedPage : Page
{
    public sealed record Tab(string Title, SymbolRegular Icon, Type PageType);

    private const string EmbeddedTag = "StrataHubTab";

    /// <summary>Página de cada pestaña → la entrada que la contiene (para abrir "Tray", "Updates"…).</summary>
    public static IReadOnlyDictionary<Type, Type> Hosts => HostTable;

    private static readonly Dictionary<Type, Type> HostTable = new();

    /// <summary>Cada entrada con pestañas, su título y sus pestañas (para el buscador del Hub).</summary>
    internal static IReadOnlyList<(Type Host, string Title, IReadOnlyList<Tab> Tabs)> Registered => RegisteredHosts;

    private static readonly List<(Type Host, string Title, IReadOnlyList<Tab> Tabs)> RegisteredHosts = new();

    static TabbedPage()
    {
        RegisterHost(typeof(TaskbarHubPage), Loc.T("Barra de tareas"), TaskbarHubPage.Tabs);
        RegisterHost(typeof(AppearanceHubPage), Loc.T("Tema y colores"), AppearanceHubPage.Tabs);
        RegisterHost(typeof(SettingsHubPage), Loc.T("General"), SettingsHubPage.Tabs);
    }

    /// <summary>
    /// Las pestañas de una entrada, para abrirlas por nombre ("--hub Tray", "--hub Privacy") y
    /// encontrarlas en el buscador. Las de un módulo (Optimización) las registra el módulo.
    /// </summary>
    public static void RegisterHost(Type host, string title, IReadOnlyList<Tab> tabs)
    {
        foreach (var tab in tabs)
            HostTable[tab.PageType] = host;
        RegisteredHosts.RemoveAll(r => r.Host == host);
        RegisteredHosts.Add((host, title, tabs));
    }

    // Pestaña pedida antes de que la entrada exista (el Hub navega y la entrada la abre al crearse).
    private static Type? _requested;

    private readonly IReadOnlyList<Tab> _tabs;
    private readonly List<RadioButton> _buttons = new();
    private readonly Frame _frame;

    protected TabbedPage(SymbolRegular icon, string title, string subtitle, bool screenPicker, IReadOnlyList<Tab> tabs)
    {
        _tabs = tabs;
        _screenPicker = screenPicker;
        Title = title;
        ScrollViewer.SetCanContentScroll(this, false);

        var top = new StackPanel { Style = (Style)FindResource("HubPageContent"), Margin = new Thickness(36, 16, 36, 0) };
        top.Children.Add(new PageHeader { Icon = icon, Title = title, Subtitle = subtitle, Margin = new Thickness(0, 4, 0, 16) });
        if (screenPicker)
            top.Children.Add(new ScreenPicker());

        var bar = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        string group = "HubTabs" + GetHashCode();
        foreach (var tab in tabs)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("HubTab"),
                GroupName = group,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new SymbolIcon { Symbol = tab.Icon, FontSize = 16, Margin = new Thickness(0, 0, 8, 0) },
                        new System.Windows.Controls.TextBlock { Text = tab.Title, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            button.Checked += (_, _) => Show(tab);
            _buttons.Add(button);
            bar.Children.Add(button);
        }
        top.Children.Add(bar);
        top.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 10, 0, 0),
            Background = (Brush)FindResource("DividerStrokeColorDefaultBrush"),
        });

        _frame = new Frame
        {
            Tag = EmbeddedTag,
            NavigationUIVisibility = NavigationUIVisibility.Hidden,
            JournalOwnership = JournalOwnership.OwnsJournal,
            Focusable = false,
        };
        // Sin historial: cada pestaña se crea de nuevo al elegirla (como las páginas del Hub).
        _frame.Navigated += (_, _) =>
        {
            while (_frame.CanGoBack)
                _frame.RemoveBackEntry();
            if (_frame.Content is FrameworkElement page)
                Motion.In(page, page, TaskbarPosition.Bottom, 0.4);
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(top);
        Grid.SetRow(_frame, 1);
        grid.Children.Add(_frame);
        Content = grid;

        int index = _requested is { } requested ? Math.Max(0, tabs.ToList().FindIndex(t => t.PageType == requested)) : 0;
        _requested = null;
        _buttons[index].IsChecked = true;
    }

    /// <summary>La próxima entrada de este tipo que se cree abre en esa pestaña.</summary>
    public static void Request(Type page) => _requested = page;

    /// <summary>Abre la pestaña de esa página (si es de esta entrada).</summary>
    public bool Select(Type page)
    {
        int index = _tabs.ToList().FindIndex(t => t.PageType == page);
        if (index < 0)
            return false;
        _buttons[index].IsChecked = true;
        return true;
    }

    private void Show(Tab tab) => _frame.Navigate(Activator.CreateInstance(tab.PageType));

    private bool _screenPicker;

    /// <summary>El elemento está en una pestaña cuya entrada ya muestra el selector de monitor (el suyo sobra).</summary>
    public static bool PickerAbove(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is TabbedPage page)
                return page._screenPicker;
        }
        return false;
    }

    /// <summary>El elemento está dentro de una pestaña (su página no muestra cabecera propia).</summary>
    public static bool IsEmbedded(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is Frame { Tag: EmbeddedTag })
                return true;
        }
        return false;
    }
}

/// <summary>Barra de tareas: la barra, la bandeja del sistema y multimedia.</summary>
public sealed class TaskbarHubPage() : TabbedPage(SymbolRegular.PanelBottom20, Loc.T("Barra de tareas"),
    Loc.T("El modo edición, las apps abiertas, la bandeja del sistema y lo que suena."), screenPicker: false, Tabs)
{
    internal static readonly IReadOnlyList<Tab> Tabs =
    [
        new(Loc.T("Barra"), SymbolRegular.PanelBottom20, typeof(TaskbarPage)),
        new(Loc.T("Bandeja del sistema"), SymbolRegular.ChevronUp24, typeof(TrayPage)),
        new(Loc.T("Multimedia"), SymbolRegular.MusicNote224, typeof(MediaPage)),
    ];
}

/// <summary>Tema y colores.</summary>
public sealed class AppearanceHubPage() : TabbedPage(SymbolRegular.PaintBrush24, Loc.T("Tema y colores"),
    Loc.T("Estilo, animaciones, color de acento y fondo de pantalla."), screenPicker: false, Tabs)
{
    internal static readonly IReadOnlyList<Tab> Tabs =
    [
        new(Loc.T("Tema"), SymbolRegular.PaintBrush24, typeof(AppearancePage)),
        new(Loc.T("Colores"), SymbolRegular.Color24, typeof(ColorsPage)),
    ];
}

/// <summary>General y actualizaciones.</summary>
public sealed class SettingsHubPage() : TabbedPage(SymbolRegular.Settings24, Loc.T("General"),
    Loc.T("Inicio con Windows, idioma, actualizaciones y tu configuración."), screenPicker: false, Tabs)
{
    internal static readonly IReadOnlyList<Tab> Tabs =
    [
        new(Loc.T("General"), SymbolRegular.Settings24, typeof(GeneralPage)),
        new(Loc.T("Actualizaciones"), SymbolRegular.ArrowDownload24, typeof(UpdatesPage)),
    ];
}
