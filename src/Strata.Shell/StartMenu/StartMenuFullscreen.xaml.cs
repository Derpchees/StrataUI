using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Strata.Shell.StartMenu;

/// <summary>Menú Inicio a pantalla completa (tipo Launchpad): buscador grande y cuadrícula de apps.</summary>
public partial class StartMenuFullscreen : StartMenuBase
{
    public StartMenuFullscreen() => InitializeComponent();

    protected override TextBox? SearchInput => SearchBox;

    protected override bool FillWorkArea => true;

    private void Search_KeyDown(object sender, KeyEventArgs e) => OnSearchKeyDown(sender, e);

    private void Background_Click(object sender, MouseButtonEventArgs e)
    {
        // Solo si el clic fue en el fondo, no sobre una app o el buscador.
        if (ReferenceEquals(e.OriginalSource, sender))
            CloseFlyout();
    }
}
