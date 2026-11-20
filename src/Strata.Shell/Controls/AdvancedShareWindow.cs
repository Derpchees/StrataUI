using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Strata.Shell.Services;
using Strata.Shell.Services.Files;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.Themes;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using static Strata.Shell.Controls.PropertiesWindow;

namespace Strata.Shell.Controls;

/// <summary>
/// Uso compartido avanzado de una carpeta, como el de Windows pero con el tema de Strata UI: compartirla
/// o no, varios nombres de recurso, límite de usuarios a la vez, comentario, permisos por cuenta
/// (Control total, Cambiar, Lectura; permitir o denegar) y archivos sin conexión. Todo se guarda con
/// un solo permiso de administrador al aceptar.
/// </summary>
internal sealed class AdvancedShareWindow : FluentWindow
{
    private readonly string _folder;
    private readonly Action _saved;
    private readonly List<ShareEdit> _original;
    private readonly List<ShareEdit> _shares;
    private readonly HashSet<ShareEdit> _touchedAces = [];
    private ShareEdit? _current;
    private bool _loading;

    private readonly CheckBox _shareThis;
    private readonly StackPanel _settings = new();
    private readonly ComboBox _names = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _removeName;
    private readonly CheckBox _limit;
    private readonly TextBox _limitCount = new() { Width = 90, MaxLength = 8, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _remark = new() { MaxLength = 256 };
    private readonly ListBox _accounts = new() { MinHeight = 90, MaxHeight = 150 };
    private readonly CheckBox[,] _rights = new CheckBox[3, 2];
    private readonly ComboBox _cache = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _problem;

    private static readonly int[] Masks = [NetShares.FullMask, NetShares.ChangeMask, NetShares.ReadMask];
    private static readonly int[] CacheModes = [NetShares.CacheManual, NetShares.CacheOptimized, NetShares.CacheAuto, NetShares.CacheNone];

    public static void Open(Window owner, string folder, Action saved)
    {
        var window = new AdvancedShareWindow(folder, saved) { Owner = owner };
        window.Show();
        window.Activate();
    }

    private AdvancedShareWindow(string folder, Action saved)
    {
        _folder = folder;
        _saved = saved;
        _original = NetShares.For(folder).Select(s => NetShares.Details(s.Name)).ToList();
        _shares = _original.Select(Clone).ToList();

        Title = Loc.T("Uso compartido avanzado");
        Width = 480;
        Height = 640;
        MinWidth = 400;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        ShowInTaskbar = false;
        FluentBackdrop.Follow(this);

        var header = new StackPanel { Margin = new Thickness(24, 22, 56, 8), Background = Brushes.Transparent };
        header.Children.Add(Text(Loc.T("Uso compartido avanzado"), 18, primary: true, weight: FontWeights.SemiBold));
        header.Children.Add(Text(folder, 12));
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        var page = new StackPanel();
        _shareThis = new CheckBox { Content = Loc.T("Compartir esta carpeta"), IsChecked = _shares.Count > 0 };
        _shareThis.Checked += (_, _) => OnShareThisChanged(true);
        _shareThis.Unchecked += (_, _) => OnShareThisChanged(false);
        page.Children.Add(_shareThis);

        // Nombre del recurso, con agregar y quitar.
        _settings.Margin = new Thickness(0, 10, 0, 0);
        _settings.Children.Add(Section(Loc.T("Configuración")));
        var nameRow = new Grid();
        nameRow.ColumnDefinitions.Add(new ColumnDefinition());
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.Children.Add(_names);
        var addName = new Button { Content = Loc.T("Agregar"), Margin = new Thickness(6, 0, 0, 0) };
        addName.Click += async (_, _) => await AddNameAsync();
        Grid.SetColumn(addName, 1);
        nameRow.Children.Add(addName);
        _removeName = new Button { Content = Loc.T("Quitar"), Margin = new Thickness(6, 0, 0, 0) };
        _removeName.Click += (_, _) => RemoveName();
        Grid.SetColumn(_removeName, 2);
        nameRow.Children.Add(_removeName);
        _settings.Children.Add(Field(Loc.T("Nombre del recurso"), nameRow));
        _names.SelectionChanged += (_, _) => ShowCurrent();

        _limit = new CheckBox { Content = Loc.T("Limitar el número de usuarios simultáneos a:") };
        _limit.Checked += (_, _) => OnLimitChanged();
        _limit.Unchecked += (_, _) => OnLimitChanged();
        _limitCount.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _limitCount.TextChanged += (_, _) =>
        {
            if (!_loading && _current != null && int.TryParse(_limitCount.Text, out int count) && count > 0)
                _current.MaxUses = count;
        };
        var limitPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        limitPanel.Children.Add(_limit);
        _limitCount.Margin = new Thickness(28, 4, 0, 0);
        limitPanel.Children.Add(_limitCount);
        _settings.Children.Add(limitPanel);

        _remark.TextChanged += (_, _) =>
        {
            if (!_loading && _current != null)
                _current.Remark = _remark.Text;
        };
        _settings.Children.Add(Field(Loc.T("Comentario"), _remark));

        _settings.Children.Add(Gap());
        _settings.Children.Add(Section(Loc.T("Permisos del recurso")));
        _settings.Children.Add(Text(Loc.T("Lo que pueden hacer por la red. Además cuentan los permisos de la pestaña Seguridad: vale el más restrictivo."), 12));
        _accounts.Margin = new Thickness(0, 8, 0, 0);
        _accounts.SelectionChanged += (_, _) => ShowRights();
        _settings.Children.Add(_accounts);
        var accountButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addAccount = new Button { Content = Loc.T("Agregar…"), Icon = new SymbolIcon(SymbolRegular.PersonAdd24) };
        addAccount.Click += async (_, _) => await AddAccountAsync();
        var removeAccount = new Button { Content = Loc.T("Quitar"), Icon = new SymbolIcon(SymbolRegular.PersonDelete24), Margin = new Thickness(6, 0, 0, 0) };
        removeAccount.Click += (_, _) => RemoveAccount();
        accountButtons.Children.Add(addAccount);
        accountButtons.Children.Add(removeAccount);
        _settings.Children.Add(accountButtons);
        _settings.Children.Add(RightsGrid());

        _settings.Children.Add(Gap());
        _settings.Children.Add(Section(Loc.T("Archivos sin conexión")));
        _cache.Items.Add(Loc.T("Solo los archivos y programas que elijan los usuarios"));
        _cache.Items.Add(Loc.T("Todos los archivos y programas, optimizado para rendimiento"));
        _cache.Items.Add(Loc.T("Todos los archivos y programas que abran los usuarios"));
        _cache.Items.Add(Loc.T("Ningún archivo ni programa disponible sin conexión"));
        _cache.SelectionChanged += (_, _) =>
        {
            if (!_loading && _current != null && _cache.SelectedIndex >= 0)
                _current.Cache = CacheModes[_cache.SelectedIndex];
        };
        _settings.Children.Add(Field(Loc.T("Qué pueden usar sin conexión los que se conectan"), _cache));
        page.Children.Add(_settings);

        _problem = Text("", 12);
        _problem.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        _problem.Margin = new Thickness(0, 10, 0, 0);
        _problem.Visibility = Visibility.Collapsed;
        page.Children.Add(_problem);

        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24, 12, 24, 16) };

        var ok = FooterButton(Loc.T("Aceptar"), ControlAppearance.Primary);
        ok.Icon = new SymbolIcon(SymbolRegular.Shield24);
        ok.IsDefault = true;
        ok.Click += async (_, _) => await SaveAsync();
        var cancel = FooterButton(Loc.T("Cancelar"), ControlAppearance.Secondary);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 12, 16, 14) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var footer = new Border { Child = buttons };
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
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = height });
        root.Children.Add(header);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        root.Children.Add(close);
        Content = root;

        Loaded += (_, _) =>
        {
            DialogPlacement.Place(this);
            Motion.In(root, root, TaskbarPosition.Bottom, 0.5);
        };
        FillNames(_shares.FirstOrDefault());
    }

    private static ShareEdit Clone(ShareEdit edit) => new()
    {
        Name = edit.Name,
        IsNew = edit.IsNew,
        Remark = edit.Remark,
        MaxUses = edit.MaxUses,
        Cache = edit.Cache,
        Aces = edit.Aces?.Select(a => new ShareAce { Sid = a.Sid, Allow = a.Allow, Deny = a.Deny }).ToList(),
    };

    private static ShareEdit NewShare(string name) => new()
    {
        Name = name,
        IsNew = true,
        Aces = [new ShareAce { Sid = NetShares.EveryoneSid, Allow = NetShares.ReadMask }],
    };

    // ---------------- Nombres ----------------

    private void OnShareThisChanged(bool shared)
    {
        if (_loading)
            return;
        if (shared && _shares.Count == 0)
        {
            // Vuelven los que había; si no había ninguno, uno nuevo con el nombre de la carpeta.
            if (_original.Count > 0)
                _shares.AddRange(_original.Select(Clone));
            else
                _shares.Add(NewShare(NetShares.SuggestName(_folder)));
        }
        FillNames(_shares.FirstOrDefault());
    }

    private void FillNames(ShareEdit? select)
    {
        _loading = true;
        _names.Items.Clear();
        foreach (var share in _shares)
            _names.Items.Add(new ComboBoxItem { Content = share.Name, Tag = share });
        _loading = false;
        bool shared = _shareThis.IsChecked == true;
        _settings.IsEnabled = shared;
        _settings.Opacity = shared ? 1 : 0.5;
        _removeName.IsEnabled = _shares.Count > 1;
        int index = select is null ? -1 : _shares.IndexOf(select);
        _names.SelectedIndex = index >= 0 ? index : _shares.Count > 0 ? 0 : -1;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        _current = (_names.SelectedItem as ComboBoxItem)?.Tag as ShareEdit;
        _loading = true;
        _limit.IsChecked = _current is { MaxUses: > 0 };
        _limitCount.Text = _current is { MaxUses: > 0 } ? _current.MaxUses.ToString() : "20";
        _limitCount.IsEnabled = _limit.IsChecked == true;
        _remark.Text = _current?.Remark ?? "";
        _cache.SelectedIndex = Math.Max(0, Array.IndexOf(CacheModes, _current?.Cache ?? NetShares.CacheManual));
        _loading = false;
        FillAccounts(null);
    }

    private void OnLimitChanged()
    {
        _limitCount.IsEnabled = _limit.IsChecked == true;
        if (_loading || _current is null)
            return;
        _current.MaxUses = _limit.IsChecked == true && int.TryParse(_limitCount.Text, out int count) && count > 0 ? count : -1;
    }

    private async Task AddNameAsync()
    {
        var box = new TextBox { MaxLength = 80, Margin = new Thickness(0, 8, 0, 0) };
        var problem = Text(Loc.T("Hasta 80 caracteres, sin \\ / [ ] : | < > + ; , ? * = ni $ al final."), 12);
        var content = new StackPanel { Children = { box, problem } };
        box.Loaded += (_, _) => box.Focus();
        if (await StrataDialog.ShowAsync(this, Loc.T("Nuevo nombre de recurso"), Loc.T("Otro nombre con el que se ve esta misma carpeta en la red."),
                                         content, false, Loc.T("Agregar"), Loc.T("Cancelar")) != 0)
            return;
        string name = box.Text.Trim();
        if (!NetShares.ValidName(name) || _shares.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            || (NetShares.Exists(name) && !_original.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))))
        {
            await StrataDialog.ShowAsync(this, Loc.T("No se pudo agregar"), Loc.T("Ese nombre no es válido o ya se usa en este equipo."), Loc.T("Aceptar"));
            return;
        }
        // Si se había quitado uno con ese nombre, vuelve como estaba.
        var share = _original.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is { } previous ? Clone(previous) : NewShare(name);
        _shares.Add(share);
        FillNames(share);
    }

    private void RemoveName()
    {
        if (_current is null || _shares.Count <= 1)
            return;
        _shares.Remove(_current);
        FillNames(_shares.FirstOrDefault());
    }

    // ---------------- Permisos ----------------

    private FrameworkElement RightsGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.RowDefinitions.Add(new RowDefinition());
        string[] titles = [Loc.T("Control total"), Loc.T("Cambiar"), Loc.T("Lectura")];
        var allow = Text(Loc.T("Permitir"), 12);
        var deny = Text(Loc.T("Denegar"), 12);
        Grid.SetColumn(allow, 1);
        Grid.SetColumn(deny, 2);
        grid.Children.Add(allow);
        grid.Children.Add(deny);
        for (int row = 0; row < 3; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var label = Text(titles[row], 13, primary: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(label, row + 1);
            grid.Children.Add(label);
            for (int column = 0; column < 2; column++)
            {
                int r = row, c = column;
                var box = new CheckBox { MinWidth = 0, Margin = new Thickness(0, 2, 0, 2) };
                System.Windows.Automation.AutomationProperties.SetName(box, $"{titles[row]}: {(column == 0 ? Loc.T("Permitir") : Loc.T("Denegar"))}");
                box.Checked += (_, _) => OnRightChanged(r, c, true);
                box.Unchecked += (_, _) => OnRightChanged(r, c, false);
                Grid.SetRow(box, row + 1);
                Grid.SetColumn(box, column + 1);
                grid.Children.Add(box);
                _rights[row, column] = box;
            }
        }
        return grid;
    }

    private ShareAce? SelectedAce => (_accounts.SelectedItem as ListBoxItem)?.Tag as ShareAce;

    private void FillAccounts(ShareAce? select)
    {
        _accounts.Items.Clear();
        if (_current?.Aces is { } aces)
            foreach (var ace in aces)
                _accounts.Items.Add(new ListBoxItem { Content = AccountName(ace.Sid), Tag = ace });
        _accounts.IsEnabled = _current?.Aces != null;
        int index = select is null || _current?.Aces is null ? 0 : _current.Aces.IndexOf(select);
        _accounts.SelectedIndex = _accounts.Items.Count > 0 ? Math.Max(0, index) : -1;
        if (_current is { Aces: null })
        {
            _problem.Text = Loc.T("No se pudieron leer los permisos de este recurso; se conservan como están.");
            _problem.Visibility = Visibility.Visible;
        }
        ShowRights();
    }

    /// <summary>Nivel de un permiso: 0 Control total, 1 Cambiar, 2 Lectura, 3 nada.</summary>
    private static int Level(int mask) =>
        (mask & NetShares.FullMask) == NetShares.FullMask ? 0 : (mask & NetShares.ChangeMask) == NetShares.ChangeMask ? 1 : (mask & NetShares.ReadMask) == NetShares.ReadMask ? 2 : 3;

    private void ShowRights()
    {
        var ace = SelectedAce;
        _loading = true;
        for (int column = 0; column < 2; column++)
        {
            int level = ace is null ? 3 : Level(column == 0 ? ace.Allow : ace.Deny);
            for (int row = 0; row < 3; row++)
            {
                _rights[row, column].IsEnabled = ace != null;
                _rights[row, column].IsChecked = row >= level;
            }
        }
        _loading = false;
    }

    private void OnRightChanged(int row, int column, bool on)
    {
        if (_loading || SelectedAce is not { } ace || _current is null)
            return;
        // Como en Windows: cada permiso incluye a los de abajo (Control total ⊃ Cambiar ⊃ Lectura).
        int level = on ? row : row + 1;
        int mask = level < 3 ? Masks[level] : 0;
        if (column == 0)
            ace.Allow = mask;
        else
            ace.Deny = mask;
        _touchedAces.Add(_current);
        ShowRights();
    }

    private async Task AddAccountAsync()
    {
        if (_current?.Aces is null)
            return;
        var known = new List<(string Sid, string Name)>();
        foreach (string sid in new[] { NetShares.EveryoneSid, "S-1-5-32-545", "S-1-5-32-544", WindowsIdentity.GetCurrent().User?.Value ?? "" })
            if (sid.Length > 0 && !_current.Aces.Any(a => a.Sid == sid))
                known.Add((sid, AccountName(sid)));
        var box = new ComboBox { IsEditable = true, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (_, name) in known)
            box.Items.Add(name);
        box.Loaded += (_, _) => box.Focus();
        var content = new StackPanel { Children = { box, Text(Loc.T("Un usuario o grupo de este equipo o del dominio, por ejemplo Usuarios o EQUIPO\\nombre."), 12) } };
        if (await StrataDialog.ShowAsync(this, Loc.T("Agregar usuario o grupo"), Loc.T("¿A quién le das permiso sobre este recurso?"),
                                         content, false, Loc.T("Agregar"), Loc.T("Cancelar")) != 0)
            return;
        string typed = box.Text.Trim();
        string? found = known.FirstOrDefault(k => string.Equals(k.Name, typed, StringComparison.OrdinalIgnoreCase)).Sid ?? Resolve(typed);
        if (found is null)
        {
            await StrataDialog.ShowAsync(this, Loc.T("No se encontró"), Loc.F("No hay ningún usuario ni grupo llamado {0}.", typed), Loc.T("Aceptar"));
            return;
        }
        var ace = _current.Aces.FirstOrDefault(a => a.Sid == found);
        if (ace is null)
        {
            ace = new ShareAce { Sid = found, Allow = NetShares.ReadMask };
            _current.Aces.Add(ace);
            _touchedAces.Add(_current);
        }
        FillAccounts(ace);
    }

    private void RemoveAccount()
    {
        if (_current?.Aces is null || SelectedAce is not { } ace)
            return;
        _current.Aces.Remove(ace);
        _touchedAces.Add(_current);
        FillAccounts(null);
    }

    private static string AccountName(string sid)
    {
        try
        {
            return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return sid;
        }
    }

    private static string? Resolve(string name)
    {
        if (name.Length == 0)
            return null;
        try
        {
            return new NTAccount(name).Translate(typeof(SecurityIdentifier)).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            try
            {
                return new NTAccount(Environment.MachineName, name).Translate(typeof(SecurityIdentifier)).Value;
            }
            catch (Exception inner) when (inner is IdentityNotMappedException or SystemException)
            {
                return null;
            }
        }
    }

    // ---------------- Guardar ----------------

    private async Task SaveAsync()
    {
        _problem.Visibility = Visibility.Collapsed;
        if (_limit.IsChecked == true && _current != null && !(int.TryParse(_limitCount.Text, out int count) && count is > 0 and <= 16_777_216))
        {
            _problem.Text = Loc.T("El límite de usuarios debe ser un número mayor que cero.");
            _problem.Visibility = Visibility.Visible;
            return;
        }
        bool shared = _shareThis.IsChecked == true;
        var edits = new List<ShareEdit>();
        var keep = shared ? _shares : [];
        foreach (var original in _original)
            if (!keep.Any(s => string.Equals(s.Name, original.Name, StringComparison.OrdinalIgnoreCase)))
                edits.Add(new ShareEdit { Name = original.Name, Remove = true });
        foreach (var share in keep)
        {
            var original = _original.FirstOrDefault(s => string.Equals(s.Name, share.Name, StringComparison.OrdinalIgnoreCase));
            var edit = Clone(share);
            edit.IsNew = original is null;
            if (!edit.IsNew && !_touchedAces.Contains(share))
                edit.Aces = null; // No se tocaron: se dejan como están (también lo que no se sabe mostrar).
            if (!edit.IsNew && edit.Aces is null && edit.Remark == original!.Remark && edit.MaxUses == original.MaxUses && edit.Cache == original.Cache)
                continue;
            edits.Add(edit);
        }
        if (edits.Count == 0)
        {
            Close();
            return;
        }
        string question = shared ? Loc.T("Guardar el uso compartido avanzado de esta carpeta.") : Loc.T("Dejar de compartir esta carpeta en la red.");
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (!await ElevatedFiles.AskAsync(hwnd, question))
            return;
        string? error = await ElevatedFiles.RunAsync(new ElevatedFiles.Job { Op = "sharesave", Paths = [_folder], Shares = edits });
        _saved();
        if (error is null)
        {
            Close();
            return;
        }
        if (error.Length > 0)
            await StrataDialog.ShowAsync(this, Loc.T("No se pudo guardar todo"), error, Loc.T("Aceptar"));
    }
}
