using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Winx.Shell.Controls;
using Winx.Shell.Services;
using Winx.Shell.Services.StartMenu;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Hub.Pages;

public partial class StartPage : Page
{
    private static readonly string[] Descriptions =
    [
        Loc.T("Buscador, apps ancladas, recientes y \"Todas las apps\"."),
        Loc.T("Lista alfabética a la izquierda; anclados y carpetas a la derecha."),
        Loc.T("Todas las apps en una cuadrícula grande que ocupa la pantalla."),
        Loc.T("Una columna angosta con las apps ancladas en lista."),
        Loc.T("Solo una barra de búsqueda centrada en la pantalla, estilo Spotlight."),
        Loc.T("Cuadrícula de íconos sin texto; el nombre aparece al pasar el mouse."),
        Loc.T("Como Windows 11, con tu foto grande arriba sobresaliendo del menú, tu nombre y un saludo."),
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
        PinnedLabelsSwitch.IsChecked = s.StartShowPinnedLabels;
        AllAppsSwitch.IsChecked = s.StartShowAllApps;
        RecentSwitch.IsChecked = s.StartShowRecent;
        UserSwitch.IsChecked = s.StartShowUser;
        PictureSwitch.IsChecked = s.StartShowUserPicture;
        WinKeySwitch.IsChecked = s.StartUseWinKey;
        MouseMonitorSwitch.IsChecked = s.StartOnMouseMonitor;
        BuildFooter();
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
        s.StartShowPinnedLabels = PinnedLabelsSwitch.IsChecked == true;
        s.StartShowAllApps = AllAppsSwitch.IsChecked == true;
        s.StartShowRecent = RecentSwitch.IsChecked == true;
        s.StartShowUser = UserSwitch.IsChecked == true;
        s.StartShowUserPicture = PictureSwitch.IsChecked == true;
        s.StartUseWinKey = WinKeySwitch.IsChecked == true;
        s.StartOnMouseMonitor = MouseMonitorSwitch.IsChecked == true;
    });

    private void ResetPinned_Click(object sender, RoutedEventArgs e) => StartMenuService.ResetPinned();

    // ── Botones del pie ─────────────────────────────────────────────────────
    // Fichas en el orden del menú: se arrastran para ordenarlas (ver Hub/ChipDrag).

    private ChipDrag? _footerDrag;

    private void BuildFooter()
    {
        _footerDrag ??= CreateFooterDrag();
        FooterChips.Children.Clear();
        foreach (var button in StartFooterCatalog.Selected)
            FooterChips.Children.Add(FooterChip(button));
    }

    private ChipDrag CreateFooterDrag()
    {
        var drag = new ChipDrag(this, () => [FooterChips]);
        drag.Committed += SaveFooter;
        return drag;
    }

    private FrameworkElement FooterChip(StartFooterButton button)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = button.Icon, FontSize = 16, Margin = new Thickness(0, 0, 8, 0) });
        content.Children.Add(new TextBlock { Text = button.Label, VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("CardTitle") });
        var chip = new Border { Style = (Style)FindResource("BarChip"), Tag = button.Id, Child = content, ToolTip = Loc.T("Arrastra para moverlo") };
        var remove = new Wpf.Ui.Controls.Button
        {
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Dismiss16, FontSize = 11 },
            Padding = new Thickness(4),
            Margin = new Thickness(6, 0, 0, 0),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
            BorderThickness = new Thickness(0),
            ToolTip = Loc.T("Quitar"),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        remove.Click += (_, _) =>
        {
            FooterChips.Children.Remove(chip);
            SaveFooter();
        };
        content.Children.Add(remove);
        _footerDrag!.Attach(chip);
        return chip;
    }

    private void FooterAdd_Click(object sender, RoutedEventArgs e)
    {
        var present = FooterChips.Children.OfType<FrameworkElement>().Select(c => (string)c.Tag).ToHashSet();
        var menu = new ContextMenu { PlacementTarget = FooterAddButton, Placement = PlacementMode.Bottom };
        foreach (var button in StartFooterCatalog.All.Where(b => !present.Contains(b.Id)))
        {
            var entry = new MenuItem { Header = button.Label, Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = button.Icon } };
            entry.Click += (_, _) =>
            {
                FooterChips.Children.Add(FooterChip(button));
                SaveFooter();
            };
            menu.Items.Add(entry);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = Loc.T("Ya están todos"), IsEnabled = false });
        menu.IsOpen = true;
    }

    private void SaveFooter() => Settings.Update(s =>
        s.StartFooterButtons = FooterChips.Children.OfType<FrameworkElement>().Select(c => (string)c.Tag).ToList());

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
            System.Windows.Automation.AutomationProperties.SetName(card, Loc.F("Ícono {0}", name));
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
