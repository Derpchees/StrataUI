using System.Windows;
using System.Windows.Controls;
using Winx.Shell.Services;
using Winx.Shell.Services.StartMenu;

namespace Winx.Shell.Hub.Pages;

public partial class StartPage : Page
{
    private static readonly string[] Descriptions =
    [
        "Buscador, apps ancladas, recientes y \"Todas las apps\".",
        "Lista alfabética a la izquierda; anclados y carpetas a la derecha.",
        "Todas las apps en una cuadrícula grande que ocupa la pantalla.",
    ];

    private readonly bool _loading = true;

    public StartPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        StyleBox.SelectedIndex = (int)s.StartStyle;
        StyleDescription.Text = Descriptions[(int)s.StartStyle];
        RecentSwitch.IsChecked = s.StartShowRecent;
        WinKeySwitch.IsChecked = s.StartUseWinKey;
        _loading = false;
    }

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StyleBox.SelectedIndex < 0)
            return;
        StyleDescription.Text = Descriptions[StyleBox.SelectedIndex];
        Settings.Update(s => s.StartStyle = (StartMenuStyle)StyleBox.SelectedIndex);
    }

    private void Options_Changed(object sender, RoutedEventArgs e) => Settings.Update(s =>
    {
        s.StartShowRecent = RecentSwitch.IsChecked == true;
        s.StartUseWinKey = WinKeySwitch.IsChecked == true;
    });

    private void ResetPinned_Click(object sender, RoutedEventArgs e) => StartMenuService.ResetPinned();
}
