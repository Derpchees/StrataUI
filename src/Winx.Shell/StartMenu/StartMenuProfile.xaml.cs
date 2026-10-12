using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.StartMenu;

/// <summary>
/// Menú Inicio "Perfil": como el de Windows 11, con la foto de la cuenta grande arriba,
/// sobresaliendo del borde del panel, más nombre y saludo.
/// </summary>
public partial class StartMenuProfile : StartMenuBase
{
    private ProfileAvatarWindow? _avatar;

    public StartMenuProfile()
    {
        InitializeComponent();
        Greeting.Text = GreetingFor(DateTime.Now);
        // La foto va en su propia ventana: se muestra cuando el menú ya está en su sitio.
        ContentRendered += (_, _) =>
        {
            _avatar ??= new ProfileAvatarWindow(this) { Owner = this };
            _avatar.Show();
            _avatar.Follow();
        };
    }

    protected override TextBox? SearchInput => SearchBox;

    /// <summary>Mitad superior de la foto, que sobresale por encima del menú.</summary>
    protected override double TopOverflow => 70;

    private static string GreetingFor(DateTime now)
    {
        string part = now.Hour switch
        {
            < 6 => Loc.T("Buenas noches"),
            < 12 => Loc.T("Buenos días"),
            < 20 => Loc.T("Buenas tardes"),
            _ => Loc.T("Buenas noches"),
        };
        // Fecha en el idioma de la interfaz (el formato también se traduce: "dddd, MMMM d" en inglés).
        var culture = System.Globalization.CultureInfo.GetCultureInfo(Loc.Language);
        return $"{part} · " + now.ToString(Loc.T("dddd d 'de' MMMM"), culture);
    }

    private void Search_KeyDown(object sender, KeyEventArgs e) => OnSearchKeyDown(sender, e);
    private void ShowAll_Click(object sender, RoutedEventArgs e) => OnShowAllApps(sender, e);
    private void Back_Click(object sender, RoutedEventArgs e) => OnBackToHome(sender, e);
}
