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
        BuildAnimationChoices();
        BuildCursorChoices();
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

        // Esquemas sobre el estilo elegido; el primero, sin esquema: los colores del estilo, claros u
        // oscuros según Windows. Después Claro y Oscuro (los colores del estilo, fijos) y los de color.
        var choices = new List<(StrataTheme? Kind, string Name, string Description)>
            { (null, Loc.T("Según Windows"), Loc.T("Los colores del estilo, claros u oscuros como Windows.")) };
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

    private const double PreviewWidth = 168, PreviewHeight = 100;

    /// <summary>
    /// Miniatura: un escritorio pequeño con un panel y la barra, con los colores reales del tema. En los
    /// estilos de vidrio el fondo se desenfoca de verdad detrás del panel y de la barra (con la misma
    /// transparencia que usan), así se nota la diferencia entre Mica, Translúcido, Transparente total y
    /// Sólido. El texto, las tarjetas y el acento salen de la paleta, así se reconoce cada esquema.
    /// </summary>
    private FrameworkElement BuildThemeCard(ThemeInfo theme, string name, bool selected)
    {
        bool light = theme.Light ?? ThemeManager.WindowsUsesLightTheme();
        var palette = Palette.Build(theme, light, ThemeManager.Readable(ThemeManager.AccentFor(theme), light));
        Brush Res(string key) => (Brush)palette[key];
        bool glassy = theme.Backdrop is Backdrop.Acrylic or Backdrop.Blur or Backdrop.Mica;

        var preview = new Grid { Width = PreviewWidth, Height = PreviewHeight, ClipToBounds = true };
        preview.Children.Add(Wallpaper(blur: 0));

        // Panel (menú Inicio, avisos, Hub…): fondo, borde, texto, una tarjeta y un botón de acento.
        var panelRadius = (CornerRadius)palette["StrataLargeCornerRadius"];
        var panelRect = new Rect(12, 10, 104, 58);
        if (glassy)
            preview.Children.Add(Frosted(panelRect, panelRadius));
        var lines = new StackPanel { Margin = new Thickness(8, 8, 8, 0) };
        lines.Children.Add(Line(44, 4, Res("StrataForegroundBrush"), 0.9));
        lines.Children.Add(Line(64, 3, Res("StrataSecondaryForegroundBrush"), 0.75, top: 5));
        lines.Children.Add(Line(52, 3, Res("StrataSecondaryForegroundBrush"), 0.75, top: 3));
        lines.Children.Add(new Border
        {
            Margin = new Thickness(0, 7, 0, 0), Height = 16, CornerRadius = new CornerRadius(4),
            Background = Res("StrataSurfaceBrush"),
            Child = new Border
            {
                Width = 24, Height = 7, Margin = new Thickness(0, 0, 5, 0), CornerRadius = new CornerRadius(3.5),
                HorizontalAlignment = HorizontalAlignment.Right, Background = Res("StrataAccentBrush"),
            },
        });
        preview.Children.Add(new Border
        {
            Width = panelRect.Width, Height = panelRect.Height, Margin = new Thickness(panelRect.X, panelRect.Y, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = panelRadius,
            Background = Res("StrataFlyoutBackgroundBrush"),
            BorderBrush = theme.Glass ? Res("StrataGlassEdgeBrush") : Res("StrataFlyoutBorderBrush"),
            BorderThickness = new Thickness(1),
            Child = lines,
        });

        // Barra: sin fondo en Transparente total; si no, su color sobre el fondo desenfocado.
        const double barHeight = 20;
        var barRect = new Rect(0, PreviewHeight - barHeight, PreviewWidth, barHeight);
        if (glassy && !theme.ClearBar)
            preview.Children.Add(Frosted(barRect, new CornerRadius(0)));
        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        icons.Children.Add(TaskIcon(Res("StrataAccentBrush"), null, null, theme));
        string[] appColors = { "#FF4CC2FF", "#FFFFB547", "#FF6CCB5F" };
        for (int i = 0; i < appColors.Length; i++)
            icons.Children.Add(TaskIcon(new SolidColorBrush(Palette.Hex(appColors[i])),
                                        i == 0 ? Res("StrataActiveTaskBrush") : null,
                                        i == 0 ? Res("StrataAccentBrush") : Res("StrataInactiveIndicatorBrush"), theme));
        preview.Children.Add(new Border
        {
            Height = barHeight, VerticalAlignment = VerticalAlignment.Bottom,
            Background = theme.ClearBar ? Brushes.Transparent : Res("StrataTaskbarBackgroundBrush"),
            BorderBrush = theme.ClearBar ? Brushes.Transparent : Res("StrataTaskbarBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = icons,
        });
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

    /// <summary>Renglón de texto de muestra (una barrita redondeada del color del texto).</summary>
    private static Border Line(double width, double height, Brush brush, double opacity, double top = 0) => new()
    {
        Width = width, Height = height, Margin = new Thickness(0, top, 0, 0), CornerRadius = new CornerRadius(height / 2),
        HorizontalAlignment = HorizontalAlignment.Left, Background = brush, Opacity = opacity,
    };

    /// <summary>Ícono de la barra con su placa (si es la app en foco) y su punto, como en la barra real.</summary>
    private static FrameworkElement TaskIcon(Brush icon, Brush? plate, Brush? dot, ThemeInfo theme)
    {
        var cell = new Grid { Width = 18, Height = 18, Margin = new Thickness(1, 1, 1, 0) };
        if (plate != null)
            cell.Children.Add(new Border { CornerRadius = new CornerRadius(Math.Min(theme.Radius / 2.0, 5)), Background = plate });
        cell.Children.Add(new Border
        {
            Width = 9, Height = 9, Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(Math.Min(theme.Radius / 3.0, 3)),
            Background = icon, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        if (dot != null)
            cell.Children.Add(new Border
            {
                Width = 5, Height = 1.5, Margin = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(0.75),
                Background = dot, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            });
        return cell;
    }

    /// <summary>Fondo de pantalla de muestra: un degradado con manchas de color, para que se note el vidrio.</summary>
    private static FrameworkElement Wallpaper(double blur)
    {
        var gradient = new LinearGradientBrush(Palette.Hex("#FF4F6BFF"), Palette.Hex("#FFFF6FAE"), 35);
        gradient.GradientStops.Add(new GradientStop(Palette.Hex("#FFFFC46B"), 1));
        var canvas = new Canvas { Width = PreviewWidth, Height = PreviewHeight, Background = gradient };
        void Blob(double x, double y, double size, string color)
        {
            var e = new System.Windows.Shapes.Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Palette.Hex(color)) };
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            canvas.Children.Add(e);
        }
        Blob(-14, 34, 54, "#FF2DE0C8");
        Blob(70, -18, 46, "#FFFFFFFF");
        Blob(96, 44, 40, "#FF1E1E5A");
        Blob(132, 6, 30, "#FFFF4D6D");
        if (blur > 0)
            canvas.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = blur, KernelType = System.Windows.Media.Effects.KernelType.Gaussian };
        return canvas;
    }

    /// <summary>El fondo desenfocado que se ve detrás de un panel o una barra de vidrio, recortado a su forma.</summary>
    private static FrameworkElement Frosted(Rect rect, CornerRadius radius)
    {
        var blurred = Wallpaper(blur: 14);
        Canvas.SetLeft(blurred, -rect.X);
        Canvas.SetTop(blurred, -rect.Y);
        return new Canvas
        {
            Width = rect.Width, Height = rect.Height, Margin = new Thickness(rect.X, rect.Y, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Clip = new RectangleGeometry(new Rect(0, 0, rect.Width, rect.Height), radius.TopLeft, radius.TopLeft),
            Children = { blurred },
        };
    }

    private static ControlTemplate FlatTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return template;
    }


    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    /// <summary>Las animaciones incluidas (del XAML) y, después, las importadas.</summary>
    private void BuildAnimationChoices()
    {
        bool loading = _loading;
        _loading = true;
        foreach (var item in AnimationBox.Items.OfType<ComboBoxItem>().Where(i => ((string)i.Tag).StartsWith(Imported, StringComparison.Ordinal)).ToList())
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

    private void Animation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AnimationBox.SelectedItem is not ComboBoxItem { Tag: string style }
            || SpeedBox.SelectedItem is not ComboBoxItem { Tag: string speed })
            return;
        Settings.Update(s =>
        {
            if (style.StartsWith(Imported, StringComparison.Ordinal))
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

    /// <summary>Los de Windows, los propios y los esquemas instalados.</summary>
    private void BuildCursorChoices()
    {
        CursorBox.Items.Add(new ComboBoxItem { Content = Loc.T("Los de Windows"), Tag = "" });
        CursorBox.Items.Add(new ComboBoxItem { Content = Loc.T("Strata blanco"), Tag = "Strata White" });
        CursorBox.Items.Add(new ComboBoxItem { Content = Loc.T("Strata negro"), Tag = "Strata Black" });
        foreach (var name in WindowsCursors.Installed())
            CursorBox.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        Select(CursorBox, Settings.Current.CursorScheme ?? "");
    }

    private void Cursor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && CursorBox.SelectedItem is ComboBoxItem { Tag: string name })
            Settings.Update(s => s.CursorScheme = name.Length == 0 ? null : name);
    }

    private void WindowsTheme_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.ApplyThemeToWindows = WindowsThemeSwitch.IsChecked == true);
}
