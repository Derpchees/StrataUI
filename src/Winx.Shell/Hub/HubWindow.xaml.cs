using Winx.Shell.Hub.Pages;
using Wpf.Ui.Controls;

namespace Winx.Shell.Hub;

/// <summary>Ventana central de configuración de WINX.</summary>
public partial class HubWindow : FluentWindow
{
    public HubWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Nav.Navigate(typeof(TaskbarPage));
    }
}
