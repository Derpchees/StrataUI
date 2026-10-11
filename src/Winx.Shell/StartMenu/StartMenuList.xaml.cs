using System.Windows.Controls;
using System.Windows.Input;
using Winx.Shell.Flyouts;

namespace Winx.Shell.StartMenu;

/// <summary>Menú Inicio estilo lista (Windows 10/7): todas las apps a la izquierda, anclados a la derecha.</summary>
public partial class StartMenuList : StartMenuBase
{
    public StartMenuList() => InitializeComponent();

    protected override TextBox? SearchInput => SearchBox;

    protected override FlyoutPlacement Placement => FlyoutPlacement.Left;

    private void Search_KeyDown(object sender, KeyEventArgs e) => OnSearchKeyDown(sender, e);
}
