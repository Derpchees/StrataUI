using Winx.Shell.Hub.Pages;
using Wpf.Ui.Controls;

namespace Winx.Shell.Hub;

/// <summary>Ventana central de configuración de WINX.</summary>
public partial class HubWindow : FluentWindow
{
    private Type _startPage = typeof(TaskbarPage);

    public HubWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Nav.Navigate(_startPage);
    }

    /// <summary>Navega a una página por nombre: "Privacy", "PrivacyPage", "Apps"…</summary>
    public void NavigateTo(string page)
    {
        var type = typeof(TaskbarPage).Assembly.GetTypes().FirstOrDefault(t =>
            t.Namespace == typeof(TaskbarPage).Namespace && !t.IsAbstract &&
            (t.Name.Equals(page, StringComparison.OrdinalIgnoreCase) ||
             t.Name.Equals(page + "Page", StringComparison.OrdinalIgnoreCase)));
        if (type is null)
            return;
        _startPage = type;
        if (IsLoaded)
            Nav.Navigate(type);
    }
}
