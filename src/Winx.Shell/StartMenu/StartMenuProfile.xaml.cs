using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
            < 6 => "Buenas noches",
            < 12 => "Buenos días",
            < 20 => "Buenas tardes",
            _ => "Buenas noches",
        };
        return $"{part} · " + now.ToString("dddd d 'de' MMMM", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
    }

    private void Search_KeyDown(object sender, KeyEventArgs e) => OnSearchKeyDown(sender, e);
    private void ShowAll_Click(object sender, RoutedEventArgs e) => OnShowAllApps(sender, e);
    private void Back_Click(object sender, RoutedEventArgs e) => OnBackToHome(sender, e);
}
