using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Winx.Shell.Services;

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

    private readonly bool _loading = true;

    public AppearancePage()
    {
        InitializeComponent();

        var s = Settings.Current;
        ThemeBox.SelectedIndex = (int)s.Theme;
        WindowsAccentSwitch.IsChecked = s.AccentColor is null;
        BuildSwatches();
        UpdateSwatches();
        UpdateWallpaper();

        _loading = false;
    }

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
            swatch.Click += (_, _) => Settings.Update(s => s.AccentColor = hex);
            swatch.Click += (_, _) => UpdateSwatches();
            Swatches.Children.Add(swatch);
        }
    }

    private void UpdateSwatches()
    {
        var current = Settings.Current.AccentColor;
        SwatchPanel.IsEnabled = current != null;
        SwatchPanel.Opacity = current != null ? 1 : 0.4;
        foreach (Button swatch in Swatches.Children)
        {
            bool selected = string.Equals((string)swatch.Tag, current, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(selected ? 3 : 0);
            swatch.BorderBrush = (Brush)FindResource("TextFillColorPrimaryBrush");
        }
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            Settings.Update(s => s.Theme = (WinxTheme)ThemeBox.SelectedIndex);
    }

    private void WindowsAccent_Click(object sender, RoutedEventArgs e)
    {
        bool useWindows = WindowsAccentSwitch.IsChecked == true;
        // Al pasar a color propio se parte del acento actual de Windows.
        var windowsAccent = ThemeManager.GetWindowsAccentColor();
        Settings.Update(s => s.AccentColor = useWindows
            ? null
            : $"#{windowsAccent.R:X2}{windowsAccent.G:X2}{windowsAccent.B:X2}");
        UpdateSwatches();
    }

    private void UpdateWallpaper()
    {
        var path = Wallpaper.Current;
        WallpaperPath.Text = string.IsNullOrEmpty(path) ? "Color sólido" : path;
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
            Title = "Elegir fondo de pantalla",
            Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Todos los archivos|*.*",
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
            System.Windows.MessageBox.Show($"No se pudo cambiar el fondo:\n{ex.Message}", "WINX");
            return false;
        }
    }
}
