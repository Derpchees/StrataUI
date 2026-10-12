using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Winx.Shell.Services;
using Winx.Shell.Services.Themes;
using Winx.Shell.Services.Localization;

namespace Winx.Shell.Hub.Pages;

public partial class AppearancePage : Page
{
    // Paleta de acentos de Windows.
    private static readonly string[] AccentPalette =
    [
        "#FFB900", "#FF8C00", "#F7630C", "#CA5010", "#DA3B01", "#EF6950", "#D13438", "#FF4343",
        "#E74856", "#E81123", "#EA005E", "#C30052", "#E3008C", "#BF0077", "#C239B3", "#9A0089",
        "#0078D4", "#0063B1", "#8E8CD8", "#6B69D6", "#8764B8", "#744DA9", "#B146C2", "#881798",
        "#0099BC", "#2D7D9A", "#00B7C3", "#038387", "#00B294", "#018574", "#00CC6A", "#10893E",
        "#7A7574", "#5D5A58", "#68768A", "#515C6B", "#567C73", "#486860", "#498205", "#107C10",
    ];

    private bool _loading = true;

    public AppearancePage()
    {
        InitializeComponent();

        var s = Settings.Current;
        WindowsThemeSwitch.IsChecked = s.ApplyThemeToWindows;
        LiveGlassSwitch.IsChecked = s.LiveGlass;
        TransparentSwitch.IsChecked = s.TaskbarFullyTransparent;
        Select(AnimationBox, s.PanelAnimation.ToString());
        Select(SpeedBox, s.AnimationSpeed.ToString());
        Select(AccentSourceBox, s.AccentFromWallpaper ? "Wallpaper" : s.AccentColor is null ? "Windows" : "Custom");
        _loading = false;
        BuildThemeGallery();
        BuildSwatches();
        UpdateSwatches();
        UpdateWallpaper();

        // Wallpaper Engine pudo abrirse, cerrarse o cambiar su opción mientras el Hub estaba cerrado.
        Services.Themes.WallpaperEngine.Recheck();
        UpdateWallpaperEngine();
        Services.Themes.WallpaperEngine.Changed += UpdateWallpaperEngine;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) =>
        {
            Services.Themes.WallpaperEngine.Changed -= UpdateWallpaperEngine;
            ThemeManager.ThemeChanged -= OnThemeChanged;
        };
    }

    /// <summary>Las miniaturas de los temas llevan el acento: se rehacen al cambiarlo.</summary>
    private void OnThemeChanged()
    {
        BuildThemeGallery();
        UpdateSwatches();
    }

    private void UpdateWallpaperEngine()
    {
        bool blocked = Services.Themes.WallpaperEngine.AdjustingWindowsColor;
        WallpaperEngineBar.IsOpen = blocked;
        WallpaperAccentItem.IsEnabled = !blocked;
        WallpaperAccentItem.Content = blocked ? Loc.T("Del fondo de pantalla (en pausa)") : Loc.T("Del fondo de pantalla");
    }

    // ── Galería de temas ───────────────────────────────────────────────────

    private void BuildThemeGallery()
    {
        ThemeGallery.Children.Clear();
        foreach (var theme in ThemeCatalog.All)
        {
            bool selected = theme.Kind == Settings.Current.Theme;
            var card = new Button
            {
                Width = 168,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = theme.Description,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = BuildThemeCard(theme, selected),
                Template = FlatTemplate(),
            };
            System.Windows.Automation.AutomationProperties.SetName(card, Loc.F("Tema {0}", theme.Name));
            card.Tag = theme.Kind;
            card.Click += (_, _) =>
            {
                // Primero se marca la selección (respuesta inmediata) y después se aplica el tema.
                UpdateGallerySelection(theme.Kind);
                Dispatcher.BeginInvoke(() => Settings.Update(s => s.Theme = theme.Kind),
                                       System.Windows.Threading.DispatcherPriority.Background);
            };
            ThemeGallery.Children.Add(card);
        }
    }

    private void UpdateGallerySelection(WinxTheme selected)
    {
        foreach (Button card in ThemeGallery.Children)
        {
            if (card.Content is not Border frame)
                continue;
            bool isSelected = (WinxTheme)card.Tag == selected;
            frame.BorderThickness = new Thickness(isSelected ? 2 : 1);
            frame.BorderBrush = isSelected
                ? (TryFindResource("AccentFillColorDefaultBrush") as Brush) ?? Brushes.DodgerBlue
                : (TryFindResource("CardStrokeColorDefaultBrush") as Brush) ?? Brushes.Gray;
        }
    }

    /// <summary>Miniatura: fondo de colores, una ventana y una barra con los colores reales del tema.</summary>
    private FrameworkElement BuildThemeCard(ThemeInfo theme, bool selected)
    {
        bool light = theme.Light ?? ThemeManager.WindowsUsesLightTheme();
        var palette = Palette.Build(theme, light, ThemeManager.Readable(ThemeManager.Accent, light));
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
            CornerRadius = (CornerRadius)palette["WinxLargeCornerRadius"],
            Background = Opaque(Res("WinxFlyoutBackgroundBrush"), light, theme),
            BorderBrush = theme.Glass ? Res("WinxGlassEdgeBrush") : Res("WinxFlyoutBorderBrush"),
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Margin = new Thickness(8, 8, 0, 0), Width = 40, Height = 6,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(3), Background = Res("WinxAccentBrush"),
            },
        };
        preview.Children.Add(window);
        var taskbar = new Border
        {
            Height = 18, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Opaque(Res("WinxTaskbarBackgroundBrush"), light, theme),
            Child = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(theme.Radius / 3),
                Background = Res("WinxAccentBrush"), HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        preview.Children.Add(taskbar);
        if (theme.Glass)
            preview.Children.Add(new Border { Background = Res("WinxGlassHighlightBrush"), Opacity = 0.6, IsHitTestVisible = false });

        var label = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        label.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = theme.Name });

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

    private void Transparent_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.TaskbarFullyTransparent = TransparentSwitch.IsChecked == true);

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    private void Animation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AnimationBox.SelectedItem is not ComboBoxItem { Tag: string style }
            || SpeedBox.SelectedItem is not ComboBoxItem { Tag: string speed })
            return;
        Settings.Update(s =>
        {
            s.PanelAnimation = Enum.Parse<PanelAnimation>(style);
            s.AnimationSpeed = Enum.Parse<AnimationSpeed>(speed);
        });
        // Vista previa: un aviso de prueba con la animación elegida.
        Services.Notifications.NotificationService.ShowTest();
    }

    private void WindowsTheme_Click(object sender, RoutedEventArgs e) =>
        Settings.Update(s => s.ApplyThemeToWindows = WindowsThemeSwitch.IsChecked == true);

    private void BuildSwatches()
    {
        foreach (var hex in AccentPalette)
        {
            var swatch = new Button
            {
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 6, 6),
                Tag = hex,
                ToolTip = hex,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            swatch.Click += (_, _) => Settings.Update(s =>
            {
                s.AccentColor = hex;
                s.AccentFromWallpaper = false;
            });
            swatch.Click += (_, _) => UpdateSwatches();
            Swatches.Children.Add(swatch);
        }
    }

    private void UpdateSwatches()
    {
        var s = Settings.Current;
        var current = s.AccentFromWallpaper ? null : s.AccentColor;
        SwatchPanel.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
        foreach (Button swatch in Swatches.Children)
        {
            bool selected = string.Equals((string)swatch.Tag, current, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(selected ? 3 : 0);
            swatch.BorderBrush = (Brush)FindResource("TextFillColorPrimaryBrush");
        }
    }

    private void AccentSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentSourceBox.SelectedItem is not ComboBoxItem { Tag: string source })
            return;
        // Al pasar a color propio se parte del acento que se ve ahora.
        var shown = ThemeManager.Accent;
        Settings.Update(s =>
        {
            s.AccentFromWallpaper = source == "Wallpaper";
            if (source == "Windows")
                s.AccentColor = null;
            else if (source == "Custom" && s.AccentColor is null)
                s.AccentColor = $"#{shown.R:X2}{shown.G:X2}{shown.B:X2}";
        });
        UpdateSwatches();
    }

    private void UpdateWallpaper()
    {
        var path = Wallpaper.Current;
        WallpaperPath.Text = string.IsNullOrEmpty(path) ? Loc.T("Color sólido") : path;
        WallpaperPreview.Source = LoadThumbnail(path);
        RestoreWallpaperButton.Visibility = Settings.Current.OriginalWallpaper is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static BitmapImage? LoadThumbnail(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 320;
            image.CacheOption = BitmapCacheOption.OnLoad; // no deja el archivo bloqueado
            image.EndInit();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return null;
        }
    }

    private void ChooseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Elegir fondo de pantalla"),
            Filter = Loc.T("Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Todos los archivos|*.*"),
        };
        if (dialog.ShowDialog() != true)
            return;

        Settings.Current.OriginalWallpaper ??= Wallpaper.Current;
        Settings.Save();
        TrySetWallpaper(dialog.FileName);
    }

    private void RestoreWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (Settings.Current.OriginalWallpaper is not { } original)
            return;
        if (TrySetWallpaper(original))
        {
            Settings.Current.OriginalWallpaper = null;
            Settings.Save();
            UpdateWallpaper();
        }
    }

    private bool TrySetWallpaper(string path)
    {
        try
        {
            Wallpaper.Set(path);
            UpdateWallpaper();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el fondo", ex);
            System.Windows.MessageBox.Show(Loc.F("No se pudo cambiar el fondo:\n{0}", ex.Message), "WINX");
            return false;
        }
    }
}
