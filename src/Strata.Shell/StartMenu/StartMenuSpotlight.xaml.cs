using System.Windows.Controls;
using System.Windows.Input;
using Strata.Shell.Flyouts;

namespace Strata.Shell.StartMenu;

/// <summary>Menú Inicio "solo búsqueda": barra centrada en la pantalla, estilo Spotlight.</summary>
public partial class StartMenuSpotlight : StartMenuBase
{
    public StartMenuSpotlight() => InitializeComponent();

    protected override TextBox? SearchInput => SearchBox;

    protected override FlyoutPlacement Placement => FlyoutPlacement.ScreenCenter;

    private void Search_KeyDown(object sender, KeyEventArgs e) => OnSearchKeyDown(sender, e);
}
