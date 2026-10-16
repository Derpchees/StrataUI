using System.IO;
using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;
using Strata.Shell.Services.Notifications;
using Strata.Shell.Services.Themes;
using Strata.Shell.Services.Widgets;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Hub › Módulos › Importar (--hub Import): todo lo que se importa en un solo lugar, cada tipo con su
/// botón para importar y la lista de lo importado: temas, animaciones, widgets, aspectos del menú
/// Inicio (archivos .json, ver <see cref="ModuleStore"/>) y sonidos de notificación
/// (<see cref="SoundLibrary"/>).
/// </summary>
public sealed class ImportPage : Page
{
    private readonly StackPanel _sounds = new();
    private readonly TextBlock _soundStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8), Visibility = Visibility.Collapsed };

    public ImportPage()
    {
        Title = Loc.T("Importar");
        var content = new StackPanel { Style = (Style)FindResource("HubPageContent") };
        content.Children.Add(new PageHeader
        {
            Icon = SymbolRegular.ArrowImport24,
            Title = Loc.T("Importar"),
            Subtitle = Loc.T("Temas, animaciones, widgets, menús de Inicio y sonidos que hiciste o que te compartieron."),
        });

        Section(content, Loc.T("Temas"), new ModulesSection(ModuleKind.Theme,
            Loc.T("Un estilo y, si quieres, sus propios colores. También aparecen en Tema y colores."),
            module => Action(Loc.T("Usar"), () =>
                AppearancePage.UseTheme(((ThemeModule)module).ToTheme()))));

        Section(content, Loc.T("Animaciones"), new ModulesSection(ModuleKind.Animation,
            Loc.T("Desde dónde llegan los paneles, cuánto crecen y con qué rebote."),
            module => Action(Loc.T("Usar"), () =>
            {
                Settings.Update(s => s.AnimationModule = module.Id);
                NotificationService.ShowTest(); // vista previa
            })));

        Section(content, Loc.T("Widgets"), new ModulesSection(ModuleKind.Widget,
            Loc.T("Líneas de texto con la hora, la fecha, la canción que suena o la batería, con sus fuentes y colores."),
            module => Action(Loc.T("Agregar"), () => WidgetManager.Add(WidgetKind.Custom, module: module.Id))));

        Section(content, Loc.T("Menú Inicio"), new ModulesSection(ModuleKind.StartMenu,
            Loc.T("Estilo, íconos, secciones, botones del pie, ícono de Inicio y tamaño del menú."),
            module => Action(Loc.T("Aplicar"), () => Settings.Update(((StartMenuModule)module).ApplyTo))));

        content.Children.Add(new TextBlock { Style = (Style)FindResource("HubSectionTitle"), Text = Loc.T("Sonidos de notificación") });
        content.Children.Add(SoundImportCard());
        _soundStatus.Style = (Style)FindResource("CardDescription");
        content.Children.Add(_soundStatus);
        content.Children.Add(_sounds);

        ScrollViewer.SetCanContentScroll(this, false);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Content = content };
        Loaded += (_, _) =>
        {
            SoundLibrary.Changed += OnSoundsChanged;
            BuildSounds();
        };
        Unloaded += (_, _) => SoundLibrary.Changed -= OnSoundsChanged;
    }

    private void Section(StackPanel content, string title, ModulesSection section)
    {
        content.Children.Add(new TextBlock { Style = (Style)FindResource("HubSectionTitle"), Text = title });
        content.Children.Add(section);
    }

    private static FrameworkElement Action(string text, Action action)
    {
        var button = new Wpf.Ui.Controls.Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }

    // ── Sonidos ─────────────────────────────────────────────────────────────

    private CardControl SoundImportCard()
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Loc.T("Importar") });
        header.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("CardDescription"),
            Text = Loc.T("Archivos .wav, .mp3, .m4a o .wma de hasta 5 MB. El que elijas suena con cada aviso en lugar del de Strata UI."),
        });
        var import = new Wpf.Ui.Controls.Button
        {
            Content = Loc.T("Importar…"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowImport24 },
            Appearance = ControlAppearance.Primary,
        };
        import.Click += async (_, _) =>
        {
            var (imported, errors) = await SoundLibrary.ImportAsync();
            var lines = new List<string>();
            if (imported > 0)
                lines.Add(Loc.F("Importados: {0}", imported));
            if (errors.Count > 0)
                lines.Add(Loc.T("No se pudo importar:") + "\n" + string.Join("\n", errors));
            _soundStatus.Text = string.Join("\n", lines);
            _soundStatus.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        return new CardControl
        {
            Margin = new Thickness(0, 0, 0, 6),
            Icon = new SymbolIcon { Symbol = SymbolRegular.MusicNote224 },
            Header = header,
            Content = import,
        };
    }

    private void OnSoundsChanged() => Dispatcher.BeginInvoke(BuildSounds);

    /// <summary>El tono de Strata UI y los importados; el que está en uso, marcado.</summary>
    private void BuildSounds()
    {
        _sounds.Children.Clear();
        string? current = SoundLibrary.Current;
        _sounds.Children.Add(SoundCard(null, Loc.T("Sonido de Strata UI"), current is null));
        foreach (var file in SoundLibrary.All())
            _sounds.Children.Add(SoundCard(file, Path.GetFileNameWithoutExtension(file),
                string.Equals(file, current, StringComparison.OrdinalIgnoreCase)));
    }

    private CardControl SoundCard(string? file, string name, bool inUse)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = name });
        if (inUse)
            header.Children.Add(new TextBlock { Style = (Style)FindResource("CardDescription"), Text = Loc.T("En uso") });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var play = new Wpf.Ui.Controls.Button { Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 }, ToolTip = Loc.T("Escuchar"), Margin = new Thickness(0, 0, 8, 0) };
        play.Click += (_, _) => NotificationSound.Play(file);
        buttons.Children.Add(play);
        if (!inUse)
        {
            var use = new Wpf.Ui.Controls.Button { Content = Loc.T("Usar"), Margin = new Thickness(0, 0, 8, 0) };
            use.Click += (_, _) => SoundLibrary.Use(file);
            buttons.Children.Add(use);
        }
        if (file != null)
        {
            var remove = new Wpf.Ui.Controls.Button { Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 }, ToolTip = Loc.T("Quitar") };
            System.Windows.Automation.AutomationProperties.SetName(remove, Loc.F("Quitar {0}", name));
            remove.Click += (_, _) => SoundLibrary.Delete(file);
            buttons.Children.Add(remove);
        }
        return new CardControl
        {
            Style = (Style)FindResource("HubCard"),
            Icon = new SymbolIcon { Symbol = inUse ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.MusicNote224 },
            Header = header,
            Content = buttons,
        };
    }
}
