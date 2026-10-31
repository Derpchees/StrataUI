using System.Windows;
using Strata.Shell.Services;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;

namespace Strata.Shell.Files;

/// <summary>
/// El visor en su propia ventana (desde el escritorio, o al sacarlo de Archivos). Una sola ventana:
/// abrir otro archivo la reutiliza.
/// </summary>
public partial class MediaViewerWindow : FluentWindow
{
    private static MediaViewerWindow? _current;

    /// <summary>Muestra <paramref name="items"/>[<paramref name="index"/>]; las flechas recorren la lista.</summary>
    public static void Open(IReadOnlyList<string> items, int index)
    {
        if (items.Count == 0)
            return;
        var viewer = _current ??= new MediaViewerWindow();
        viewer.Show();
        if (viewer.WindowState == WindowState.Minimized)
            viewer.WindowState = WindowState.Normal;
        viewer.Topmost = true;
        viewer.Activate();
        viewer.Topmost = false;
        viewer.Viewer.Open(items, index);
    }

    private MediaViewerWindow()
    {
        InitializeComponent();
        FluentBackdrop.Follow(this);
        Viewer.TitleChanged += name => Title = ViewerTitleBar.Title = name;
        Viewer.CloseRequested += Close;
        Closed += (_, _) =>
        {
            Viewer.Release();
            _current = null;
        };
        Loaded += (_, _) => Motion.In(Root, Root, TaskbarPosition.Bottom, 0.8);
    }
}
