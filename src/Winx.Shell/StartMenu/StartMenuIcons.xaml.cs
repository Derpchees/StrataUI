using System.Windows.Controls;
using System.Windows.Input;

namespace Winx.Shell.StartMenu;

/// <summary>Menú Inicio de íconos: cuadrícula sin texto, el nombre aparece al pasar el mouse.</summary>
public partial class StartMenuIcons : StartMenuBase
{
    public StartMenuIcons() => InitializeComponent();

    protected override TextBox? SearchInput => SearchBox;

    private void Search_KeyDown(object sender, KeyEventArgs e) => OnSearchKeyDown(sender, e);
}
