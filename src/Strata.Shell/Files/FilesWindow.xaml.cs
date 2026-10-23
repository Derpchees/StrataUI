using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;

namespace Strata.Shell.Files;

/// <summary>
/// Explorador de archivos de Strata UI: barra lateral (Este equipo, carpetas, nube, unidades y red),
/// pestañas en la barra de título y, en cada pestaña, una carpeta o dos lado a lado (vista doble).
/// Los comandos de arriba actúan sobre el panel activo (<see cref="FolderPane"/>).
/// Se abre con <c>StrataUI.exe --files [ruta]</c>, Win+E, desde el pie del menú Inicio o la bandeja.
/// </summary>
public partial class FilesWindow : FluentWindow
{
    /// <summary>Una pestaña: su panel y, en la vista doble, el de la derecha.</summary>
    private sealed class FilesTab : INotifyPropertyChanged
    {
        public FilesTab(FolderPane left)
        {
            Left = left;
            Active = left;
            Host = new Grid();
            Host.Children.Add(left);
        }

        public FolderPane Left { get; }
        public FolderPane? Right { get; set; }
        public FolderPane Active { get; set; }
        public Grid Host { get; }
        public IEnumerable<FolderPane> Panes => Right is null ? [Left] : [Left, Right];

        public string Title => Right is null ? Left.Title : $"{Left.Title} | {Right.Title}";
        public string Path => Active.CurrentPath ?? Active.Title;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                Changed();
            }
        }

        public void TitleChanged()
        {
            Changed(nameof(Title));
            Changed(nameof(Path));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Changed([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly ObservableCollection<FilesTab> _tabs = [];
    private FilesTab? _tab;
    private readonly Place _home;
    private readonly List<Place> _folders;
    private readonly Place _recycleBin;
    private List<Place> _pinned = [], _drives = [], _cloud = [], _network = [];
    private DateTime _drivesRead;

    public FilesWindow(string? path = null)
    {
        InitializeComponent();
        FluentBackdrop.Follow(this);
        SourceInitialized += (_, _) => Controls.FilesIcon.ApplyAppId(new WindowInteropHelper(this).Handle);
        _home = new Place(Loc.T("Este equipo"), null, SymbolRegular.Home24);
        _folders = Places.Folders();
        _recycleBin = new Place(Loc.T("Papelera de reciclaje"), Places.RecycleBin, SymbolRegular.Delete24);
        SideFolders.ItemsSource = new[] { _home }.Concat(_folders).Append(_recycleBin).ToList();
        UpdatePinned();
        TabStrip.ItemsSource = _tabs;

        HiddenToggle.IsChecked = Settings.Current.FilesShowHidden;
        FileEntry.ShowExtensions = Settings.Current.FilesShowExtensions;
        ExtensionsToggle.IsChecked = FileEntry.ShowExtensions;
        ApplyViewToggles();

        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseDown += (_, e) =>
        {
            // Botones laterales del mouse: atrás y adelante.
            if (e.ChangedButton == MouseButton.XButton1)
                Pane.GoBack();
            else if (e.ChangedButton == MouseButton.XButton2)
                Pane.GoForward();
        };
        Activated += (_, _) =>
        {
            // La Papelera no avisa cambios: se vuelve a leer al volver a la ventana.
            if (_tab != null && Pane.InRecycleBin)
                Pane.Reload();
            UpdateCommands();
            if (DateTime.UtcNow - _drivesRead > TimeSpan.FromSeconds(10))
                _ = LoadPlacesAsync();
        };
        Closed += (_, _) =>
        {
            foreach (var pane in _tabs.SelectMany(t => t.Panes))
                pane.Close();
            MemoryTrim.Schedule(TimeSpan.FromSeconds(3));
        };
        Loaded += (_, _) => Motion.In(Root, Root, TaskbarPosition.Bottom, 0.8);
        AddTab(path);
        _ = LoadPlacesAsync();
    }

    /// <summary>El panel activo de la pestaña elegida (al que van los comandos).</summary>
    private FolderPane Pane => _tab!.Active;

    // ── Pestañas ────────────────────────────────────────────────────────────

    /// <summary>Abre una pestaña en esa carpeta (null: Este equipo) y la elige.</summary>
    public void AddTab(string? path)
    {
        var tab = new FilesTab(CreatePane());
        _tabs.Add(tab);
        SelectTab(tab);
        tab.Left.Navigate(path);
    }

    private FolderPane CreatePane()
    {
        var pane = new FolderPane();
        if (_drivesRead != default)
            pane.SetPlaces(CurrentPlaces());
        pane.StateChanged += () => OnPaneChanged(pane);
        pane.Activated += () => ActivatePane(pane);
        pane.PlacesRefreshRequested += () =>
        {
            if (DateTime.UtcNow - _drivesRead > TimeSpan.FromSeconds(2))
                _ = LoadPlacesAsync();
        };
        pane.OpenInNewTabRequested += AddTab;
        pane.PinsChanged += () =>
        {
            foreach (var window in Application.Current.Windows.OfType<FilesWindow>())
                window.UpdatePinned();
        };
        return pane;
    }

    private void SelectTab(FilesTab tab)
    {
        foreach (var other in _tabs)
            other.IsSelected = other == tab;
        _tab = tab;
        TabHost.Content = tab.Host;
        SplitToggle.IsChecked = tab.Right != null;
        UpdateCommands();
        Dispatcher.BeginInvoke(() => tab.Active.FocusList(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseTab(FilesTab tab)
    {
        foreach (var pane in tab.Panes)
            pane.Close();
        int index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (_tabs.Count == 0)
        {
            Close();
            return;
        }
        if (tab == _tab)
            SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
    }

    private void CycleTab(int step)
    {
        if (_tab is null || _tabs.Count < 2)
            return;
        int index = (_tabs.IndexOf(_tab) + step + _tabs.Count) % _tabs.Count;
        SelectTab(_tabs[index]);
    }

    // ── Vista doble ─────────────────────────────────────────────────────────

    /// <summary>Parte la pestaña en dos carpetas lado a lado (la nueva abre donde está la activa) o vuelve a una.</summary>
    private void SetSplit(bool split)
    {
        if (_tab is not { } tab || (tab.Right != null) == split)
            return;
        var host = tab.Host;
        if (split)
        {
            var right = CreatePane();
            tab.Right = right;
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
            var splitter = new GridSplitter
            {
                Width = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Cursor = Cursors.SizeWE,
            };
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(right, 2);
            host.Children.Add(splitter);
            host.Children.Add(right);
            right.Navigate(tab.Left.CurrentPath);
            ActivatePane(right);
        }
        else
        {
            tab.Right!.Close();
            host.Children.Clear();
            host.ColumnDefinitions.Clear();
            host.Children.Add(tab.Left);
            Grid.SetColumn(tab.Left, 0);
            tab.Right = null;
            ActivatePane(tab.Left);
        }
        SplitToggle.IsChecked = split;
        tab.TitleChanged();
    }

    private void ActivatePane(FolderPane pane)
    {
        if (_tab is not { } tab || !tab.Panes.Contains(pane))
            return;
        tab.Active = pane;
        // El borde de acento solo tiene sentido con dos paneles.
        foreach (var other in tab.Panes)
            other.IsActivePane = tab.Right != null && other == pane;
        tab.TitleChanged();
        UpdateCommands();
        UpdateCurrentPlace();
    }

    // ── Lo que se ve ────────────────────────────────────────────────────────

    private void OnPaneChanged(FolderPane pane)
    {
        var tab = _tabs.FirstOrDefault(t => t.Panes.Contains(pane));
        tab?.TitleChanged();
        if (tab == _tab && pane == _tab?.Active)
        {
            UpdateCommands();
            UpdateCurrentPlace();
        }
    }

    private void UpdateCommands()
    {
        if (_tab is null)
            return;
        var pane = Pane;
        Title = pane.Title;
        WindowTitleBar.Title = pane.Title;
        bool inFolder = pane.InFolder, canModify = pane.CanModify;
        int selected = pane.SelectedCount;
        NewFolderButton.IsEnabled = canModify;
        PasteButton.IsEnabled = canModify && FileOperations.ClipboardHasFiles();
        CutButton.IsEnabled = CopyButton.IsEnabled = canModify && selected > 0;
        DeleteButton.IsEnabled = selected > 0;
        RenameButton.IsEnabled = canModify && selected == 1;
        // En Este equipo no hay orden ni vista que cambiar.
        ViewControls.Visibility = inFolder ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Las carpetas ancladas, debajo de las del usuario.</summary>
    private void UpdatePinned()
    {
        _pinned = Places.Pinned();
        SidePinned.ItemsSource = _pinned;
        SidePinnedTitle.Visibility = _pinned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCurrentPlace();
    }

    private void UpdateCurrentPlace()
    {
        if (_tab is null)
            return;
        string? path = Pane.CurrentPath;
        foreach (var place in _folders.Append(_home).Concat(_pinned).Concat(_drives).Concat(_cloud).Concat(_network))
            place.IsCurrent = FolderPane.SamePath(place.Path, path);
    }

    private void ApplyViewToggles()
    {
        bool grid = Settings.Current.FilesGridView;
        DetailsToggle.IsChecked = !grid;
        GridToggle.IsChecked = grid;
    }

    private PlacesData CurrentPlaces() => new(_home, _folders, _drives, _cloud, _network);

    /// <summary>Unidades, nube y red (fuera de la interfaz) para la barra lateral y la portada de cada panel.</summary>
    private async Task LoadPlacesAsync()
    {
        _drivesRead = DateTime.UtcNow;
        var remembered = Settings.Current.FilesNetworkPlaces.ToList();
        StorageView storage;
        try
        {
            storage = await FileIcons.RunAsync(() => Places.Storage(remembered));
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: no se pudieron leer las unidades", ex);
            return;
        }
        (_drives, _cloud, _network) = (storage.Drives, storage.Cloud, storage.Network);
        SideDrives.ItemsSource = _drives;
        SideCloud.ItemsSource = _cloud;
        SideNetwork.ItemsSource = _network;
        SideCloudTitle.Visibility = _cloud.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var places = CurrentPlaces();
        foreach (var pane in _tabs.SelectMany(t => t.Panes))
            pane.SetPlaces(places);
        UpdateCurrentPlace();
    }

    // ── Opciones de todas las ventanas ──────────────────────────────────────

    private static IEnumerable<FolderPane> AllPanes =>
        Application.Current.Windows.OfType<FilesWindow>().SelectMany(w => w._tabs).SelectMany(t => t.Panes);

    /// <summary>Lista o cuadrícula, para todas las ventanas y pestañas.</summary>
    internal static void SetGridView(bool grid)
    {
        if (Settings.Current.FilesGridView != grid)
            Settings.Update(s => s.FilesGridView = grid);
        foreach (var window in Application.Current.Windows.OfType<FilesWindow>())
            window.ApplyViewToggles();
        foreach (var pane in AllPanes)
            pane.ViewChanged();
    }

    internal static void SetShowHidden(bool show)
    {
        Settings.Update(s => s.FilesShowHidden = show);
        foreach (var window in Application.Current.Windows.OfType<FilesWindow>())
            window.HiddenToggle.IsChecked = show;
        foreach (var pane in AllPanes)
            pane.Reload();
    }

    /// <summary>Muestra u oculta las extensiones en todas las ventanas abiertas de Archivos.</summary>
    internal static void SetShowExtensions(bool show)
    {
        FileEntry.ShowExtensions = show;
        Settings.Update(s => s.FilesShowExtensions = show);
        foreach (var window in Application.Current.Windows.OfType<FilesWindow>())
            window.ExtensionsToggle.IsChecked = show;
        foreach (var pane in AllPanes)
            pane.RefreshNames();
    }

    // ── Teclado ─────────────────────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool handled = true;
        switch (key)
        {
            case Key.T when mods == ModifierKeys.Control:
                AddTab(Pane.CurrentPath);
                break;
            case Key.W when mods == ModifierKeys.Control:
                CloseTab(_tab!);
                break;
            case Key.Tab when mods == ModifierKeys.Control:
                CycleTab(1);
                break;
            case Key.Tab when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                CycleTab(-1);
                break;
            case >= Key.D1 and <= Key.D9 when mods == ModifierKeys.Control:
                int index = key == Key.D9 ? _tabs.Count - 1 : key - Key.D1;
                if (index < _tabs.Count)
                    SelectTab(_tabs[index]);
                break;
            case Key.D when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                SetSplit(_tab?.Right is null);
                break;
            default:
                handled = Pane.HandleKey(e);
                break;
        }
        if (handled)
            e.Handled = true;
    }

    // ── Controles ───────────────────────────────────────────────────────────

    private void NewTab_Click(object sender, RoutedEventArgs e) => AddTab(null);

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FilesTab tab && tab != _tab)
            SelectTab(tab);
    }

    /// <summary>Clic con la rueda en una pestaña: se cierra, como en un navegador.</summary>
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && ((FrameworkElement)sender).DataContext is FilesTab tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FilesTab tab)
            CloseTab(tab);
        e.Handled = true;
    }

    private void Split_Click(object sender, RoutedEventArgs e) => SetSplit(SplitToggle.IsChecked == true);
    private void NewFolder_Click(object sender, RoutedEventArgs e) => Pane.NewFolder();
    private void Cut_Click(object sender, RoutedEventArgs e) => Pane.CopySelection(cut: true);
    private void Copy_Click(object sender, RoutedEventArgs e) => Pane.CopySelection(cut: false);
    private void Paste_Click(object sender, RoutedEventArgs e) => _ = Pane.PasteAsync();
    private void Rename_Click(object sender, RoutedEventArgs e) => Pane.RenameSelected();
    private void Delete_Click(object sender, RoutedEventArgs e) => _ = Pane.RecycleSelectionAsync();
    private void Sort_Click(object sender, RoutedEventArgs e) => Pane.ShowSortMenu(SortButton);
    private void DetailsView_Click(object sender, RoutedEventArgs e) => SetGridView(false);
    private void GridView_Click(object sender, RoutedEventArgs e) => SetGridView(true);
    private void Hidden_Click(object sender, RoutedEventArgs e) => SetShowHidden(HiddenToggle.IsChecked == true);
    private void Extensions_Click(object sender, RoutedEventArgs e) => SetShowExtensions(ExtensionsToggle.IsChecked == true);

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Place place)
            return;
        Pane.Navigate(place.Path);
    }

    /// <summary>Clic con la rueda en un lugar de la barra lateral: en una pestaña nueva.</summary>
    private void Place_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && ((FrameworkElement)sender).DataContext is Place place)
        {
            AddTab(place.Path);
            e.Handled = true;
        }
    }

    /// <summary>Clic derecho en un lugar: abrirlo en una pestaña nueva o, si es una carpeta de red recordada, quitarla.</summary>
    private void Place_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var button = (FrameworkElement)sender;
        if (button.DataContext is not Place place)
        {
            e.Handled = true;
            return;
        }
        var menu = button.ContextMenu!;
        menu.Items.Clear();
        if (place.Path == Places.RecycleBin)
        {
            menu.Items.Add(FolderPane.Item(Loc.T("Vaciar la Papelera"), SymbolRegular.DeleteDismiss24, () =>
            {
                FileOperations.EmptyRecycleBin(new WindowInteropHelper(this).Handle);
                foreach (var pane in _tabs.SelectMany(t => t.Panes).Where(p => p.InRecycleBin))
                    pane.Reload();
            }));
            return;
        }
        menu.Items.Add(FolderPane.Item(Loc.T("Abrir en una pestaña nueva"), SymbolRegular.TabAdd24, () => AddTab(place.Path)));
        if (place.CanUnpin)
        {
            menu.Items.Add(FolderPane.Item(Loc.T("Desanclar de la barra lateral"), SymbolRegular.PinOff24, () =>
            {
                Places.SetPinned(place.Path!, false);
                RefreshPinned();
            }));
        }
        if (place.CanForget)
        {
            menu.Items.Add(FolderPane.Item(Loc.T("Quitar de la lista"), SymbolRegular.Dismiss24, () =>
            {
                Settings.Update(s => s.FilesNetworkPlaces = s.FilesNetworkPlaces.Where(p => !FolderPane.SamePath(p, place.Path)).ToList());
                _ = LoadPlacesAsync();
            }));
        }
    }

    // ── Arrastrar y soltar en la barra lateral ──────────────────────────────

    /// <summary>Formato de una carpeta anclada que se arrastra para cambiarla de lugar.</summary>
    private const string PinnedFormat = "StrataUI.PinnedPlace";

    private Point? _placeDragStart;

    private void Place_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _placeDragStart = ((FrameworkElement)sender).DataContext is Place { CanUnpin: true } ? e.GetPosition(this) : null;

    /// <summary>Una carpeta anclada se arrastra para ordenarla entre las demás.</summary>
    private void Place_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_placeDragStart is not { } start || e.LeftButton != MouseButtonState.Pressed
            || ((FrameworkElement)sender).DataContext is not Place { CanUnpin: true, Path: { } path })
            return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _placeDragStart = null;
        ((UIElement)sender).ReleaseMouseCapture(); // si no, el botón queda presionado
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(PinnedFormat, path), DragDropEffects.Move);
    }

    /// <summary>Qué pasa al soltar sobre un lugar: ordenar las ancladas, mandar a la Papelera, o mover/copiar dentro.</summary>
    private static DragDropEffects PlaceDropEffect(DragEventArgs e, Place place)
    {
        if (e.Data.GetData(PinnedFormat) is string pinned)
            return place.CanUnpin && !FolderPane.SamePath(pinned, place.Path) ? DragDropEffects.Move : DragDropEffects.None;
        if (place.Path is not { } target || Places.IsServer(target) || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return DragDropEffects.None;
        if (target == Places.RecycleBin)
            return DragDropEffects.Move;
        return FolderPane.DropEffect(e, target, files);
    }

    private void Place_DragOver(object sender, DragEventArgs e)
    {
        var button = (FrameworkElement)sender;
        try
        {
            e.Effects = button.DataContext is Place place ? PlaceDropEffect(e, place) & e.AllowedEffects : DragDropEffects.None;
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: falló el arrastre", ex);
            e.Effects = DragDropEffects.None;
        }
        button.Tag = e.Effects != DragDropEffects.None ? "drop" : null;
        e.Handled = true;
    }

    private void Place_DragLeave(object sender, DragEventArgs e) => ((FrameworkElement)sender).Tag = null;

    private async void Place_Drop(object sender, DragEventArgs e)
    {
        var button = (FrameworkElement)sender;
        button.Tag = null;
        if (button.DataContext is not Place { Path: { } target } place)
            return;
        e.Handled = true;
        try
        {
            var effect = PlaceDropEffect(e, place) & e.AllowedEffects;
            if (effect == DragDropEffects.None)
                return;
            if (e.Data.GetData(PinnedFormat) is string pinned)
            {
                Places.MovePinned(pinned, target);
                RefreshPinned();
                return;
            }
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            var handle = new WindowInteropHelper(this).Handle;
            if (target == Places.RecycleBin)
                await FileOperations.RecycleAsync(files, handle);
            else if (effect == DragDropEffects.Move)
                await FileOperations.MoveAsync(files, target, handle);
            else if (effect == DragDropEffects.Copy)
                await FileOperations.CopyAsync(files, target, handle);
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: falló al soltar en la barra lateral", ex);
        }
    }

    /// <summary>Carpetas soltadas en la barra lateral (fuera de un lugar): se anclan.</summary>
    private static string[] FoldersToPin(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => System.IO.Directory.Exists(f) && !Places.IsPinned(f)).ToArray()
            : [];

    private void Sidebar_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = FoldersToPin(e).Length > 0 ? DragDropEffects.Link & e.AllowedEffects : DragDropEffects.None;
        e.Handled = true;
    }

    private void Sidebar_Drop(object sender, DragEventArgs e)
    {
        var folders = FoldersToPin(e);
        if (folders.Length == 0)
            return;
        e.Handled = true;
        foreach (var folder in folders)
            Places.SetPinned(folder, true);
        RefreshPinned();
    }

    private static void RefreshPinned()
    {
        foreach (var window in Application.Current.Windows.OfType<FilesWindow>())
            window.UpdatePinned();
    }

    /// <summary>"Conectar unidad de red" de Windows, en su propio proceso.</summary>
    private void ConnectDrive_Click(object sender, RoutedEventArgs e) =>
        FileOperations.ConnectNetworkDrive();
}
