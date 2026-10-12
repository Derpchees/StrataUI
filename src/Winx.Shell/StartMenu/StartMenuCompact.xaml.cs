using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Winx.Shell.StartMenu;

/// <summary>Menú Inicio compacto: una columna angosta con anclados en lista.</summary>
public partial class StartMenuCompact : StartMenuBase
{
    public StartMenuCompact() => InitializeComponent();

    protected override TextBox? SearchInput => SearchBox;

    private void Search_KeyDown(object sender, KeyEventArgs e) => OnSearchKeyDown(sender, e);
    private void ShowAll_Click(object sender, RoutedEventArgs e) => OnShowAllApps(sender, e);
    private void Back_Click(object sender, RoutedEventArgs e) => OnBackToHome(sender, e);
}
