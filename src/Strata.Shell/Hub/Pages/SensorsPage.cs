using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Lecturas avanzadas (--hub Sensors): temperaturas y FPS con su permiso, PawnIO y Liberar RAM
/// (<see cref="PerformanceTools"/>).
/// </summary>
public sealed class SensorsPage : Page
{
    public SensorsPage()
    {
        Title = Loc.T("Lecturas avanzadas");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.Temperature24,
            Title = Loc.T("Lecturas avanzadas"),
            Subtitle = Loc.T("Temperaturas, FPS y liberar la memoria RAM."),
        });
        content.Children.Add(new PerformanceTools());
        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Content = content };
    }
}
