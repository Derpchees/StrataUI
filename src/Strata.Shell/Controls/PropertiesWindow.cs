using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Windows.ApplicationModel.DataTransfer;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Strata.Shell.Controls;

/// <summary>
/// Propiedades de archivos y carpetas con el tema de Strata UI (Archivos y el escritorio), en pestañas:
/// General (tipo, con qué se abre, tamaño, fechas, atributos), Compartir (con aplicaciones y personas
/// o en la red), Detalles (lo que sabe Windows del archivo: autor, dimensiones, duración…) y Seguridad
/// (propietario y permisos). Lo que no hace (editar permisos, versiones anteriores, uso compartido
/// avanzado) abre las Propiedades de Windows, desde el proceso aparte del menú de Windows.
/// </summary>
public sealed class PropertiesWindow : FluentWindow
{
    private readonly IReadOnlyList<string> _paths;
    private readonly bool _single;
    private readonly bool _folder;
    private readonly bool _root;
    private readonly CancellationTokenSource _closing = new();
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, FrameworkElement> _built = [];
    private CheckBox? _readOnly, _hidden;
    private bool? _readOnlyStart, _hiddenStart;
    private readonly Button _apply;
    private DataTransferManager? _share;

    /// <summary>Abre las propiedades de esos elementos (los que ya no existen se ignoran).</summary>
    public static void Open(IReadOnlyList<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (existing.Count == 0)
            return;
        var window = new PropertiesWindow(existing);
        window.Show();
        window.Activate();
    }

    private PropertiesWindow(IReadOnlyList<string> paths)
    {
        _paths = paths;
        _single = paths.Count == 1;
        _folder = _single && Directory.Exists(paths[0]);
        _root = _folder && Path.GetPathRoot(paths[0])?.TrimEnd('\\') is { } driveRoot
                && string.Equals(driveRoot, paths[0].TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        string name = _single ? DisplayName(paths[0]) : Loc.F("{0} elementos", paths.Count);
        Title = _single ? Loc.F("Propiedades de {0}", name) : Loc.T("Propiedades");
        Width = 480;
        Height = 620;
        MinWidth = 400;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        FluentBackdrop.Follow(this);

        // Encabezado: ícono, nombre y tipo (o cuántos elementos).
        var header = new Grid { Margin = new Thickness(24, 22, 56, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new System.Windows.Controls.Image
        {
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 0, 14, 0),
            Source = ThumbnailHost.Get(paths[0], 64, cachedOnly: false) ?? FileIcons.IconOfPath(paths[0], Directory.Exists(paths[0])),
        };
        header.Children.Add(icon);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Text(name, 18, primary: true, weight: FontWeights.SemiBold));
        titles.Children.Add(Text(_single ? TypeName(paths[0]) : CommonType(paths), 13));
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);

        // Pestañas como las del Hub.
        var bar = new WrapPanel { Margin = new Thickness(20, 8, 20, 0) };
        var tabs = new List<(string Key, SymbolRegular Icon, string Title, Func<FrameworkElement> Build)>
        {
            ("general", SymbolRegular.Info24, Loc.T("General"), BuildGeneral),
            ("share", SymbolRegular.Share24, Loc.T("Compartir"), BuildShare),
        };
        if (_single && !_folder)
            tabs.Add(("details", SymbolRegular.TextBulletListSquare24, Loc.T("Detalles"), BuildDetails));
        if (_single)
            tabs.Add(("security", SymbolRegular.Shield24, Loc.T("Seguridad"), BuildSecurity));
        string group = "PropertiesTabs" + GetHashCode();
        RadioButton? first = null;
        foreach (var tab in tabs)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("HubTab"),
                GroupName = group,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new SymbolIcon { Symbol = tab.Icon, FontSize = 16, Margin = new Thickness(0, 0, 8, 0) },
                        new TextBlock { Text = tab.Title, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            button.Checked += (_, _) => ShowTab(tab.Key, tab.Build);
            bar.Children.Add(button);
            first ??= button;
        }
        var divider = new Border { Height = 1, Margin = new Thickness(24, 8, 24, 0) };
        divider.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");

        var scroll = new ScrollViewer
        {
            Content = _page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(24, 16, 24, 16),
        };

        // Abajo: las Propiedades de Windows a la izquierda; Aceptar, Cancelar y Aplicar a la derecha.
        var windows = new Button
        {
            Content = Loc.T("Propiedades de Windows"),
            Icon = new SymbolIcon(SymbolRegular.WindowNew24),
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(10, 6, 10, 6),
        };
        windows.Click += (_, _) => OpenWindowsProperties();
        var ok = FooterButton(Loc.T("Aceptar"), ControlAppearance.Primary);
        ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            if (ApplyAttributes())
                Close();
        };
        var cancel = FooterButton(Loc.T("Cancelar"), ControlAppearance.Secondary);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        _apply = FooterButton(Loc.T("Aplicar"), ControlAppearance.Secondary);
        _apply.IsEnabled = false;
        _apply.Click += (_, _) => ApplyAttributes();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_apply);
        var footerGrid = new Grid { Margin = new Thickness(16, 12, 16, 14) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.Children.Add(windows);
        Grid.SetColumn(buttons, 1);
        footerGrid.Children.Add(buttons);
        var footer = new Border { Child = footerGrid };
        footer.SetResourceReference(Border.BackgroundProperty, "StrataSurfaceBrush");

        var close = new Button
        {
            Icon = new SymbolIcon(SymbolRegular.Dismiss16),
            Appearance = ControlAppearance.Transparent,
            BorderThickness = new Thickness(0),
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
        };
        close.Click += (_, _) => Close();
        System.Windows.Automation.AutomationProperties.SetName(close, Loc.T("Cerrar"));

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "StrataHubTintBrush");
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = height });
        root.Children.Add(header);
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);
        Grid.SetRow(divider, 2);
        root.Children.Add(divider);
        Grid.SetRow(scroll, 3);
        root.Children.Add(scroll);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        root.Children.Add(close);
        Content = root;

        // Sin barra de título: se arrastra desde el encabezado.
        header.Background = Brushes.Transparent;
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
        Loaded += (_, _) =>
        {
            DialogPlacement.Place(this);
            Motion.In(root, root, TaskbarPosition.Bottom, 0.5);
        };
        Closed += (_, _) =>
        {
            _closing.Cancel();
            if (_share != null)
                _share.DataRequested -= OnShareRequested;
        };
        if (first != null)
            first.IsChecked = true;
    }

    private void ShowTab(string key, Func<FrameworkElement> build)
    {
        if (!_built.TryGetValue(key, out var page))
        {
            try
            {
                page = build();
            }
            catch (Exception ex)
            {
                Log.Error($"Propiedades: no se pudo mostrar la pestaña {key}", ex);
                page = Text(Loc.T("No se pudo leer esta información."), 14);
            }
            _built[key] = page;
        }
        _page.Content = page;
        Motion.In(page, page, TaskbarPosition.Bottom, 0.4);
    }

    // ---------------- General ----------------

    private FrameworkElement BuildGeneral()
    {
        var panel = new StackPanel();
        string path = _paths[0];
        if (_single)
        {
            if (!_folder)
                AddOpensWith(panel, path);
            panel.Children.Add(Row(Loc.T("Ubicación"), Path.GetDirectoryName(path.TrimEnd('\\')) ?? path));
        }
        else
        {
            var parents = _paths.Select(p => Path.GetDirectoryName(p.TrimEnd('\\')) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            panel.Children.Add(Row(Loc.T("Ubicación"), parents.Count == 1 ? parents[0] : Loc.T("Varias carpetas")));
        }

        if (_root)
            AddDrive(panel, path);
        else
            AddSizes(panel);

        if (_single)
        {
            panel.Children.Add(Gap());
            FileSystemInfo info = _folder ? new DirectoryInfo(path) : new FileInfo(path);
            try
            {
                panel.Children.Add(Row(Loc.T("Creado"), FormatDate(info.CreationTime)));
                panel.Children.Add(Row(Loc.T("Modificado"), FormatDate(info.LastWriteTime)));
                panel.Children.Add(Row(Loc.T("Último acceso"), FormatDate(info.LastAccessTime)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (!_root)
        {
            panel.Children.Add(Gap());
            panel.Children.Add(Section(Loc.T("Atributos")));
            var attributes = _paths.Select(p =>
            {
                try
                {
                    return File.GetAttributes(p);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return (FileAttributes)0;
                }
            }).ToList();
            // Solo lectura en una carpeta no significa nada para Windows: se ofrece solo con archivos.
            if (_paths.All(File.Exists))
                _readOnly = AttributeBox(Loc.T("Solo lectura"), attributes, FileAttributes.ReadOnly, out _readOnlyStart);
            _hidden = AttributeBox(Loc.T("Oculto"), attributes, FileAttributes.Hidden, out _hiddenStart);
            if (_readOnly != null)
                panel.Children.Add(_readOnly);
            panel.Children.Add(_hidden);
        }
        return panel;
    }

    private CheckBox AttributeBox(string label, List<FileAttributes> attributes, FileAttributes flag, out bool? start)
    {
        int set = attributes.Count(a => a.HasFlag(flag));
        start = set == 0 ? false : set == attributes.Count ? true : null;
        var box = new CheckBox { Content = label, IsChecked = start, IsThreeState = start is null, Margin = new Thickness(0, 4, 0, 0) };
        box.Click += (_, _) =>
        {
            // Mezclado al abrir: el tercer estado solo sirve para dejarlo como estaba.
            _apply.IsEnabled = _readOnly?.IsChecked != _readOnlyStart || _hidden?.IsChecked != _hiddenStart;
        };
        return box;
    }

    private bool ApplyAttributes()
    {
        if (!_apply.IsEnabled)
            return true;
        var errors = new List<string>();
        foreach (string path in _paths)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                var updated = Toggle(Toggle(attributes, FileAttributes.ReadOnly, _readOnly, _readOnlyStart), FileAttributes.Hidden, _hidden, _hiddenStart);
                if (updated != attributes)
                    File.SetAttributes(path, updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add($"{Path.GetFileName(path.TrimEnd('\\'))}: {ex.Message}");
            }
        }
        _readOnlyStart = _readOnly?.IsChecked;
        _hiddenStart = _hidden?.IsChecked;
        _apply.IsEnabled = false;
        if (errors.Count == 0)
            return true;
        _ = StrataDialog.ShowAsync(this, Loc.T("No se pudieron cambiar los atributos"), string.Join("\n", errors.Take(8)), Loc.T("Aceptar"));
        return false;

        static FileAttributes Toggle(FileAttributes attributes, FileAttributes flag, CheckBox? box, bool? start) =>
            box?.IsChecked is bool value && box.IsChecked != start
                ? value ? attributes | flag : attributes & ~flag
                : attributes;
    }

    private void AddOpensWith(StackPanel panel, string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length == 0 || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return;
        var value = Text(AppFor(extension) ?? Loc.T("Ninguna aplicación"), 14, primary: true);
        value.VerticalAlignment = VerticalAlignment.Center;
        var change = new Button { Content = Loc.T("Cambiar…"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(10, 0, 0, 0) };
        change.Click += async (_, _) =>
        {
            await ChooseAppAsync(path);
            value.Text = AppFor(extension) ?? Loc.T("Ninguna aplicación");
        };
        var line = new DockPanel();
        DockPanel.SetDock(change, Dock.Right);
        line.Children.Add(change);
        line.Children.Add(value);
        panel.Children.Add(Row(Loc.T("Se abre con"), line));
    }

    /// <summary>El "Abrir con" de Windows, en su propio hilo (es modal y no debe frenar la barra de tareas).</summary>
    private Task ChooseAppAsync(string path)
    {
        var done = new TaskCompletionSource();
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        var thread = new Thread(() =>
        {
            try
            {
                var info = new OpenAsInfo { File = path, Class = null, Flags = OaifAllowRegistration | OaifRegisterExt };
                SHOpenWithDialog(hwnd, ref info);
            }
            catch (Exception ex)
            {
                Log.Error("Propiedades: falló Abrir con", ex);
            }
            done.TrySetResult();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return done.Task;
    }

    private void AddDrive(StackPanel panel, string path)
    {
        try
        {
            var drive = new DriveInfo(path);
            panel.Children.Add(Row(Loc.T("Sistema de archivos"), drive.DriveFormat));
            panel.Children.Add(Row(Loc.T("Capacidad"), SizeText(drive.TotalSize)));
            panel.Children.Add(Row(Loc.T("Usado"), SizeText(drive.TotalSize - drive.TotalFreeSpace)));
            panel.Children.Add(Row(Loc.T("Libre"), SizeText(drive.TotalFreeSpace)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private void AddSizes(StackPanel panel)
    {
        bool anyFolder = _paths.Any(Directory.Exists);
        string waiting = Loc.T("Calculando…");
        var size = Row(Loc.T("Tamaño"), waiting);
        var disk = Row(Loc.T("Tamaño en disco"), waiting);
        panel.Children.Add(size);
        panel.Children.Add(disk);
        FrameworkElement? contains = null;
        if (anyFolder)
            panel.Children.Add(contains = Row(Loc.T("Contiene"), waiting));
        var token = _closing.Token;
        _ = Task.Run(() => Measure(_paths, token, (total, onDisk, files, folders, finished) => Dispatcher.BeginInvoke(() =>
        {
            SetRowValue(size, SizeText(total) + (finished ? "" : "…"));
            SetRowValue(disk, SizeText(onDisk) + (finished ? "" : "…"));
            if (contains != null)
                SetRowValue(contains, Loc.F("{0} archivos, {1} carpetas", files.ToString("N0"), folders.ToString("N0")));
        })), token);
    }

    private delegate void MeasureReport(long size, long onDisk, long files, long folders, bool finished);

    /// <summary>Suma tamaños recorriendo las carpetas (sin seguir uniones ni vínculos), avisando cada tanto.</summary>
    private static void Measure(IReadOnlyList<string> paths, CancellationToken token, MeasureReport report)
    {
        long size = 0, disk = 0, files = 0, folders = 0;
        var clusters = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var pending = new Stack<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                if (paths.Count > 1)
                    folders++;
                pending.Push(path);
            }
            else
                AddFile(new FileInfo(path));
        }
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        while (pending.Count > 0 && !token.IsCancellationRequested)
        {
            string folder = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
                {
                    if (token.IsCancellationRequested)
                        return;
                    if (entry is DirectoryInfo directory)
                    {
                        folders++;
                        if (!directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            pending.Push(directory.FullName);
                    }
                    else if (entry is FileInfo file)
                        AddFile(file);
                    if (clock.ElapsedMilliseconds > 250)
                    {
                        clock.Restart();
                        report(size, disk, files, folders, false);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }
        if (!token.IsCancellationRequested)
            report(size, disk, files, folders, true);

        void AddFile(FileInfo file)
        {
            try
            {
                long length = file.Length;
                files++;
                size += length;
                var attributes = file.Attributes;
                // En la nube sin descargar (OneDrive): no ocupa espacio en el disco.
                if (((int)attributes & (RecallOnDataAccess | RecallOnOpen)) != 0 || attributes.HasFlag(FileAttributes.Offline))
                    return;
                if (attributes.HasFlag(FileAttributes.Compressed) || attributes.HasFlag(FileAttributes.SparseFile))
                {
                    uint low = GetCompressedFileSizeW(file.FullName, out uint high);
                    if (low != uint.MaxValue || Marshal.GetLastWin32Error() == 0)
                        length = ((long)high << 32) | low;
                }
                long cluster = Cluster(Path.GetPathRoot(file.FullName) ?? "");
                disk += cluster > 0 ? (length + cluster - 1) / cluster * cluster : length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        long Cluster(string root)
        {
            if (!clusters.TryGetValue(root, out long cluster))
            {
                cluster = GetDiskFreeSpaceW(root, out uint sectors, out uint bytes, out _, out _) ? (long)sectors * bytes : 0;
                clusters[root] = cluster;
            }
            return cluster;
        }
    }

    // ---------------- Compartir ----------------

    private FrameworkElement BuildShare()
    {
        var panel = new StackPanel();
        bool files = _paths.All(File.Exists);
        panel.Children.Add(Section(Loc.T("Compartir con aplicaciones y personas")));
        if (files)
        {
            panel.Children.Add(Text(Loc.T("Envíalo por correo, a un dispositivo cercano o a una aplicación, con la ventana Compartir de Windows."), 13));
            var share = new Button
            {
                Content = Loc.T("Compartir…"),
                Icon = new SymbolIcon(SymbolRegular.Share24),
                Appearance = ControlAppearance.Primary,
                Margin = new Thickness(0, 10, 0, 0),
            };
            share.Click += (_, _) => ShareWithWindows();
            panel.Children.Add(share);
        }
        else
            panel.Children.Add(Text(Loc.T("La ventana Compartir de Windows solo envía archivos, no carpetas."), 13));

        panel.Children.Add(Gap());
        panel.Children.Add(Section(Loc.T("Compartir en la red")));
        if (_folder && !ElevatedFiles.Protected(_paths[0]))
        {
            var network = new StackPanel();
            panel.Children.Add(network);
            FillNetwork(network);
        }
        else
            panel.Children.Add(Text(_single && !_folder ? Loc.T("En la red se comparten carpetas: comparte la carpeta que lo contiene.")
                                    : _folder ? Loc.T("Esta carpeta no se puede compartir desde aquí; usa el uso compartido avanzado.")
                                    : Loc.T("Elige una sola carpeta para compartirla en la red."), 13));

        panel.Children.Add(Gap());
        panel.Children.Add(Section(Loc.T("Uso compartido avanzado")));
        panel.Children.Add(Text(Loc.T("Permisos por usuario, límite de conexiones y archivos sin conexión, en las Propiedades de Windows."), 13));
        var advanced = new Button { Content = Loc.T("Uso compartido avanzado…"), Icon = new SymbolIcon(SymbolRegular.WindowNew24), Margin = new Thickness(0, 10, 0, 0) };
        advanced.Click += (_, _) => OpenWindowsProperties();
        panel.Children.Add(advanced);
        return panel;
    }

    private void FillNetwork(StackPanel network)
    {
        network.Children.Clear();
        string folder = _paths[0];
        var shares = NetShares.For(folder);
        if (shares.Count == 0)
            network.Children.Add(Text(Loc.T("Esta carpeta no está compartida en la red."), 13));
        foreach (var share in shares)
        {
            string unc = NetShares.UncPath(share.Name);
            var card = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10, 10, 10), Margin = new Thickness(0, 6, 0, 0) };
            card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(Text(unc, 14, primary: true, weight: FontWeights.SemiBold));
            texts.Children.Add(Text(share.ReadOnly ? Loc.T("Todos: lectura") : Loc.T("Todos: lectura y escritura"), 12));
            grid.Children.Add(texts);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var copy = new Button { Icon = new SymbolIcon(SymbolRegular.Copy24), Appearance = ControlAppearance.Transparent, Padding = new Thickness(6), ToolTip = Loc.T("Copiar ruta de red") };
            copy.Click += (_, _) => CopyText(unc);
            var stop = new Button { Content = Loc.T("Dejar de compartir"), Icon = new SymbolIcon(SymbolRegular.Shield24), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            stop.Click += async (_, _) =>
            {
                if (await RunShareAsync(new ElevatedFiles.Job { Op = "unshare", Paths = [folder], NewName = share.Name },
                                        Loc.F("Dejar de compartir {0} en la red.", unc), Loc.T("No se pudo dejar de compartir")))
                    FillNetwork(network);
            };
            actions.Children.Add(copy);
            actions.Children.Add(stop);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            card.Child = grid;
            network.Children.Add(card);
        }

        // Compartir (o agregar otro nombre): nombre y permiso.
        var form = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        form.Children.Add(Text(shares.Count == 0 ? Loc.T("Compartir esta carpeta") : Loc.T("Compartir con otro nombre"), 14, primary: true, weight: FontWeights.SemiBold));
        var nameBox = new TextBox { Text = NetShares.SuggestName(folder), MaxLength = 80, Margin = new Thickness(0, 8, 0, 0) };
        var permission = new ComboBox { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        permission.Items.Add(Loc.T("Todos pueden ver y copiar (lectura)"));
        permission.Items.Add(Loc.T("Todos pueden cambiar y borrar (lectura y escritura)"));
        permission.SelectedIndex = 0;
        var problem = Text("", 12);
        problem.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        problem.Visibility = Visibility.Collapsed;
        var path = Text(Loc.F("Ruta de red: {0}", NetShares.UncPath(nameBox.Text)), 12);
        nameBox.TextChanged += (_, _) =>
        {
            path.Text = Loc.F("Ruta de red: {0}", NetShares.UncPath(nameBox.Text.Trim()));
            problem.Visibility = Visibility.Collapsed;
        };
        var add = new Button
        {
            Content = Loc.T("Compartir"),
            Icon = new SymbolIcon(SymbolRegular.Shield24),
            Appearance = ControlAppearance.Primary,
            Margin = new Thickness(0, 10, 0, 0),
        };
        add.Click += async (_, _) =>
        {
            string name = nameBox.Text.Trim();
            if (!NetShares.ValidName(name) || shares.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                problem.Text = Loc.T("Ese nombre no es válido: hasta 80 caracteres, sin \\ / [ ] : | < > + ; , ? * = ni $ al final.");
                problem.Visibility = Visibility.Visible;
                return;
            }
            string unc = NetShares.UncPath(name);
            if (await RunShareAsync(new ElevatedFiles.Job { Op = "share", Paths = [folder], NewName = name, Label = permission.SelectedIndex == 1 ? "change" : "read" },
                                    Loc.F("Compartir esta carpeta en la red como {0}.", unc), Loc.T("No se pudo compartir")))
                FillNetwork(network);
        };
        form.Children.Add(Field(Loc.T("Nombre del recurso"), nameBox));
        form.Children.Add(path);
        form.Children.Add(Field(Loc.T("Permiso"), permission));
        form.Children.Add(problem);
        form.Children.Add(add);
        var note = Text(Loc.T("Quien se conecte necesita además permiso en la pestaña Seguridad, y la detección de redes y el uso compartido de archivos deben estar activados."), 12);
        note.Margin = new Thickness(0, 10, 0, 0);
        form.Children.Add(note);
        network.Children.Add(form);
    }

    private async Task<bool> RunShareAsync(ElevatedFiles.Job job, string question, string failed)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (!await ElevatedFiles.AskAsync(hwnd, question))
            return false;
        string? error = await ElevatedFiles.RunAsync(job);
        if (error is null)
            return true;
        if (error.Length > 0)
            await StrataDialog.ShowAsync(this, failed, error, Loc.T("Aceptar"));
        return false;
    }

    /// <summary>La ventana Compartir de Windows (correo, Nearby Share, apps) con los archivos.</summary>
    private void ShareWithWindows()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        try
        {
            if (_share is null)
            {
                _share = DataTransferManagerInterop.GetForWindow(hwnd);
                _share.DataRequested += OnShareRequested;
            }
            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception ex)
        {
            Log.Error("Propiedades: no se pudo abrir Compartir de Windows", ex);
        }
    }

    private async void OnShareRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        try
        {
            var items = new List<Windows.Storage.IStorageItem>();
            foreach (string path in _paths)
                items.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
            args.Request.Data.Properties.Title = _single ? Path.GetFileName(_paths[0]) : Loc.F("{0} elementos", _paths.Count);
            args.Request.Data.SetStorageItems(items);
        }
        catch (Exception ex)
        {
            Log.Error("Propiedades: no se pudieron preparar los archivos para compartir", ex);
            args.Request.FailWithDisplayText(Loc.T("No se pudieron compartir los archivos."));
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ---------------- Detalles ----------------

    private FrameworkElement BuildDetails()
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(Loc.T("Cargando…"), 13));
        string path = _paths[0];
        var token = _closing.Token;
        _ = Task.Run(() => ReadDetails(path), token).ContinueWith(task =>
        {
            panel.Children.Clear();
            var details = task.IsCompletedSuccessfully ? task.Result : [];
            if (details.Count == 0)
                panel.Children.Add(Text(Loc.T("Windows no tiene más detalles de este archivo."), 13));
            foreach (var (name, value) in details)
                panel.Children.Add(Row(name, value));
        }, token, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        return panel;
    }

    /// <summary>Lo que dice el sistema de propiedades de Windows (autor, dimensiones, duración, cámara…).</summary>
    private static List<(string Name, string Value)> ReadDetails(string path)
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        IPropertyStore? store = null;
        IntPtr value = Marshal.AllocCoTaskMem(32);
        try
        {
            var storeId = typeof(IPropertyStore).GUID;
            SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, GpsBestEffort, ref storeId, out store);
            store.GetCount(out uint count);
            var descriptionId = typeof(IPropertyDescription).GUID;
            for (uint i = 0; i < count && result.Count < 80; i++)
            {
                IPropertyDescription? description = null;
                try
                {
                    store.GetAt(i, out var key);
                    if (PSGetPropertyDescription(ref key, ref descriptionId, out description) != 0 || description is null)
                        continue;
                    if (description.GetCanonicalName(out IntPtr canonicalPointer) != 0)
                        continue;
                    string canonical = Marshal.PtrToStringUni(canonicalPointer) ?? "";
                    Marshal.FreeCoTaskMem(canonicalPointer);
                    if (Hidden.Contains(canonical) || description.GetTypeFlags(PdtfIsViewable, out uint flags) != 0 || (flags & PdtfIsViewable) == 0)
                        continue;
                    if (description.GetDisplayName(out IntPtr labelPointer) != 0 || labelPointer == IntPtr.Zero)
                        continue;
                    string label = Marshal.PtrToStringUni(labelPointer) ?? "";
                    Marshal.FreeCoTaskMem(labelPointer);
                    for (int offset = 0; offset < 32; offset += 8)
                        Marshal.WriteInt64(value, offset, 0);
                    if (store.GetValue(ref key, value) != 0 || Marshal.ReadInt16(value) == 0)
                        continue;
                    try
                    {
                        if (PSFormatForDisplayAlloc(ref key, value, 0, out IntPtr textPointer) == 0 && textPointer != IntPtr.Zero)
                        {
                            string text = Marshal.PtrToStringUni(textPointer) ?? "";
                            Marshal.FreeCoTaskMem(textPointer);
                            // Quita las marcas de dirección que Windows mete en las fechas.
                            text = text.Replace("‎", "").Replace("‏", "").Trim();
                            if (label.Length > 0 && text.Length > 0 && seen.Add(label))
                                result.Add((label, text));
                        }
                    }
                    finally
                    {
                        PropVariantClear(value);
                    }
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
                {
                }
                finally
                {
                    if (description != null)
                        Marshal.ReleaseComObject(description);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FileNotFoundException or UnauthorizedAccessException)
        {
            Log.Info($"Propiedades: sin detalles de {path} ({ex.Message})");
        }
        finally
        {
            if (store != null)
                Marshal.ReleaseComObject(store);
            Marshal.FreeCoTaskMem(value);
        }
        return result;
    }

    /// <summary>Lo que ya está en General (nombre, tipo, ruta, tamaño, fechas, atributos).</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.ItemNameDisplay", "System.ItemTypeText", "System.ItemType", "System.ItemFolderPathDisplay",
        "System.ItemFolderPathDisplayNarrow", "System.ItemPathDisplay", "System.ItemFolderNameDisplay", "System.ParsingName",
        "System.Size", "System.DateModified", "System.DateCreated", "System.DateAccessed", "System.FileAttributes",
        "System.ItemNameDisplayWithoutExtension", "System.FileName", "System.FileExtension", "System.SFGAOFlags",
        "System.Kind", "System.KindText", "System.PerceivedType", "System.ZoneIdentifier", "System.FileOwner",
    };

    // ---------------- Seguridad ----------------

    private FrameworkElement BuildSecurity()
    {
        var panel = new StackPanel();
        string path = _paths[0];
        FileSystemSecurity? security = null;
        try
        {
            security = _folder ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PrivilegeNotHeldException)
        {
            panel.Children.Add(Text(Loc.T("No tienes permiso para ver la seguridad de este elemento."), 13));
        }
        if (security != null)
        {
            panel.Children.Add(Row(Loc.T("Propietario"), Account(security.GetOwner(typeof(SecurityIdentifier)))));
            panel.Children.Add(Gap());
            panel.Children.Add(Section(Loc.T("Permisos")));
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>()
                .Where(r => !r.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
                .GroupBy(r => (r.IdentityReference.Value, r.AccessControlType))
                .OrderBy(g => g.Key.AccessControlType == AccessControlType.Allow ? 1 : 0);
            foreach (var rule in rules)
            {
                var rights = rule.Aggregate((FileSystemRights)0, (all, r) => all | r.FileSystemRights);
                bool inherited = rule.All(r => r.IsInherited);
                var card = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 4, 0, 0) };
                card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
                var texts = new StackPanel();
                texts.Children.Add(Text(Account(new SecurityIdentifier(rule.Key.Value)), 14, primary: true, weight: FontWeights.SemiBold));
                string kind = rule.Key.AccessControlType == AccessControlType.Allow ? Loc.T("Permitir") : Loc.T("Denegar");
                texts.Children.Add(Text($"{kind}: {RightsText(rights)}" + (inherited ? "  ·  " + Loc.T("heredado") : ""), 12));
                card.Child = texts;
                panel.Children.Add(card);
            }
        }
        panel.Children.Add(Gap());
        panel.Children.Add(Text(Loc.T("Para cambiar permisos, propietario o auditoría se abren las Propiedades de Windows."), 13));
        var edit = new Button { Content = Loc.T("Editar permisos…"), Icon = new SymbolIcon(SymbolRegular.WindowNew24), Margin = new Thickness(0, 10, 0, 0) };
        edit.Click += (_, _) => OpenWindowsProperties();
        panel.Children.Add(edit);
        return panel;
    }

    private static string Account(IdentityReference? identity)
    {
        if (identity is null)
            return "—";
        try
        {
            return identity.Translate(typeof(NTAccount)).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return identity.Value;
        }
    }

    private static string RightsText(FileSystemRights rights)
    {
        int value = (int)rights;
        // Los genéricos (los que ponen los instaladores) también cuentan.
        if ((value & GenericAll) != 0)
            return Loc.T("Control total");
        if ((value & GenericWrite) != 0)
            value |= (int)FileSystemRights.Write;
        if ((value & GenericRead) != 0)
            value |= (int)FileSystemRights.Read;
        if ((value & GenericExecute) != 0)
            value |= (int)FileSystemRights.ReadAndExecute & ~(int)FileSystemRights.Read | (int)FileSystemRights.ExecuteFile;
        var effective = (FileSystemRights)value;
        bool Has(FileSystemRights mask) => (effective & mask) == mask;
        if (Has(FileSystemRights.FullControl))
            return Loc.T("Control total");
        if (Has(FileSystemRights.Modify))
            return Loc.T("Modificar");
        var parts = new List<string>();
        if (Has(FileSystemRights.ReadAndExecute))
            parts.Add(Loc.T("Lectura y ejecución"));
        else if (Has(FileSystemRights.Read))
            parts.Add(Loc.T("Lectura"));
        if (Has(FileSystemRights.Write))
            parts.Add(Loc.T("Escritura"));
        return parts.Count > 0 ? string.Join(", ", parts) : Loc.T("Permisos especiales");
    }

    // ---------------- Propiedades de Windows ----------------

    /// <summary>
    /// Las Propiedades de Windows (permisos, versiones anteriores, uso compartido avanzado), desde el
    /// proceso del menú de Windows: sus extensiones no se cargan dentro de Strata UI.
    /// </summary>
    private async void OpenWindowsProperties()
    {
        var session = ShellMenuSession.Start(_paths, background: false);
        if (session is null)
            return;
        try
        {
            var item = Find(await session.Items);
            if (item != null)
                session.Invoke(item.Id);
            else
                FileOperations.ShowInExplorer(_paths[0], select: true);
        }
        finally
        {
            session.Dispose();
        }

        static NativeMenuItem? Find(IEnumerable<NativeMenuItem> items)
        {
            foreach (var item in items)
            {
                if (string.Equals(item.Verb, "properties", StringComparison.OrdinalIgnoreCase))
                    return item;
                if (item.Children is { } children && Find(children) is { } found)
                    return found;
            }
            return null;
        }
    }

    // ---------------- Piezas ----------------

    private static TextBlock Text(string text, double size, bool primary = false, FontWeight? weight = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
        block.SetResourceReference(TextBlock.ForegroundProperty, primary ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        return block;
    }

    private static TextBlock Section(string title)
    {
        var block = Text(title, 14, primary: true, weight: FontWeights.SemiBold);
        block.Margin = new Thickness(0, 0, 0, 6);
        return block;
    }

    private static FrameworkElement Gap() => new Border { Height = 18 };

    /// <summary>Una fila "etiqueta: valor" (el valor se puede marcar y copiar).</summary>
    private static Grid Row(string label, object value)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var name = Text(label, 13);
        name.Margin = new Thickness(0, 2, 12, 0);
        grid.Children.Add(name);
        UIElement content = value is string text ? Selectable(text) : (UIElement)value;
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private static void SetRowValue(FrameworkElement row, string text)
    {
        if (row is Grid grid && grid.Children.OfType<TextBox>().FirstOrDefault() is { } box)
            box.Text = text;
    }

    private static TextBox Selectable(string text)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Template = PlainTextBox(),
        };
        box.SetResourceReference(TextBox.ForegroundProperty, "TextFillColorPrimaryBrush");
        return box;
    }

    private static ControlTemplate? _plain;

    /// <summary>Un cuadro de texto sin marco (el estilo de WPF-UI le pone borde, fondo y botón de borrar).</summary>
    private static ControlTemplate PlainTextBox()
    {
        if (_plain != null)
            return _plain;
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        return _plain = new ControlTemplate(typeof(TextBox)) { VisualTree = host };
    }

    private static FrameworkElement Field(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(Text(label, 12));
        control.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(control);
        return panel;
    }

    private static Button FooterButton(string text, ControlAppearance appearance) => new()
    {
        Content = text,
        Appearance = appearance,
        MinWidth = 88,
        Margin = new Thickness(6, 0, 0, 0),
        Padding = new Thickness(12, 6, 12, 6),
    };

    private static void CopyText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Log.Error("Propiedades: no se pudo copiar", ex);
        }
    }

    private static string SizeText(long bytes) =>
        bytes < 1024 ? Loc.F("{0} bytes", bytes.ToString("N0")) : $"{FileEntry.FormatSize(bytes)} ({Loc.F("{0} bytes", bytes.ToString("N0"))})";

    private static string FormatDate(DateTime date) => $"{date:D}, {date:T}";

    private static string DisplayName(string path) =>
        Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;

    private static string TypeName(string path)
    {
        var info = new ShFileInfo();
        if (SHGetFileInfoW(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiTypeName) != IntPtr.Zero && info.TypeName.Length > 0)
            return info.TypeName;
        return Directory.Exists(path) ? Loc.T("Carpeta de archivos") : Loc.T("Archivo");
    }

    private static string CommonType(IReadOnlyList<string> paths)
    {
        var types = paths.Take(200).Select(TypeName).Distinct().ToList();
        return types.Count == 1 ? Loc.F("{0} elementos · {1}", paths.Count, types[0]) : Loc.F("{0} elementos · varios tipos", paths.Count);
    }

    /// <summary>El nombre de la aplicación que abre esa extensión (null si ninguna).</summary>
    private static string? AppFor(string extension)
    {
        uint size = 512;
        var text = new StringBuilder((int)size);
        return AssocQueryStringW(0, AssocStrFriendlyAppName, extension, null, text, ref size) == 0 && text.Length > 0 ? text.ToString() : null;
    }

    // ---------------- Win32 ----------------

    private const int RecallOnDataAccess = 0x400000, RecallOnOpen = 0x40000;
    private const int GenericAll = 0x10000000, GenericExecute = 0x20000000, GenericWrite = 0x40000000, GenericRead = unchecked((int)0x80000000);
    private const int GpsBestEffort = 0x40;
    private const uint PdtfIsViewable = 0x80;
    private const uint ShgfiTypeName = 0x400;
    private const int AssocStrFriendlyAppName = 4;
    private const int OaifAllowRegistration = 0x1, OaifRegisterExt = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        public string File;
        public string? Class;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("6F79D558-3E96-4549-A1D1-7D75D2288814"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyDescription
    {
        [PreserveSig] int GetPropertyKey(out PropertyKey key);
        [PreserveSig] int GetCanonicalName(out IntPtr name);
        [PreserveSig] int GetPropertyType(out ushort type);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int GetEditInvitation(out IntPtr invitation);
        [PreserveSig] int GetTypeFlags(uint mask, out uint flags);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, int flags, ref Guid riid, out IPropertyStore store);

    [DllImport("propsys.dll")]
    private static extern int PSGetPropertyDescription(ref PropertyKey key, ref Guid riid, out IPropertyDescription description);

    [DllImport("propsys.dll")]
    private static extern int PSFormatForDisplayAlloc(ref PropertyKey key, IntPtr value, int flags, out IntPtr text);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(IntPtr value);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryStringW(int flags, int what, string association, string? extra, StringBuilder? result, ref uint size);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr parent, ref OpenAsInfo info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string path, out uint high);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
}
