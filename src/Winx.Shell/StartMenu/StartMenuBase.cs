using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Winx.Shell.Flyouts;
using Winx.Shell.Services;

namespace Winx.Shell.StartMenu;

/// <summary>
/// Comportamiento común de los estilos de menú Inicio: modelo compartido, foco en el buscador,
/// escribir en cualquier parte busca, Enter abre el primer resultado y Esc limpia antes de cerrar.
/// </summary>
public abstract class StartMenuBase : FlyoutWindow
{
    protected StartMenuBase()
    {
        DataContext = Model;
        Loaded += (_, _) => SearchInput?.Focus();
        PreviewTextInput += (_, e) =>
        {
            // Empezar a escribir con el foco en otro control lleva el texto al buscador.
            if (SearchInput is { IsKeyboardFocusWithin: false } box && !string.IsNullOrWhiteSpace(e.Text))
            {
                box.Focus();
                box.AppendText(e.Text);
                box.CaretIndex = box.Text.Length;
                e.Handled = true;
            }
        };
    }

    public StartMenuModel Model { get; } = new();

    /// <summary>Caja de búsqueda del estilo concreto.</summary>
    protected abstract TextBox? SearchInput { get; }

    protected override bool ScrollContent => false;

    protected override FlyoutPlacement Placement =>
        Settings.Current.TaskbarAlignment == TaskbarAlignment.Left ? FlyoutPlacement.Left : FlyoutPlacement.Center;

    protected override bool HandleEscape()
    {
        if (Model.ShowAllApps || Model.IsSearching)
        {
            Model.Query = "";
            Model.ShowAllApps = false;
            return true;
        }
        return false;
    }

    protected void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Model.Submit();
            e.Handled = true;
        }
    }

    protected void OnShowAllApps(object sender, RoutedEventArgs e) => Model.ShowAllApps = true;

    protected void OnBackToHome(object sender, RoutedEventArgs e) => Model.ShowAllApps = false;
}
