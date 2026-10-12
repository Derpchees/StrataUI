using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Winx.Shell.Controls;
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
        "Una columna angosta con las apps ancladas en lista.",
        "Solo una barra de búsqueda centrada en la pantalla, estilo Spotlight.",
        "Cuadrícula de íconos sin texto; el nombre aparece al pasar el mouse.",
        "Como Windows 11, con tu foto grande arriba sobresaliendo del menú, tu nombre y un saludo.",
    ];

    private readonly bool _loading = true;

    public StartPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        StyleBox.SelectedIndex = (int)s.StartStyle;
        StyleDescription.Text = Descriptions[(int)s.StartStyle];
        IconSizeBox.SelectedIndex = (int)s.StartIconSize;
        PinnedSwitch.IsChecked = s.StartShowPinned;
        AllAppsSwitch.IsChecked = s.StartShowAllApps;
        RecentSwitch.IsChecked = s.StartShowRecent;
        UserSwitch.IsChecked = s.StartShowUser;
        PictureSwitch.IsChecked = s.StartShowUserPicture;
        WinKeySwitch.IsChecked = s.StartUseWinKey;
        FooterList.ItemsSource = StartFooterCatalog.All;
        BuildIconGallery();
        _loading = false;
    }

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StyleBox.SelectedIndex < 0)
            return;
        StyleDescription.Text = Descriptions[StyleBox.SelectedIndex];
        Settings.Update(s => s.StartStyle = (StartMenuStyle)StyleBox.SelectedIndex);
    }

    private void IconSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && IconSizeBox.SelectedIndex >= 0)
            Settings.Update(s => s.StartIconSize = (StartIconSize)IconSizeBox.SelectedIndex);
    }

    private void Options_Changed(object sender, RoutedEventArgs e) => Settings.Update(s =>
    {
        s.StartShowPinned = PinnedSwitch.IsChecked == true;
        s.StartShowAllApps = AllAppsSwitch.IsChecked == true;
        s.StartShowRecent = RecentSwitch.IsChecked == true;
        s.StartShowUser = UserSwitch.IsChecked == true;
        s.StartShowUserPicture = PictureSwitch.IsChecked == true;
        s.StartUseWinKey = WinKeySwitch.IsChecked == true;
    });

    private void ResetPinned_Click(object sender, RoutedEventArgs e) => StartMenuService.ResetPinned();

    // ── Botones del pie ─────────────────────────────────────────────────────

    private void FooterSwitch_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string id } toggle)
            toggle.IsChecked = Settings.Current.StartFooterButtons.Contains(id);
    }

    private void FooterSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string id } toggle)
            return;
        bool on = toggle.IsChecked == true;
        Settings.Update(s =>
        {
            // Se guarda en el orden del catálogo, que es el que se ve en esta página.
            var selected = new HashSet<string>(s.StartFooterButtons);
            if (on) selected.Add(id); else selected.Remove(id);
            s.StartFooterButtons = StartFooterCatalog.All.Select(b => b.Id).Where(selected.Contains).ToList();
        });
    }

    // ── Ícono del botón Inicio ──────────────────────────────────────────────

    private void BuildIconGallery()
    {
        foreach (var (kind, name) in StartIcons.All)
        {
            var content = new StackPanel { Margin = new Thickness(12, 12, 12, 8) };
            content.Children.Add(new Border { Height = 28, Child = StartIcons.Create(kind, 22) });
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                TextAlignment = TextAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
            });

            var card = new Button
            {
                Width = 116,
                Margin = new Thickness(0, 0, 8, 8),
                Tag = kind,
                Content = content,
                ToolTip = name,
            };
            System.Windows.Automation.AutomationProperties.SetName(card, $"Ícono {name}");
            card.Click += (_, _) =>
            {
                Settings.Update(s => s.StartIcon = kind);
                UpdateIconSelection();
            };
            IconGallery.Children.Add(card);
        }
        UpdateIconSelection();
    }

    private void UpdateIconSelection()
    {
        foreach (Button card in IconGallery.Children)
        {
            bool selected = (StartIconKind)card.Tag == Settings.Current.StartIcon;
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            card.BorderBrush = selected
                ? (TryFindResource("AccentFillColorDefaultBrush") as Brush) ?? Brushes.DodgerBlue
                : (TryFindResource("CardStrokeColorDefaultBrush") as Brush) ?? Brushes.Gray;
        }
    }
}
