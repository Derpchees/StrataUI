using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Strata.Shell.StartMenu;

/// <summary>Menú Inicio estilo Windows 11: buscador, anclados, recientes y todas las apps.</summary>
public partial class StartMenuWin11 : StartMenuBase
{
    public StartMenuWin11() => InitializeComponent();

    protected override TextBox? SearchInput => SearchBox;

    private void Search_KeyDown(object sender, KeyEventArgs e) => OnSearchKeyDown(sender, e);
    private void ShowAll_Click(object sender, RoutedEventArgs e) => OnShowAllApps(sender, e);
    private void Back_Click(object sender, RoutedEventArgs e) => OnBackToHome(sender, e);
}
