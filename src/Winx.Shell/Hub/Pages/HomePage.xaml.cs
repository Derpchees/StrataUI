using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Winx.Shell.Services;
using Winx.Shell.Services.Localization;
using Winx.Shell.Services.Updates;
using Wpf.Ui.Controls;

namespace Winx.Shell.Hub.Pages;

/// <summary>Portada del Hub: versión, estado de las actualizaciones y accesos a cada sección.</summary>
public partial class HomePage : Page
{
    public sealed record Tile(SymbolRegular Icon, string Title, string Description, string Page);

    public HomePage()
    {
        InitializeComponent();
        HeroSubtitle.Text = Loc.F("Versión {0} · Barra, menú Inicio y notificaciones para Windows 11", App.Version);
        Tiles.ItemsSource = new[]
        {
            new Tile(SymbolRegular.PanelBottom20, Loc.T("Barra de tareas"), Loc.T("Alineación, tamaño y monitores"), "Taskbar"),
            new Tile(SymbolRegular.Grid24, Loc.T("Menú Inicio"), Loc.T("Estilo, anclados y botones"), "Start"),
            new Tile(SymbolRegular.Apps24, Loc.T("Bandeja y área de estado"), Loc.T("Íconos, brillo, sonido y reloj"), "Tray"),
            new Tile(SymbolRegular.Alert24, Loc.T("Notificaciones"), Loc.T("Centro, avisos y su posición"), "Notifications"),
            new Tile(SymbolRegular.PaintBrush24, Loc.T("Apariencia"), Loc.T("Tema, color y fondo"), "Appearance"),
            new Tile(SymbolRegular.Flash24, Loc.T("Optimización"), Loc.T("Rendimiento, privacidad, IA y más"), "Performance"),
        };
        DndSwitch.IsChecked = Settings.Current.NotificationDoNotDisturb;
        AutoStartSwitch.IsChecked = AutoStart.IsEnabled;

        ApplyHeroBrush();
        ThemeManager.ThemeChanged += ApplyHeroBrush;
        UpdateService.Changed += OnUpdateChanged;
        Unloaded += (_, _) =>
        {
            ThemeManager.ThemeChanged -= ApplyHeroBrush;
            UpdateService.Changed -= OnUpdateChanged;
        };
        RefreshUpdate();
    }

    /// <summary>Degradado diagonal del color de acento a un tono más oscuro.</summary>
    private void ApplyHeroBrush()
    {
        var accent = (TryFindResource("WinxAccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x00, 0x67, 0xC0);
        Color Darker(double f) => Color.FromRgb((byte)(accent.R * f), (byte)(accent.G * f), (byte)(accent.B * f));
        Hero.Background = new LinearGradientBrush(
            [new GradientStop(accent, 0), new GradientStop(Darker(0.55), 1)],
            new Point(0, 0), new Point(1, 1));
    }

    private void OnUpdateChanged() => Dispatcher.BeginInvoke(RefreshUpdate);

    private void RefreshUpdate()
    {
        (UpdateIcon.Symbol, UpdateText.Text) = UpdateService.State switch
        {
            UpdateState.Available or UpdateState.Downloading when UpdateService.Available is { } u =>
                (SymbolRegular.ArrowCircleDown24, Loc.F("WINX {0} está disponible", u.Version.ToString(3))),
            UpdateState.UpToDate => (SymbolRegular.CheckmarkCircle24, Loc.T("Al día")),
            UpdateState.Checking => (SymbolRegular.ArrowSync24, Loc.T("Buscando…")),
            _ => (SymbolRegular.ArrowSync24, Loc.T("Buscar actualizaciones")),
        };
    }

    private void Navigate(string page) => (Window.GetWindow(this) as HubWindow)?.NavigateTo(page);

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Tile tile })
            Navigate(tile.Page);
    }

    private void UpdateChip_Click(object sender, RoutedEventArgs e) => Navigate("Updates");

    private void Dnd_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.NotificationDoNotDisturb = DndSwitch.IsChecked == true);

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AutoStart.IsEnabled = AutoStartSwitch.IsChecked == true;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el inicio automático", ex);
            AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
        }
    }
}
