using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub;

/// <summary>
/// Sección "Importados" de una página del Hub: el botón para importar módulos de ese tipo, el resultado de la última importación y (si se pide) la lista de los
/// importados con su acción ("Aplicar", "Agregar"…) y "Quitar".
/// </summary>
public sealed class ModulesSection : StackPanel
{
    private readonly ModuleKind _kind;
    private readonly Func<ModuleBase, FrameworkElement?>? _action;
    private readonly bool _showList;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8), Visibility = Visibility.Collapsed };
    private readonly WrapPanel _buttons = new() { HorizontalAlignment = HorizontalAlignment.Right };

    /// <summary>Se importó algo (para elegirlo enseguida, p. ej. el primer tema importado).</summary>
    public event Action<IReadOnlyList<ModuleBase>>? Imported;

    public ModulesSection(ModuleKind kind, string description, Func<ModuleBase, FrameworkElement?>? action = null, bool showList = true)
    {
        _kind = kind;
        _action = action;
        _showList = showList;

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = Loc.T("Importar") });
        header.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        header.Loaded += (_, _) =>
        {
            ((TextBlock)header.Children[0]).Style = (Style)FindResource("CardTitle");
            ((TextBlock)header.Children[1]).Style = (Style)FindResource("CardDescription");
            _status.Style = (Style)FindResource("CardDescription");
        };

        var import = new Wpf.Ui.Controls.Button
        {
            Content = Loc.T("Importar…"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowImport24 },
            Appearance = ControlAppearance.Primary,
        };
        import.Click += async (_, _) => await ImportAsync();
        _buttons.Children.Add(import);

        Children.Add(new CardControl
        {
            Margin = new Thickness(0, 0, 0, 6),
            Icon = new SymbolIcon { Symbol = SymbolRegular.PuzzlePiece24 },
            Header = header,
            Content = _buttons,
        });
        Children.Add(_status);
        Children.Add(_list);

        Loaded += (_, _) =>
        {
            ModuleStore.Changed += OnChanged;
            ModuleStore.Reload(_kind); // por si se editaron o copiaron a mano
            Build();
        };
        Unloaded += (_, _) => ModuleStore.Changed -= OnChanged;
    }

    /// <summary>Mensaje bajo la tarjeta (resultado de importar o exportar).</summary>
    public void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChanged(ModuleKind kind)
    {
        if (kind == _kind)
            Dispatcher.BeginInvoke(Build);
    }

    private async Task ImportAsync()
    {
        var (imported, errors) = await ModuleStore.ImportAsync(_kind);
        var lines = new List<string>();
        if (imported.Count > 0)
            lines.Add(Loc.F("Importado: {0}", string.Join(", ", imported.Select(m => m.Name))));
        if (errors.Count > 0)
            lines.Add(Loc.T("No se pudo importar:") + "\n" + string.Join("\n", errors));
        ShowStatus(string.Join("\n", lines));
        if (imported.Count > 0)
            Imported?.Invoke(imported);
    }

    private void Build()
    {
        _list.Children.Clear();
        if (!_showList)
            return;
        foreach (var module in ModuleStore.All<ModuleBase>(_kind))
        {
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = module.Name });
            if (module.Summary.Length > 0)
                header.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = module.Summary, TextWrapping = TextWrapping.Wrap });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            if (_action?.Invoke(module) is { } action)
            {
                action.Margin = new Thickness(0, 0, 8, 0);
                buttons.Children.Add(action);
            }
            var remove = new Wpf.Ui.Controls.Button
            {
                Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 },
                ToolTip = Loc.T("Quitar"),
            };
            System.Windows.Automation.AutomationProperties.SetName(remove, Loc.F("Quitar {0}", module.Name));
            remove.Click += (_, _) => ModuleStore.Delete(_kind, module.Id);
            buttons.Children.Add(remove);

            _list.Children.Add(new CardControl
            {
                Style = (Style)FindResource("HubCard"),
                Header = header,
                Content = buttons,
            });
        }
    }

    /// <summary>El formato de los módulos, con ejemplos, en el repositorio.</summary>
    internal static void OpenFormat()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"https://github.com/{Services.Updates.UpdateService.Repository}/blob/main/docs/modules/README.md")
            { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Sin navegador predeterminado: no hay nada más que hacer.
        }
    }
}
