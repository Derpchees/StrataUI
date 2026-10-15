using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Widgets;
using Wpf.Ui.Controls;
using PageHeader = Strata.Shell.Hub.PageHeader;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Hub › Widgets: agregar relojes y visualizadores al escritorio, y ver o quitar los que hay. Se
/// mueven arrastrándolos en el escritorio; el tamaño y las opciones, con su clic derecho.
/// </summary>
public sealed class WidgetsPage : Page
{
    private readonly StackPanel _list = new();

    public WidgetsPage()
    {
        Title = Loc.T("Widgets");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.Apps24,
            Title = Loc.T("Widgets"),
            Subtitle = Loc.T("Relojes y un visualizador de música sobre el fondo de pantalla. Arrástralos para moverlos; clic derecho para su tamaño y opciones."),
        });

        content.Children.Add(new TextBlock { Style = (Style)FindResource("HubSectionTitle"), Text = Loc.T("Agregar") });
        var add = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        add.Children.Add(AddButton(SymbolRegular.Clock24, Loc.T("Reloj"), () => WidgetManager.Add(WidgetKind.Clock, showDate: false)));
        add.Children.Add(AddButton(SymbolRegular.CalendarClock24, Loc.T("Reloj con fecha"), () => WidgetManager.Add(WidgetKind.Clock, showDate: true)));
        add.Children.Add(AddButton(SymbolRegular.MusicNote224, Loc.T("Visualizador de música"), () => WidgetManager.Add(WidgetKind.Visualizer)));
        content.Children.Add(add);

        content.Children.Add(new TextBlock { Style = (Style)FindResource("HubSectionTitle"), Text = Loc.T("En el escritorio") });
        content.Children.Add(_list);

        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Content = content };
        Loaded += (_, _) =>
        {
            WidgetManager.Changed += Build;
            Build();
        };
        Unloaded += (_, _) => WidgetManager.Changed -= Build;
    }

    private static FrameworkElement AddButton(SymbolRegular icon, string text, Action action)
    {
        var button = new Wpf.Ui.Controls.Button
        {
            Icon = new SymbolIcon { Symbol = icon },
            Content = text,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(14, 8, 14, 8),
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void Build()
    {
        _list.Children.Clear();
        var widgets = Settings.Current.Widgets;
        if (widgets.Count == 0)
        {
            _list.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = Loc.T("Todavía no hay widgets.") });
            return;
        }
        var screens = System.Windows.Forms.Screen.AllScreens.Select(s => s.DeviceName).ToList();
        foreach (var widget in widgets)
        {
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Name(widget) });
            int monitor = screens.IndexOf(widget.Device ?? "") + 1;
            header.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("CardDescription"),
                Text = (monitor > 0 ? Loc.F("Monitor {0}", monitor) : Loc.T("Monitor principal"))
                       + (widget.Locked ? " · " + Loc.T("posición bloqueada") : ""),
            });
            var remove = new Wpf.Ui.Controls.Button { Content = Loc.T("Quitar"), Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 } };
            remove.Click += (_, _) => WidgetManager.Remove(widget.Id);
            _list.Children.Add(new CardControl
            {
                Style = (Style)FindResource("HubCard"),
                Header = header,
                Content = remove,
                Icon = new SymbolIcon { Symbol = widget.Kind == WidgetKind.Visualizer ? SymbolRegular.MusicNote224 : SymbolRegular.Clock24 },
            });
        }
    }

    private static string Name(WidgetConfig widget) => widget.Kind switch
    {
        WidgetKind.Visualizer => Loc.T("Visualizador de música"),
        _ => widget.ShowDate ? Loc.T("Reloj con fecha") : Loc.T("Reloj"),
    };
}
