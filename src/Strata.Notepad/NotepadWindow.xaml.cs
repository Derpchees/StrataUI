using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Strata.Shell.Controls;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Notepad;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;

namespace Strata.Shell.Notepad;

/// <summary>Un documento abierto: su archivo, su formato y su propio cuadro de texto (con su historial de deshacer).</summary>
public sealed class NotepadDocument : INotifyPropertyChanged
{
    private string? _path;
    private string _untitled = "";
    private bool _dirty;
    private bool _selected;

    public NotepadDocument(TextBox editor) => Editor = editor;

    public TextBox Editor { get; }
    public TextEncodingKind Encoding { get; set; } = TextEncodingKind.Utf8;
    public LineEnding LineEnding { get; set; } = LineEnding.CrLf;
    /// <summary>Fecha del archivo al abrirlo o guardarlo: si cambia por fuera, se recarga.</summary>
    public DateTime LastWrite { get; set; }

    public string? Path
    {
        get => _path;
        set { _path = value; Changed(); Changed(nameof(Title)); Changed(nameof(ToolTip)); }
    }

    public string Untitled
    {
        get => _untitled;
        set { _untitled = value; Changed(nameof(Title)); Changed(nameof(ToolTip)); }
    }

    public string Title => _path is null ? _untitled : System.IO.Path.GetFileName(_path);
    public string ToolTip => _path ?? _untitled;

    public bool IsDirty
    {
        get => _dirty;
        set { if (_dirty != value) { _dirty = value; Changed(); } }
    }

    public bool IsSelected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Changed(); } }
    }

    /// <summary>Pestaña vacía sin archivo ni cambios: abrir un archivo la reutiliza.</summary>
    public bool IsBlank => _path is null && !_dirty && Editor.Text.Length == 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// El Bloc de notas de Strata UI: pestañas, buscar y reemplazar, ir a la línea, zoom, ajuste de línea y
/// cualquier codificación. Una sola ventana: abrir otro archivo agrega una pestaña.
/// </summary>
public partial class NotepadWindow : FluentWindow
{
    private static NotepadWindow? _current;
    private static double _zoom = 1;

    private readonly ObservableCollection<NotepadDocument> _docs = [];
    private readonly DispatcherTimer _caretTimer;
    private readonly DispatcherTimer _badgeTimer;
    private NotepadDocument? _doc;
    private bool _loading;
    private bool _closingChecked;
    private bool _checkingFiles;

    public static Window Open(IReadOnlyList<string> paths)
    {
        var window = _current ??= new NotepadWindow();
        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        if (paths.Count == 0)
            window.NewDocument();
        else
        {
            foreach (var path in paths)
                window.OpenPath(path);
        }
        if (window._docs.Count == 0)
            window.NewDocument();
        return window;
    }

    private NotepadWindow()
    {
        InitializeComponent();
        FluentBackdrop.Follow(this);
        SourceInitialized += (_, _) => NotepadIcon.ApplyAppId(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        TabStrip.ItemsSource = _docs;
        WrapToggle.IsChecked = Settings.Current.NotepadWordWrap;
        StatusBar.Visibility = Settings.Current.NotepadStatusBar ? Visibility.Visible : Visibility.Collapsed;

        _caretTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
        _caretTimer.Tick += (_, _) => { _caretTimer.Stop(); UpdateStatus(); };
        _badgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _badgeTimer.Tick += (_, _) => { _badgeTimer.Stop(); ZoomBadge.Visibility = Visibility.Collapsed; };

        Loaded += (_, _) => Motion.In(Root, Root, TaskbarPosition.Bottom, 0.8);
        Activated += (_, _) => Dispatcher.BeginInvoke(CheckExternalChanges, DispatcherPriority.Background);
        Closing += Window_Closing;
        Closed += (_, _) => _current = null;
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseWheel += Window_PreviewMouseWheel;
        PreviewDragOver += Window_PreviewDragOver;
        PreviewDrop += Window_PreviewDrop;
    }

    // ── Documentos y pestañas ────────────────────────────────────────────

    private TextBox CreateEditor()
    {
        var box = new TextBox
        {
            Style = (Style)FindResource("EditorBox"),
            FontFamily = new FontFamily(Settings.Current.NotepadFont),
        };
        ApplyWrap(box);
        ApplyZoom(box);
        box.TextChanged += Editor_TextChanged;
        box.SelectionChanged += (_, _) => _caretTimer.Start();
        return box;
    }

    private NotepadDocument NewDocument()
    {
        int n = 1;
        while (_docs.Any(d => d.Path is null && d.Untitled == UntitledName(n)))
            n++;
        var doc = new NotepadDocument(CreateEditor()) { Untitled = UntitledName(n) };
        _docs.Add(doc);
        Select(doc);
        return doc;
    }

    private static string UntitledName(int n) => n == 1 ? Loc.T("Sin título") : $"{Loc.T("Sin título")} {n}";

    private void Select(NotepadDocument doc)
    {
        foreach (var d in _docs)
            d.IsSelected = d == doc;
        _doc = doc;
        EditorHost.Content = doc.Editor;
        Title = $"{doc.Title} - {Loc.T("Bloc de notas")}";
        UpdateStatus();
        if (FindBar.Visibility == Visibility.Visible)
            UpdateMatchInfo();
        Dispatcher.BeginInvoke(() => doc.Editor.Focus(), DispatcherPriority.Input);
    }

    private void OpenPath(string path)
    {
        try
        {
            path = System.IO.Path.GetFullPath(path);
        }
        catch
        {
            return;
        }
        if (_docs.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            Select(open);
            return;
        }
        _ = OpenPathAsync(path);
    }

    private async Task OpenPathAsync(string path)
    {
        TextFileContent content;
        DateTime lastWrite = default;
        if (!File.Exists(path))
        {
            if (Directory.Exists(path))
                return;
            // "notepad nuevo.txt": un documento vacío que se guarda con ese nombre.
            content = new TextFileContent("", TextEncodingKind.Utf8, LineEnding.CrLf);
        }
        else
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > TextFile.MaxSize)
                {
                    int choice = await StrataDialog.ShowAsync(this, Loc.T("Archivo muy grande"),
                        string.Format(Loc.T("{0} pesa {1} MB. El Bloc de notas de Strata UI abre archivos de hasta {2} MB."),
                            info.Name, info.Length / 1048576, TextFile.MaxSize / 1048576),
                        Loc.T("Abrir en el Bloc de notas de Windows"), Loc.T("Cancelar"));
                    if (choice == 0)
                        WindowsNotepad.Open([path]);
                    return;
                }
                var bytes = await Task.Run(() => File.ReadAllBytes(path));
                if (TextFile.LooksBinary(bytes))
                {
                    int choice = await StrataDialog.ShowAsync(this, Loc.T("No parece un archivo de texto"),
                        string.Format(Loc.T("{0} tiene datos binarios. Si lo guardas desde aquí puede dañarse."), info.Name),
                        Loc.T("Abrir de todos modos"), Loc.T("Cancelar"));
                    if (choice != 0)
                        return;
                }
                content = await Task.Run(() => TextFile.Decode(bytes));
                lastWrite = info.LastWriteTimeUtc;
            }
            catch (Exception ex)
            {
                Log.Error($"Bloc de notas: no se pudo abrir {path}", ex);
                await StrataDialog.ShowAsync(this, Loc.T("No se pudo abrir"), ex.Message, Loc.T("Aceptar"));
                return;
            }
        }

        if (_docs.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)) is { } already)
        {
            Select(already);
            return;
        }
        var doc = _doc is { IsBlank: true } blank ? blank : NewDocument();
        doc.Path = path;
        doc.Encoding = content.Encoding;
        doc.LineEnding = content.LineEnding;
        doc.LastWrite = lastWrite;
        SetText(doc, content.Text);
        Select(doc);
        doc.Editor.CaretIndex = 0;
        doc.Editor.ScrollToHome();
    }

    /// <summary>Pone el texto sin marcarlo como cambio ni dejarlo en el historial de deshacer.</summary>
    private void SetText(NotepadDocument doc, string text)
    {
        _loading = true;
        doc.Editor.IsUndoEnabled = false;
        doc.Editor.Text = text;
        doc.Editor.IsUndoEnabled = true;
        _loading = false;
        doc.IsDirty = false;
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;
        if (_docs.FirstOrDefault(d => d.Editor == sender) is { } doc)
            doc.IsDirty = true;
        _caretTimer.Start();
        if (FindBar.Visibility == Visibility.Visible)
            UpdateMatchInfo();
    }

    private async Task<bool> CloseDocumentAsync(NotepadDocument doc)
    {
        if (doc.IsDirty && !await ConfirmDiscardAsync(doc))
            return false;
        int index = _docs.IndexOf(doc);
        _docs.Remove(doc);
        doc.Editor.TextChanged -= Editor_TextChanged;
        if (_docs.Count == 0)
        {
            _closingChecked = true;
            Close();
            return true;
        }
        if (doc == _doc)
            Select(_docs[Math.Min(index, _docs.Count - 1)]);
        return true;
    }

    /// <summary>Preguntar antes de perder cambios: true si se puede cerrar (guardado o descartado).</summary>
    private async Task<bool> ConfirmDiscardAsync(NotepadDocument doc)
    {
        Select(doc);
        int choice = await StrataDialog.ShowAsync(this, Loc.T("¿Guardar los cambios?"),
            string.Format(Loc.T("{0} tiene cambios sin guardar."), doc.Title),
            Loc.T("Guardar"), Loc.T("No guardar"), Loc.T("Cancelar"));
        return choice switch
        {
            0 => await SaveAsync(doc, saveAs: false),
            1 => true,
            _ => false,
        };
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingChecked || !_docs.Any(d => d.IsDirty))
            return;
        e.Cancel = true;
        foreach (var doc in _docs.Where(d => d.IsDirty).ToList())
        {
            if (!await ConfirmDiscardAsync(doc))
                return;
        }
        _closingChecked = true;
        Close();
    }

    // ── Guardar ──────────────────────────────────────────────────────────

    private async Task<bool> SaveAsync(NotepadDocument doc, bool saveAs)
    {
        string? path = doc.Path;
        if (saveAs || path is null)
        {
            string folder = path is null ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : System.IO.Path.GetDirectoryName(path)!;
            string name = path is null ? doc.Untitled + ".txt" : System.IO.Path.GetFileName(path);
            path = await FilePicker.PickSaveAsync(Loc.T("Guardar como"), Filter, folder, name);
            if (path is null)
                return false;
        }
        if (doc.Encoding == TextEncodingKind.Ansi && !TextFile.FitsAnsi(doc.Editor.Text))
        {
            int choice = await StrataDialog.ShowAsync(this, Loc.T("Hay caracteres que ANSI no guarda"),
                Loc.T("Algunos caracteres se perderían con la codificación ANSI. ¿Guardar en UTF-8?"),
                Loc.T("Guardar en UTF-8"), Loc.T("Guardar en ANSI"), Loc.T("Cancelar"));
            if (choice < 0)
                return false;
            if (choice == 0)
                doc.Encoding = TextEncodingKind.Utf8;
        }
        try
        {
            string text = doc.Editor.Text;
            var (encoding, ending) = (doc.Encoding, doc.LineEnding);
            await Task.Run(() => TextFile.Write(path, text, encoding, ending));
            doc.Path = path;
            doc.LastWrite = File.GetLastWriteTimeUtc(path);
            doc.IsDirty = false;
            if (doc == _doc)
            {
                Title = $"{doc.Title} - {Loc.T("Bloc de notas")}";
                UpdateStatus();
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Bloc de notas: no se pudo guardar {path}", ex);
            await StrataDialog.ShowAsync(this, Loc.T("No se pudo guardar"), ex.Message, Loc.T("Aceptar"));
            return false;
        }
    }

    private static string Filter =>
        $"{Loc.T("Documentos de texto")}|*.txt|{Loc.T("Todos los archivos")}|*.*";

    /// <summary>Al volver a la ventana: si un archivo cambió por fuera, se recarga (o se pregunta, si hay cambios aquí).</summary>
    private async void CheckExternalChanges()
    {
        if (_checkingFiles)
            return;
        _checkingFiles = true;
        try
        {
            foreach (var doc in _docs.ToList())
            {
                if (doc.Path is not { } path || doc.LastWrite == default || !File.Exists(path))
                    continue;
                var write = File.GetLastWriteTimeUtc(path);
                if (write == doc.LastWrite)
                    continue;
                doc.LastWrite = write;
                if (doc.IsDirty)
                {
                    int choice = await StrataDialog.ShowAsync(this, Loc.T("El archivo cambió"),
                        string.Format(Loc.T("Otro programa modificó {0}. ¿Recargarlo y perder tus cambios?"), doc.Title),
                        Loc.T("Recargar"), Loc.T("Conservar los míos"));
                    if (choice != 0)
                        continue;
                }
                var content = await Task.Run(() => TextFile.Read(path));
                int caret = doc.Editor.CaretIndex;
                doc.Encoding = content.Encoding;
                doc.LineEnding = content.LineEnding;
                SetText(doc, content.Text);
                doc.Editor.CaretIndex = Math.Min(caret, doc.Editor.Text.Length);
                if (doc == _doc)
                    UpdateStatus();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Bloc de notas: no se pudo recargar un archivo", ex);
        }
        finally
        {
            _checkingFiles = false;
        }
    }

    // ── Comandos ─────────────────────────────────────────────────────────

    private void New_Click(object sender, RoutedEventArgs e) => NewDocument();

    private async void Open_Click(object sender, RoutedEventArgs e) => await PickAndOpenAsync();

    private async Task PickAndOpenAsync()
    {
        var files = await FilePicker.PickFilesAsync(Loc.T("Abrir"), Filter, multiselect: true);
        foreach (var file in files)
            OpenPath(file);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_doc != null)
            await SaveAsync(_doc, saveAs: false);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_doc != null)
            await SaveAsync(_doc, saveAs: true);
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _doc?.Editor.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _doc?.Editor.Redo();

    private void WrapToggle_Click(object sender, RoutedEventArgs e) => SetWrap(WrapToggle.IsChecked == true);

    private void SetWrap(bool wrap)
    {
        WrapToggle.IsChecked = wrap;
        Settings.Update(s => s.NotepadWordWrap = wrap);
        foreach (var doc in _docs)
            ApplyWrap(doc.Editor);
        UpdateStatus();
    }

    private static void ApplyWrap(TextBox box)
    {
        bool wrap = Settings.Current.NotepadWordWrap;
        box.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        box.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    private void InsertDateTime()
    {
        if (_doc is null)
            return;
        var now = DateTime.Now;
        _doc.Editor.SelectedText = $"{now.ToShortTimeString()} {now.ToShortDateString()}";
        _doc.Editor.CaretIndex = _doc.Editor.SelectionStart + _doc.Editor.SelectionLength;
        _doc.Editor.SelectionLength = 0;
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        MenuItem Item(string text, SymbolRegular icon, Action action, bool enabled = true, string? gesture = null)
        {
            var item = new MenuItem { Header = text, Icon = new SymbolIcon(icon), IsEnabled = enabled, InputGestureText = gesture ?? "" };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
        void Check(string text, bool value, Action<bool> set)
        {
            var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = value, StaysOpenOnClick = true };
            item.Checked += (_, _) => set(true);
            item.Unchecked += (_, _) => set(false);
            menu.Items.Add(item);
        }

        string? path = _doc?.Path;
        Item(Loc.T("Fecha y hora"), SymbolRegular.CalendarClock20, InsertDateTime, gesture: "F5");
        Item(Loc.T("Seleccionar todo"), SymbolRegular.SelectAllOn20, () => _doc?.Editor.SelectAll(), gesture: "Ctrl+A");
        menu.Items.Add(new Separator());
        Item(Loc.T("Abrir la carpeta del archivo"), SymbolRegular.FolderOpen20, () => Services.Files.FileOperations.ShowInExplorer(path!, select: true), path != null && File.Exists(path));
        Item(Loc.T("Abrir en el Bloc de notas de Windows"), SymbolRegular.WindowNew20, () => WindowsNotepad.Open([path!]), path != null && File.Exists(path));
        menu.Items.Add(new Separator());
        Check(Loc.T("Barra de estado"), Settings.Current.NotepadStatusBar, on =>
        {
            Settings.Update(s => s.NotepadStatusBar = on);
            StatusBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        });
        Check(Loc.T("Abrir los archivos de texto aquí"), Settings.Current.NotepadReplace, on => Settings.Update(s => s.NotepadReplace = on));
        Item(Loc.T("Elegir como aplicación predeterminada…"), SymbolRegular.CheckmarkCircle20, NotepadRegistration.OpenDefaultAppsSettings);
        menu.IsOpen = true;
    }

    // ── Atajos ───────────────────────────────────────────────────────────

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (ctrl && key == Key.Tab && _docs.Count > 1 && _doc != null)
        {
            int i = _docs.IndexOf(_doc);
            Select(_docs[(i + (shift ? _docs.Count - 1 : 1)) % _docs.Count]);
            e.Handled = true;
            return;
        }
        if (mods == ModifierKeys.Alt && key == Key.Z)
        {
            SetWrap(WrapToggle.IsChecked != true);
            e.Handled = true;
            return;
        }
        switch (key)
        {
            case Key.F3:
                FindStep(!shift);
                e.Handled = true;
                return;
            case Key.F5 when mods == ModifierKeys.None:
                InsertDateTime();
                e.Handled = true;
                return;
            case Key.Escape when FindBar.Visibility == Visibility.Visible:
                HideFind();
                e.Handled = true;
                return;
        }
        if (!ctrl)
            return;
        e.Handled = true;
        switch (key)
        {
            case Key.N: NewDocument(); break;
            case Key.O: await PickAndOpenAsync(); break;
            case Key.S when _doc != null: await SaveAsync(_doc, saveAs: shift); break;
            case Key.W or Key.F4 when _doc != null: await CloseDocumentAsync(_doc); break;
            case Key.F: ShowFind(replace: false); break;
            case Key.H: ShowFind(replace: true); break;
            case Key.G: await GoToLineAsync(); break;
            case Key.OemPlus or Key.Add: Zoom(+0.1); break;
            case Key.OemMinus or Key.Subtract: Zoom(-0.1); break;
            case Key.D0 or Key.NumPad0: SetZoom(1); break;
            default: e.Handled = false; break;
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;
        Zoom(e.Delta > 0 ? 0.1 : -0.1);
        e.Handled = true;
    }

    // ── Arrastrar archivos a la ventana ──────────────────────────────────

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;
        e.Handled = true;
        foreach (var file in files.Where(File.Exists))
            OpenPath(file);
    }

    // ── Pestañas ─────────────────────────────────────────────────────────

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotepadDocument doc })
            Select(doc);
    }

    private async void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: NotepadDocument doc })
        {
            e.Handled = true;
            await CloseDocumentAsync(doc);
        }
    }

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotepadDocument doc })
            await CloseDocumentAsync(doc);
    }

    // ── Zoom ─────────────────────────────────────────────────────────────

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(+0.1);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(-0.1);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1);

    private void Zoom(double step) => SetZoom(Math.Round(_zoom + step, 1));

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.5, 4);
        foreach (var doc in _docs)
            ApplyZoom(doc.Editor);
        ZoomBadge.Text = $"{_zoom * 100:0}%";
        ZoomBadge.Visibility = Visibility.Visible;
        _badgeTimer.Stop();
        _badgeTimer.Start();
        UpdateStatus();
    }

    private static void ApplyZoom(TextBox box) => box.FontSize = Settings.Current.NotepadFontSize * _zoom;

    // ── Barra de estado ──────────────────────────────────────────────────

    private void UpdateStatus()
    {
        if (_doc is null)
            return;
        var box = _doc.Editor;
        string text = box.Text;
        int caret = Math.Min(box.CaretIndex, text.Length);
        // Línea y columna lógicas (las de TextBox cuentan las líneas ajustadas).
        int line = 1, lineStart = 0;
        for (int i = text.IndexOf('\n'); i >= 0 && i < caret; i = text.IndexOf('\n', i + 1))
        {
            line++;
            lineStart = i + 1;
        }
        CaretInfo.Content = string.Format(Loc.T("Lín {0}, col {1}"), line, caret - lineStart + 1);
        int chars = text.Length - CountNewlines(text);
        CountInfo.Text = box.SelectionLength > 0
            ? string.Format(Loc.T("{0} de {1} caracteres"), box.SelectionLength, chars)
            : string.Format(Loc.T("{0} caracteres"), chars);
        ZoomInfo.Content = $"{_zoom * 100:0}%";
        LineEndingButton.Content = LineEndingName(_doc.LineEnding);
        EncodingButton.Content = EncodingName(_doc.Encoding);
        UndoButton.IsEnabled = box.CanUndo;
        RedoButton.IsEnabled = box.CanRedo;
    }

    /// <summary>Cuántos caracteres de salto de línea hay (no cuentan como caracteres escritos).</summary>
    private static int CountNewlines(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c is '\r' or '\n')
                count++;
        }
        return count;
    }

    private static string LineEndingName(LineEnding ending) => ending switch
    {
        LineEnding.Lf => "Unix (LF)",
        LineEnding.Cr => "Mac (CR)",
        _ => "Windows (CRLF)",
    };

    private static string EncodingName(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8Bom => Loc.T("UTF-8 con BOM"),
        TextEncodingKind.Utf16Le => "UTF-16 LE",
        TextEncodingKind.Utf16Be => "UTF-16 BE",
        TextEncodingKind.Ansi => "ANSI",
        _ => "UTF-8",
    };

    private void Encoding_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is not { } doc)
            return;
        ShowChoices(EncodingButton, Enum.GetValues<TextEncodingKind>(), doc.Encoding, EncodingName, kind =>
        {
            doc.Encoding = kind;
            if (doc.Path != null)
                doc.IsDirty = true;
        });
    }

    private void LineEnding_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is not { } doc)
            return;
        ShowChoices(LineEndingButton, Enum.GetValues<LineEnding>(), doc.LineEnding, LineEndingName, ending =>
        {
            doc.LineEnding = ending;
            if (doc.Path != null)
                doc.IsDirty = true;
        });
    }

    private void ShowChoices<T>(UIElement target, IEnumerable<T> values, T current, Func<T, string> name, Action<T> choose)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Top };
        foreach (var value in values)
        {
            var item = new MenuItem { Header = name(value), IsCheckable = true, IsChecked = EqualityComparer<T>.Default.Equals(value, current) };
            item.Click += (_, _) => { choose(value); UpdateStatus(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ── Ir a la línea ────────────────────────────────────────────────────

    private void GoTo_Click(object sender, RoutedEventArgs e) => _ = GoToLineAsync();

    private async Task GoToLineAsync()
    {
        if (_doc is not { } doc)
            return;
        string text = doc.Editor.Text;
        int total = CountLines(text);
        var input = new Wpf.Ui.Controls.TextBox { PlaceholderText = $"1 - {total}" };
        int choice = await StrataDialog.ShowAsync(this, Loc.T("Ir a la línea"),
            string.Format(Loc.T("Número de línea (1 a {0})"), total), input, false, Loc.T("Ir"), Loc.T("Cancelar"));
        if (choice != 0 || !int.TryParse(input.Text.Trim(), out int target))
            return;
        GoToLine(doc, Math.Clamp(target, 1, total));
    }

    private static int CountLines(string text)
    {
        int lines = 1;
        for (int i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
            lines++;
        return lines;
    }

    private void GoToLine(NotepadDocument doc, int line)
    {
        string text = doc.Editor.Text;
        int offset = 0;
        for (int n = 1; n < line; n++)
        {
            int next = text.IndexOf('\n', offset);
            if (next < 0)
                break;
            offset = next + 1;
        }
        doc.Editor.Focus();
        doc.Editor.Select(offset, 0);
        ScrollTo(doc.Editor, offset);
    }

    private static void ScrollTo(TextBox box, int offset)
    {
        int visual = box.GetLineIndexFromCharacterIndex(offset);
        if (visual >= 0)
            box.ScrollToLine(visual);
    }

    // ── Buscar y reemplazar ──────────────────────────────────────────────

    private void FindToggle_Click(object sender, RoutedEventArgs e)
    {
        if (FindToggle.IsChecked == true)
            ShowFind(replace: false);
        else
            HideFind();
    }

    private void ShowFind(bool replace)
    {
        FindBar.Visibility = Visibility.Visible;
        FindToggle.IsChecked = true;
        if (_doc is { } doc && doc.Editor.SelectionLength is > 0 and < 200 && !doc.Editor.SelectedText.Contains('\n'))
            FindText.Text = doc.Editor.SelectedText;
        var box = replace && FindText.Text.Length > 0 ? ReplaceText : FindText;
        Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
        UpdateMatchInfo();
    }

    private void HideFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindToggle.IsChecked = false;
        _doc?.Editor.Focus();
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => HideFind();
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindStep(forward: true);
    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindStep(forward: false);
    private void FindOption_Click(object sender, RoutedEventArgs e) => UpdateMatchInfo();

    private void FindText_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateMatchInfo();
        // Mientras se escribe, ir a la primera coincidencia desde donde estaba el cursor.
        if (_doc is { } doc && FindText.Text.Length > 0)
        {
            int from = doc.Editor.SelectionStart;
            if (Find(doc.Editor.Text, from, forward: true) is int at)
                SelectMatch(doc, at, keepFocus: true);
        }
    }

    private void FindText_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        FindStep(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), keepFocus: true);
        e.Handled = true;
    }

    private void ReplaceText_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            ReplaceAll();
        else
            ReplaceOne();
        e.Handled = true;
    }

    private StringComparison Comparison => MatchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private bool IsWholeWord(string text, int at, int length)
    {
        if (WholeWord.IsChecked != true)
            return true;
        static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_';
        return (at == 0 || !Word(text[at - 1])) && (at + length >= text.Length || !Word(text[at + length]));
    }

    /// <summary>La siguiente (o anterior) coincidencia desde <paramref name="from"/>, dando la vuelta al llegar al final.</summary>
    private int? Find(string text, int from, bool forward)
    {
        string query = FindText.Text;
        if (query.Length == 0 || text.Length == 0)
            return null;
        from = Math.Clamp(from, 0, text.Length);
        if (forward)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int start = pass == 0 ? from : 0;
                for (int i = text.IndexOf(query, start, Comparison); i >= 0; i = text.IndexOf(query, i + 1, Comparison))
                {
                    if (pass == 1 && i >= from)
                        break;
                    if (IsWholeWord(text, i, query.Length))
                        return i;
                }
            }
        }
        else
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int start = pass == 0 ? from - 1 : text.Length - 1;
                for (int i = start < 0 ? -1 : text.LastIndexOf(query, start, Comparison); i >= 0;
                     i = i == 0 ? -1 : text.LastIndexOf(query, i - 1, Comparison))
                {
                    if (pass == 1 && i < from)
                        break;
                    if (i + query.Length <= text.Length && IsWholeWord(text, i, query.Length))
                        return i;
                }
            }
        }
        return null;
    }

    private void FindStep(bool forward, bool keepFocus = false)
    {
        if (_doc is not { } doc)
            return;
        if (FindBar.Visibility != Visibility.Visible || FindText.Text.Length == 0)
        {
            ShowFind(replace: false);
            return;
        }
        var box = doc.Editor;
        int from = forward ? box.SelectionStart + box.SelectionLength : box.SelectionStart;
        if (Find(box.Text, from, forward) is int at)
            SelectMatch(doc, at, keepFocus);
        UpdateMatchInfo();
    }

    private void SelectMatch(NotepadDocument doc, int at, bool keepFocus)
    {
        doc.Editor.Select(at, FindText.Text.Length);
        ScrollTo(doc.Editor, at);
        if (!keepFocus && !FindText.IsKeyboardFocusWithin && !ReplaceText.IsKeyboardFocusWithin)
            doc.Editor.Focus();
        UpdateMatchInfo();
    }

    private bool SelectionIsMatch(TextBox box) =>
        box.SelectionLength == FindText.Text.Length && FindText.Text.Length > 0 &&
        string.Equals(box.SelectedText, FindText.Text, Comparison) &&
        IsWholeWord(box.Text, box.SelectionStart, box.SelectionLength);

    private void Replace_Click(object sender, RoutedEventArgs e) => ReplaceOne();
    private void ReplaceAll_Click(object sender, RoutedEventArgs e) => ReplaceAll();

    private void ReplaceOne()
    {
        if (_doc is not { } doc || FindText.Text.Length == 0)
            return;
        var box = doc.Editor;
        if (SelectionIsMatch(box))
        {
            box.SelectedText = ReplaceText.Text;
            box.Select(box.SelectionStart + box.SelectionLength, 0);
        }
        if (Find(box.Text, box.SelectionStart, forward: true) is int at)
            SelectMatch(doc, at, keepFocus: true);
        UpdateMatchInfo();
    }

    /// <summary>Reemplazar todo en un solo paso (un Ctrl+Z lo deshace entero).</summary>
    private void ReplaceAll()
    {
        if (_doc is not { } doc || FindText.Text.Length == 0)
            return;
        string text = doc.Editor.Text, query = FindText.Text, with = ReplaceText.Text;
        var result = new System.Text.StringBuilder(text.Length);
        int last = 0, count = 0;
        for (int i = text.IndexOf(query, Comparison); i >= 0; i = text.IndexOf(query, i, Comparison))
        {
            if (!IsWholeWord(text, i, query.Length))
            {
                i++;
                if (i >= text.Length)
                    break;
                continue;
            }
            result.Append(text, last, i - last).Append(with);
            last = i += query.Length;
            count++;
            if (i >= text.Length)
                break;
        }
        if (count == 0)
        {
            MatchInfo.Text = Loc.T("Sin resultados");
            return;
        }
        result.Append(text, last, text.Length - last);
        int caret = doc.Editor.CaretIndex;
        doc.Editor.BeginChange();
        doc.Editor.SelectAll();
        doc.Editor.SelectedText = result.ToString();
        doc.Editor.EndChange();
        doc.Editor.Select(Math.Min(caret, doc.Editor.Text.Length), 0);
        MatchInfo.Text = string.Format(Loc.T("{0} reemplazados"), count);
    }

    /// <summary>"3 de 12": en qué coincidencia está la selección y cuántas hay.</summary>
    private void UpdateMatchInfo()
    {
        if (_doc is not { } doc || FindText.Text.Length == 0)
        {
            MatchInfo.Text = "";
            return;
        }
        string text = doc.Editor.Text, query = FindText.Text;
        int total = 0, current = 0;
        int selection = SelectionIsMatch(doc.Editor) ? doc.Editor.SelectionStart : -1;
        for (int i = text.IndexOf(query, Comparison); i >= 0 && total < 100000; i = text.IndexOf(query, i + 1, Comparison))
        {
            if (!IsWholeWord(text, i, query.Length))
                continue;
            total++;
            if (i == selection)
                current = total;
        }
        MatchInfo.Text = total == 0 ? Loc.T("Sin resultados")
            : current > 0 ? string.Format(Loc.T("{0} de {1}"), current, total)
            : string.Format(Loc.T("{0} resultados"), total);
    }
}
