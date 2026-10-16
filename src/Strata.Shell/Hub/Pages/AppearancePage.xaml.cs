using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Modules;
using Strata.Shell.Services.Themes;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Hub.Pages;

/// <summary>
/// Hub › Tema: la galería (estilos y esquemas de color) del monitor elegido, el vidrio, llevar el tema a Windows y las animaciones, con los temas y animaciones importados.
/// Los colores están en <see cref="ColorsPage"/>.
/// </summary>
public partial class AppearancePage : Page
{
    private bool _loading = true;

    public AppearancePage()
    {
        InitializeComponent();

        var s = Settings.Current;
        WindowsThemeSwitch.IsChecked = s.ApplyThemeToWindows;
        LiveGlassSwitch.IsChecked = s.LiveGlass;
        BuildAnimationChoices();
        Select(SpeedBox, s.AnimationSpeed.ToString());
        _loading = false;
        BuildThemeGallery();

        ThemeManager.ThemeChanged += BuildThemeGallery;
        ScreenPicker.Changed += OnScreenChanged;
        ModuleStore.Changed += OnModulesChanged;
        Unloaded += (_, _) =>
        {
            ThemeManager.ThemeChanged -= BuildThemeGallery;
            ScreenPicker.Changed -= OnScreenChanged;
            ModuleStore.Changed -= OnModulesChanged;
        };
    }

    /// <summary>
    /// Pone ese tema en todos los monitores (los que se conecten después lo toman de los generales):
    /// lo individual de cada monitor es su barra y sus widgets.
    /// </summary>
    internal static void UseTheme(ThemeInfo theme) => Settings.Update(s =>
    {
        s.ThemeModule = theme.ModuleId;
        if (theme.ModuleId is null)
            s.Theme = theme.Kind;
        foreach (var screen in s.Screens.Values)
        {
            screen.ThemeModule = s.ThemeModule;
            screen.Theme = s.Theme;
        }
    });

    /// <summary>Prefijo de la etiqueta de lo importado (en la galería y en la lista de animaciones).</summary>
    private const string Imported = "module:";

    private void OnModulesChanged(ModuleKind kind) => Dispatcher.BeginInvoke(() =>
    {
        if (kind == ModuleKind.Theme)
            BuildThemeGallery();
        else if (kind == ModuleKind.Animation)
            BuildAnimationChoices();
    });

    /// <summary>Otro monitor elegido: su tema.</summary>
    private void OnScreenChanged()
    {
        _loading = true;
        UpdateGallerySelection(CurrentStyleKey(), CurrentSchemeKey());
        _loading = false;
    }

    // ── Galería: estilos y esquemas de color, que se combinan ──────────────

    private const string SchemeTag = "scheme:";

    /// <summary>El estilo elegido (de la lista o importado), sin el esquema que lo tiñe.</summary>
    private static ThemeInfo CurrentStyle()
    {
        var s = Settings.Current;
        return ModuleStore.Get<ThemeModule>(ModuleKind.Theme, s.ThemeModule)?.ToTheme() ?? ThemeCatalog.Get(s.Theme);
    }

    private static string CurrentStyleKey() => CurrentStyle().Key;

    private static string CurrentSchemeKey() => SchemeTag + (Settings.Current.ColorScheme?.ToString() ?? "none");

    /// <summary>Las miniaturas llevan el acento y el estilo o esquema actual: se rehacen cuando cambian.</summary>
    private void BuildThemeGallery()
    {
        ThemeGallery.Children.Clear();
        SchemeGallery.Children.Clear();
        ImportedGallery.Children.Clear();
        var modules = ModuleStore.All<ThemeModule>(ModuleKind.Theme);
        ImportedTitle.Visibility = modules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var style = CurrentStyle();
        string styleKey = style.Key, schemeKey = CurrentSchemeKey();
        var scheme = Settings.Current.ColorScheme;

        // Estilos (cada uno con el esquema elegido, como quedaría) e importados.
        foreach (var theme in ThemeCatalog.All.Where(t => !t.IsScheme).Concat(modules.Select(m => m.ToTheme())))
        {
            var card = GalleryCard(ThemeCatalog.Combine(theme, scheme), theme.Name, theme.Description, theme.Key, theme.Key == styleKey);
            card.Click += (_, _) =>
            {
                // Primero se marca la selección (respuesta inmediata) y después se aplica.
                UpdateGallerySelection(theme.Key, schemeKey);
                Dispatcher.BeginInvoke(() => UseTheme(theme), System.Windows.Threading.DispatcherPriority.Background);
            };
            if (theme.ModuleId is { } id)
            {
                var remove = new MenuItem { Header = Loc.T("Quitar"), Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete24 } };
                remove.Click += (_, _) => ModuleStore.Delete(ModuleKind.Theme, id);
                card.ContextMenu = new ContextMenu { Items = { remove } };
                ImportedGallery.Children.Add(card);
            }
            else
                ThemeGallery.Children.Add(card);
        }

        // Esquemas sobre el estilo elegido; el primero, sin esquema (los colores del estilo).
        var choices = new List<(StrataTheme? Kind, string Name, string Description)>
            { (null, Loc.T("Colores del estilo"), Loc.T("Sin esquema: los colores propios del estilo.")) };
        choices.AddRange(ThemeCatalog.All.Where(t => t.IsScheme).Select(t => ((StrataTheme?)t.Kind, t.Name, t.Description)));
        foreach (var (kind, name, description) in choices)
        {
            string tag = SchemeTag + (kind?.ToString() ?? "none");
            var card = GalleryCard(ThemeCatalog.Combine(style, kind), name, description, tag, tag == schemeKey);
            // Los importados traen sus propios colores: el esquema no los tiñe.
            card.IsEnabled = style.ModuleId is null;
            card.Click += (_, _) =>
            {
                UpdateGallerySelection(styleKey, tag);
                Dispatcher.BeginInvoke(() => Settings.Update(s => s.ColorScheme = kind),
                                       System.Windows.Threading.DispatcherPriority.Background);
            };
            SchemeGallery.Children.Add(card);
        }
    }

    private Button GalleryCard(ThemeInfo preview, string name, string description, string tag, bool selected)
    {
        var card = new Button
        {
            Width = 168,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = description,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = BuildThemeCard(preview, name, selected),
            Template = FlatTemplate(),
            Tag = tag,
        };
        System.Windows.Automation.AutomationProperties.SetName(card, Loc.F("Tema {0}", name));
        return card;
    }

    private void UpdateGallerySelection(string styleKey, string schemeKey)
    {
        foreach (Button card in ThemeGallery.Children.OfType<Button>().Concat(SchemeGallery.Children.OfType<Button>())
                     .Concat(ImportedGallery.Children.OfType<Button>()))
        {
            if (card.Content is not Border frame)
                continue;
            bool isSelected = (string)card.Tag == styleKey || (string)card.Tag == schemeKey;
            frame.BorderThickness = new Thickness(isSelected ? 2 : 1);
            frame.BorderBrush = isSelected
                ? (TryFindResource("AccentFillColorDefaultBrush") as Brush) ?? Brushes.DodgerBlue
                : (TryFindResource("CardStrokeColorDefaultBrush") as Brush) ?? Brushes.Gray;
        }
    }

    /// <summary>Miniatura: fondo de colores, una ventana y una barra con los colores reales del tema.</summary>
    private FrameworkElement BuildThemeCard(ThemeInfo theme, string name, bool selected)
    {
        bool light = theme.Light ?? ThemeManager.WindowsUsesLightTheme();
        var palette = Palette.Build(theme, light, ThemeManager.Readable(ThemeManager.AccentFor(theme), light));
        Brush Res(string key) => (Brush)palette[key];

        var wallpaper = new LinearGradientBrush(Palette.Hex("#FF5B7FFF"), Palette.Hex("#FFFF7AB6"), 35);
        wallpaper.GradientStops.Add(new GradientStop(Palette.Hex("#FFFFC46B"), 1));

        var preview = new Grid { Height = 96, ClipToBounds = true };
        preview.Children.Add(new Border { Background = wallpaper });
        // Vidrio sin desenfoque real en la miniatura: se aumenta la opacidad para insinuarlo.
        var window = new Border
        {
            Width = 92, Height = 52, Margin = new Thickness(14, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = (CornerRadius)palette["StrataLargeCornerRadius"],
            Background = Opaque(Res("StrataFlyoutBackgroundBrush"), light, theme),
            BorderBrush = theme.Glass ? Res("StrataGlassEdgeBrush") : Res("StrataFlyoutBorderBrush"),
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Margin = new Thickness(8, 8, 0, 0), Width = 40, Height = 6,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(3), Background = Res("StrataAccentBrush"),
            },
        };
        preview.Children.Add(window);
        var taskbar = new Border
        {
            Height = 18, VerticalAlignment = VerticalAlignment.Bottom,
            // Transparente total: la barra sin fondo, solo su ícono sobre el fondo de pantalla.
            Background = theme.ClearBar ? Brushes.Transparent : Opaque(Res("StrataTaskbarBackgroundBrush"), light, theme),
            Child = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(theme.Radius / 3),
                Background = Res("StrataAccentBrush"), HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        preview.Children.Add(taskbar);
        if (theme.Glass)
            preview.Children.Add(new Border { Background = Res("StrataGlassHighlightBrush"), Opacity = 0.6, IsHitTestVisible = false });

        var label = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        label.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = name });

        var stack = new StackPanel();
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(6, 6, 0, 0), ClipToBounds = true, Child = preview });
        stack.Children.Add(label);

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = (TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush) ?? Brushes.Transparent,
            BorderBrush = selected
                ? (TryFindResource("AccentFillColorDefaultBrush") as Brush) ?? Brushes.DodgerBlue
                : (TryFindResource("CardStrokeColorDefaultBrush") as Brush) ?? Brushes.Gray,
            BorderThickness = new Thickness(selected ? 2 : 1),
            Child = stack,
        };
    }

    private static Brush Opaque(Brush brush, bool light, ThemeInfo theme)
    {
        if (brush is not SolidColorBrush solid || theme.Backdrop != Backdrop.Acrylic)
            return brush;
        // En la miniatura no hay Acrylic: se mezcla con el tono que tendría el desenfoque.
        var frosted = light ? Palette.Hex("#D8F0F0F5") : Palette.Hex("#D0202024");
        return new SolidColorBrush(Palette.Blend(frosted, solid.Color, solid.Color.A / 255.0));
    }

    private static ControlTemplate FlatTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return template;
    }

    private void LiveGlass_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.LiveGlass = LiveGlassSwitch.IsChecked == true);

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    /// <summary>Las animaciones incluidas (del XAML) y, después, las importadas.</summary>
    private void BuildAnimationChoices()
    {
        bool loading = _loading;
        _loading = true;
        foreach (var item in AnimationBox.Items.OfType<ComboBoxItem>().Where(i => ((string)i.Tag).StartsWith(Imported)).ToList())
            AnimationBox.Items.Remove(item);
        foreach (var module in ModuleStore.All<AnimationModule>(ModuleKind.Animation))
            AnimationBox.Items.Add(new ComboBoxItem { Content = module.Name, Tag = Imported + module.Id });
        var s = Settings.Current;
        string chosen = s.AnimationModule is { } id && ModuleStore.Get<AnimationModule>(ModuleKind.Animation, id) is { } found
            ? Imported + found.Id
            : s.PanelAnimation.ToString();
        Select(AnimationBox, chosen);
        _loading = loading;
    }

    private void SelectAnimation(string tag)
    {
        if (AnimationBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) is { } item)
            AnimationBox.SelectedItem = item; // Animation_Changed guarda y muestra la vista previa
    }

    private void Animation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AnimationBox.SelectedItem is not ComboBoxItem { Tag: string style }
            || SpeedBox.SelectedItem is not ComboBoxItem { Tag: string speed })
            return;
        Settings.Update(s =>
        {
            if (style.StartsWith(Imported))
                s.AnimationModule = style[Imported.Length..];
            else
            {
                s.AnimationModule = null;
                s.PanelAnimation = Enum.Parse<PanelAnimation>(style);
            }
            s.AnimationSpeed = Enum.Parse<AnimationSpeed>(speed);
        });
        // Vista previa: un aviso de prueba con la animación elegida.
        Services.Notifications.NotificationService.ShowTest();
    }

    private void WindowsTheme_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.ApplyThemeToWindows = WindowsThemeSwitch.IsChecked == true);
}
