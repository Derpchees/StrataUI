using System.IO;
using System.IO.Enumeration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Files;

/// <summary>
/// Archivos como cuadro Abrir, Guardar como o Elegir carpeta de otra app (lo intercepta
/// <c>DialogTakeover</c> en el shell): abajo, el nombre, el tipo y Aceptar/Cancelar. Abrir un
/// archivo lo elige en lugar de abrirlo; las carpetas se siguen recorriendo como siempre.
/// </summary>
public partial class FilesWindow
{
    private readonly FilePickRequest? _pick;
    private readonly TaskCompletionSource<FilePickResult?> _picked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _nameTyped;

    /// <summary>Abre una ventana de Archivos en modo cuadro y espera lo que se elija (null: cancelado).</summary>
    internal static Task<FilePickResult?> PickAsync(FilePickRequest request)
    {
        string? folder = Directory.Exists(request.Folder ?? "") ? request.Folder
                       : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var window = new FilesWindow(folder, request);
        if (request.Owner != IntPtr.Zero)
            new WindowInteropHelper(window).Owner = request.Owner;
        window.WindowStartupLocation = request.Owner != IntPtr.Zero ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        window.Show();
        // Topmost momentáneo: queda encima aunque Windows niegue el foco a Strata UI.
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        window.PickName.Focus();
        return window._picked.Task;
    }

    private void SetUpPicker()
    {
        var pick = _pick!;
        string title = pick.Title is { Length: > 0 } t ? t : pick.Mode switch
        {
            FilePickMode.Save => Loc.T("Guardar como"),
            FilePickMode.Folder => Loc.T("Elegir carpeta"),
            _ => Loc.T("Abrir"),
        };
        Title = title;
        WindowTitleBar.Title = title;
        PickerBar.Visibility = Visibility.Visible;
        PickOk.Content = pick.Mode switch
        {
            FilePickMode.Save => Loc.T("Guardar"),
            FilePickMode.Folder => Loc.T("Elegir carpeta"),
            _ => Loc.T("Abrir"),
        };
        PickNameLabel.Text = pick.Mode == FilePickMode.Folder ? Loc.T("Carpeta:") : Loc.T("Nombre:");
        PickName.Text = pick.FileName ?? "";
        _nameTyped = !string.IsNullOrEmpty(pick.FileName);

        if (pick.Mode == FilePickMode.Folder || pick.Filters.Count == 0)
            PickType.Visibility = Visibility.Collapsed;
        else
        {
            foreach (var filter in pick.Filters)
                PickType.Items.Add(new ComboBoxItem { Content = filter.Name, Tag = filter });
            PickType.SelectedIndex = Math.Clamp(pick.FilterIndex, 0, pick.Filters.Count - 1);
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                Close();
            }
        };
        Closed += (_, _) => _picked.TrySetResult(null);
    }

    private void WirePicker(FolderPane pane)
    {
        pane.TypeFilter = MatchesType;
        pane.Choosing = entries => Choose(pane, entries);
        // Elegir un archivo pone su nombre abajo (como el cuadro de Windows).
        pane.StateChanged += () =>
        {
            if (_pick!.Mode == FilePickMode.Folder || pane != Pane)
                return;
            var files = pane.SelectedEntries.Where(e => !e.IsFolder).ToList();
            if (files.Count == 0)
                return;
            PickName.Text = files.Count == 1 ? files[0].Name : string.Join(" ", files.Select(f => $"\"{f.Name}\""));
            _nameTyped = false;
        };
    }

    /// <summary>Los patrones del tipo elegido (*.png;*.jpg). Sin tipo, o con *.*, todo se ve.</summary>
    private bool MatchesType(FileEntry entry)
    {
        if (_pick!.Mode == FilePickMode.Folder)
            return false; // Elegir carpeta: solo carpetas, como en Windows
        if ((PickType.SelectedItem as ComboBoxItem)?.Tag is not FilePickFilter filter)
            return true;
        foreach (var pattern in filter.Patterns)
        {
            string p = pattern.Trim();
            if (p is "*" or "*.*" or "")
                return true;
            if (FileSystemName.MatchesSimpleExpression(p, entry.Name))
                return true;
        }
        return false;
    }

    private void PickType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tab is null)
            return;
        foreach (var pane in _tab.Panes)
            pane.RefreshFilter();
        // Guardar: la extensión del nombre sigue al tipo (nota.txt → nota.md), si el tipo tiene una sola.
        if (_pick?.Mode == FilePickMode.Save && (PickType.SelectedItem as ComboBoxItem)?.Tag is FilePickFilter filter
            && filter.Patterns.FirstOrDefault() is { } first && first.StartsWith("*.") && first.IndexOfAny(['*', '?'], 2) < 0
            && PickName.Text is { Length: > 0 } name && Path.HasExtension(name))
            PickName.Text = Path.ChangeExtension(name, first[2..]);
    }

    private void PickName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Accept();
        }
        else
            _nameTyped = true;
    }

    private void PickOk_Click(object sender, RoutedEventArgs e) => Accept();

    private void PickCancel_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Doble clic o Entrar sobre archivos de la lista.</summary>
    private void Choose(FolderPane pane, IReadOnlyList<FileEntry> entries)
    {
        if (_pick!.Mode == FilePickMode.Folder)
            return; // en Elegir carpeta, los archivos no cuentan
        var files = entries.Where(e => !e.IsFolder).ToList();
        if (files.Count == 0)
            return;
        if (pane.InArchive || files.Any(f => f.ArchivePath != null))
        {
            pane.Warn(Loc.T("Saca el archivo del comprimido para elegirlo."));
            return;
        }
        if (_pick.Mode == FilePickMode.Save)
        {
            PickName.Text = files[0].Name;
            Accept();
            return;
        }
        Finish(_pick.Multiple ? files.Select(f => f.FullPath).ToList() : [files[0].FullPath]);
    }

    /// <summary>Aceptar: lo elegido en la lista o lo escrito abajo (nombre o ruta completa).</summary>
    private void Accept()
    {
        var pick = _pick!;
        var pane = Pane;
        string? folder = pane.InFolder && !pane.InRecycleBin && !pane.InArchive ? pane.CurrentPath : null;
        string typed = PickName.Text.Trim();

        if (pick.Mode == FilePickMode.Folder)
        {
            if (typed.Length > 0 && Resolve(folder, typed) is { } path && Directory.Exists(path))
            {
                Finish([path]);
                return;
            }
            var chosen = pane.SelectedEntries.Where(e => e.IsFolder && e.ArchivePath is null).Select(e => e.FullPath).ToList();
            if (chosen.Count > 0)
                Finish(pick.Multiple ? chosen : [chosen[0]]);
            else if (folder != null)
                Finish([folder]);
            else
                pane.Warn(Loc.T("Entra en una carpeta o elige una."));
            return;
        }

        // Lo seleccionado en la lista, si el nombre no se escribió a mano.
        if (pick.Mode == FilePickMode.Open && !_nameTyped)
        {
            var selected = pane.SelectedEntries;
            if (selected.FirstOrDefault(e => e.IsFolder) is { } dir && selected.Count == 1)
            {
                pane.Navigate(dir.FullPath);
                return;
            }
            var files = selected.Where(e => !e.IsFolder).ToList();
            if (files.Count > 0)
            {
                Choose(pane, files);
                return;
            }
        }

        if (typed.Length == 0)
        {
            pane.Warn(pick.Mode == FilePickMode.Save ? Loc.T("Escribe un nombre para el archivo.") : Loc.T("Elige un archivo."));
            return;
        }

        // Varios nombres entre comillas ("a.txt" "b.txt"), como en el cuadro de Windows.
        var names = typed.Contains('"')
            ? typed.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(n => n.Length > 0).ToList()
            : [typed];
        if (names.Count == 1 && Resolve(folder, names[0]) is { } single && Directory.Exists(single))
        {
            // Una carpeta escrita: se entra en ella.
            pane.Navigate(single);
            PickName.Text = "";
            return;
        }
        var paths = names.Select(n => Resolve(folder, n)).ToList();
        if (paths.Any(p => p is null))
        {
            pane.Warn(Loc.T("Entra en una carpeta para guardar ahí."));
            return;
        }
        if (pick.Mode == FilePickMode.Open)
        {
            if (paths.FirstOrDefault(p => !File.Exists(p)) is { } missing)
            {
                pane.Warn(Loc.F("No existe {0}", Path.GetFileName(missing!)));
                return;
            }
            Finish(pick.Multiple ? paths.Select(p => p!).ToList() : [paths[0]!]);
            return;
        }
        // Guardar: la app agrega la extensión y pregunta si se reemplaza.
        Finish([paths[0]!]);
    }

    /// <summary>Un nombre relativo a la carpeta abierta o una ruta completa; null si no hay carpeta.</summary>
    private static string? Resolve(string? folder, string name)
    {
        name = Environment.ExpandEnvironmentVariables(name);
        if (Path.IsPathFullyQualified(name))
            return name;
        if (folder is null)
            return null;
        try { return Path.GetFullPath(Path.Combine(folder, name)); }
        catch (Exception) { return null; }
    }

    private void Finish(IReadOnlyList<string> paths)
    {
        // El cuadro de Windows rechaza un archivo que otro programa tiene bloqueado (con su propio mensaje).
        if (_pick!.Mode == FilePickMode.Open && paths.FirstOrDefault(InUse) is { } busy)
        {
            Pane.Warn(Loc.F("{0} está en uso por otro programa. Ciérralo o elige otro archivo.", Path.GetFileName(busy)));
            return;
        }
        Log.Info($"Cuadro de archivos: {paths.Count} elegido(s)");
        _picked.TrySetResult(new FilePickResult(paths, Math.Max(0, PickType.SelectedIndex)));
        Close();
    }

    private static bool InUse(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33) // ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION
        {
            return true;
        }
        catch (Exception)
        {
            return false; // sin permiso u otro problema: que lo decida la app
        }
    }
}
