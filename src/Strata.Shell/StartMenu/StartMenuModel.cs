using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Strata.Shell.Flyouts;
using Strata.Shell.Services;
using Strata.Shell.Services.Localization;
using Strata.Shell.Services.StartMenu;

namespace Strata.Shell.StartMenu;

/// <summary>Estado compartido por los tres estilos de menú Inicio.</summary>
public sealed class StartMenuModel : INotifyPropertyChanged
{
    private string _query = "";
    private bool _showAllApps;
    private IReadOnlyList<StartApp> _results = [];

    public StartMenuModel()
    {
        var source = new CollectionViewSource { Source = StartMenuService.Apps };
        source.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StartApp.Group)));
        AllApps = source.View;
        // Las herramientas de Windows (Servicios, Desfragmentar…) no van en la lista: solo al buscarlas.
        AllApps.Filter = item => item is StartApp { IsSystemTool: false };
        _recent = ReadRecent();
    }

    private static IReadOnlyList<RecentItem> ReadRecent() => Settings.Current.StartShowRecent ? StartMenuService.GetRecent(8) : [];

    /// <summary>El menú se vuelve a abrir (la misma ventana): sin la búsqueda de antes y con los recientes al día.</summary>
    public void Reset()
    {
        Query = "";
        ShowAllApps = false;
        _recent = ReadRecent();
        Notify(nameof(Recent));
        Notify(nameof(HasRecent));
    }

    public ObservableCollection<StartApp> Pinned => StartMenuService.Pinned;
    public ICollectionView AllApps { get; }
    private IReadOnlyList<RecentItem> _recent;
    public IReadOnlyList<RecentItem> Recent => _recent;
    public bool HasRecent => Recent.Count > 0;

    // Opciones del Hub (se leen al abrir el menú)
    public bool ShowPinned => Settings.Current.StartShowPinned;
    public bool ShowAllAppsButton => Settings.Current.StartShowAllApps;
    public bool ShowUser => Settings.Current.StartShowUser;
    public IReadOnlyList<StartFooterButton> FooterButtons { get; } = StartFooterCatalog.Selected;

    public string UserName => StartMenuService.UserName;
    public string UserInitials => StartMenuService.UserInitials;
    public ImageSource? UserPicture => Settings.Current.StartShowUserPicture ? StartMenuService.UserPicture : null;
    public bool HasUserPicture => UserPicture != null;

    public string Query
    {
        get => _query;
        set
        {
            _query = value;
            Results = StartMenuService.Search(value);
            SearchFiles(value);
            Notify();
            Notify(nameof(IsSearching));
            Notify(nameof(NoResults));
            Notify(nameof(SearchInSidePanel));
            NotifyViews();
        }
    }

    public bool IsSearching => _query.Trim().Length > 0;

    /// <summary>Estilo Lista con la lista de apps oculta: los resultados van en el panel derecho.</summary>
    public bool SearchInSidePanel => IsSearching && !ShowAllAppsButton;

    public IReadOnlyList<StartApp> Results
    {
        get => _results;
        private set
        {
            _results = value;
            Notify();
            // Al sumar los archivos sigue elegido el mismo; si no, el primero (como en Windows).
            Selected = _selected is { } kept && value.Contains(kept) ? kept : value.FirstOrDefault();
        }
    }

    private StartApp? _selected;

    /// <summary>El resultado que abre Enter y que muestra el panel de detalles (se mueve con las flechas).</summary>
    public StartApp? Selected
    {
        get => _selected;
        private set
        {
            if (_selected == value)
                return;
            if (_selected != null)
                _selected.IsSelected = false;
            _selected = value;
            if (value != null)
                value.IsSelected = true;
            Notify();
        }
    }

    public void MoveSelection(int delta)
    {
        if (_results.Count > 0)
            Selected = _results[Math.Clamp(_results.ToList().IndexOf(_selected!) + delta, 0, _results.Count - 1)];
    }

    private int _fileSearch;

    /// <summary>Suma los archivos y carpetas del índice de Windows a los resultados, sin frenar la escritura.</summary>
    private async void SearchFiles(string query)
    {
        var id = ++_fileSearch;
        query = query.Trim();
        if (query.Length < 2)
            return;
        await Task.Delay(200);
        if (id != _fileSearch)
            return;
        var paths = await Task.Run(() => FileSearch.Find(query, 8));
        if (id != _fileSearch || paths.Count == 0)
            return;
        Results = [.. _results, .. paths.Select(StartMenuService.FileResult).OfType<StartApp>()];
        Notify(nameof(NoResults));
    }

    public bool NoResults => IsSearching && _results.Count == 0;

    public bool ShowAllApps
    {
        get => _showAllApps;
        set { _showAllApps = value; Notify(); NotifyViews(); }
    }

    // Qué sección se ve (estilo Windows 11): búsqueda > todas las apps > inicio.
    public bool HomeVisible => !IsSearching && !_showAllApps;
    public bool AllAppsVisible => !IsSearching && _showAllApps;

    private void NotifyViews()
    {
        Notify(nameof(HomeVisible));
        Notify(nameof(AllAppsVisible));
    }

    /// <summary>Enter en el buscador: abre el resultado elegido o ejecuta el texto como comando.</summary>
    public void Submit()
    {
        if (_selected != null)
            StartCommands.Launch.Execute(_selected);
        else if (IsSearching && StartMenuService.RunCommand(_query))
            FlyoutManager.CloseAll();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Acciones del menú, accesibles desde cualquier plantilla (incluidos menús contextuales).</summary>
public static class StartCommands
{
    public static ICommand Launch { get; } = new Relay(p =>
    {
        (p as StartApp)?.Launch();
        FlyoutManager.CloseAll();
    });

    public static ICommand OpenRecent { get; } = new Relay(p =>
    {
        (p as RecentItem)?.Open();
        FlyoutManager.CloseAll();
    });

    public static ICommand TogglePin { get; } = new Relay(p =>
    {
        if (p is StartApp app)
            StartMenuService.SetPinned(app, !app.IsPinned);
    });

    public static ICommand OpenLocation { get; } = new Relay(p =>
    {
        FlyoutManager.CloseAll();
        if (p is StartApp app)
            AppActions.OpenLocation(app);
    });

    public static ICommand OpenPrivateWindow { get; } = new Relay(p =>
    {
        FlyoutManager.CloseAll();
        if (p is StartApp app)
            AppActions.OpenPrivateWindow(app);
    });

    public static ICommand RunAsAdmin { get; } = new Relay(p =>
    {
        FlyoutManager.CloseAll();
        if (p is StartApp app)
            AppActions.RunAsAdmin(app);
    });

    public static ICommand Uninstall { get; } = new Relay(async p =>
    {
        FlyoutManager.CloseAll();
        if (p is StartApp app)
            await AppActions.UninstallAsync(app);
    });

    public static ICommand ToggleTaskbarPin { get; } = new Relay(p =>
    {
        if (p is not StartApp app)
            return;
        if (app.IsPinnedToTaskbar)
            TaskbarPins.Unpin(app.Id);
        else
            TaskbarPins.Pin(app.Id);
    });

    /// <summary>Elige una imagen (o el ícono de un .exe, .dll o .ico) para la app.</summary>
    public static ICommand ChangeIcon { get; } = new Relay(async p =>
    {
        if (p is not StartApp app)
            return;
        // El menú se cierra antes: el diálogo le quitaría el foco.
        FlyoutManager.CloseAll();
        var files = await FilePicker.PickFilesAsync(Loc.F("Ícono para {0}", app.Name),
                                                    Loc.T("Imágenes e íconos") + "|" + CustomIcons.DialogFilter);
        if (files.Length > 0 && !CustomIcons.Set(app.Id, files[0]))
            await Strata.Shell.Controls.StrataDialog.ShowAsync(null, Loc.T("Cambiar ícono…"),
                Loc.T("No se pudo leer una imagen de ese archivo."),
                Loc.T("Cerrar"));
    });

    public static ICommand ResetIcon { get; } = new Relay(p =>
    {
        if (p is StartApp app)
            CustomIcons.Reset(app.Id);
    });

    public static ICommand PinFile { get; } = new Relay(async _ =>
    {
        FlyoutManager.CloseAll();
        // Un acceso directo se ancla tal cual, con sus argumentos.
        StartMenuService.PinPaths(await FilePicker.PickFilesAsync(Loc.T("Anclar a Inicio"), multiselect: true, keepLinks: true));
    });

    public static ICommand PinFolder { get; } = new Relay(async _ =>
    {
        FlyoutManager.CloseAll();
        StartMenuService.PinPaths(await FilePicker.PickFoldersAsync(Loc.T("Anclar a Inicio"), multiselect: true));
    });

    public static ICommand OpenFolder { get; } = new Relay(p =>
    {
        switch (p as string)
        {
            case "Documents": StartMenuService.OpenFolder(Environment.SpecialFolder.MyDocuments); break;
            case "Downloads": StartMenuService.OpenDownloads(); break;
            case "Pictures": StartMenuService.OpenFolder(Environment.SpecialFolder.MyPictures); break;
            case "Music": StartMenuService.OpenFolder(Environment.SpecialFolder.MyMusic); break;
            case "Videos": StartMenuService.OpenFolder(Environment.SpecialFolder.MyVideos); break;
            case "User": StartMenuService.OpenFolder(Environment.SpecialFolder.UserProfile); break;
        }
        FlyoutManager.CloseAll();
    });

    public static ICommand OpenHub { get; } = new Relay(_ =>
    {
        FlyoutManager.CloseAll();
        App.Current.OpenHub();
    });

    public static ICommand Power { get; } = new Relay(p =>
    {
        FlyoutManager.CloseAll();
        switch (p as string)
        {
            case "Lock": StartMenuService.Lock(); break;
            case "SignOut": StartMenuService.SignOut(); break;
            case "Sleep": StartMenuService.Sleep(); break;
            case "Restart": StartMenuService.Restart(); break;
            case "PowerOff": StartMenuService.PowerOff(); break;
            case "UpdateRestart": StartMenuService.UpdateAndRestart(); break;
            case "UpdatePowerOff": StartMenuService.UpdateAndPowerOff(); break;
        }
    });

    private sealed class Relay(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
    }
}

/// <summary>Abre el estilo de menú elegido en el Hub.</summary>
public static class StartMenuLauncher
{
    public static void Toggle(FrameworkElement anchor)
    {
        switch (Settings.Current.StartStyle)
        {
            case StartMenuStyle.List: FlyoutManager.Toggle<StartMenuList>(anchor); break;
            case StartMenuStyle.Fullscreen: FlyoutManager.Toggle<StartMenuFullscreen>(anchor); break;
            case StartMenuStyle.Compact: FlyoutManager.Toggle<StartMenuCompact>(anchor); break;
            case StartMenuStyle.Spotlight: FlyoutManager.Toggle<StartMenuSpotlight>(anchor); break;
            case StartMenuStyle.Icons: FlyoutManager.Toggle<StartMenuIcons>(anchor); break;
            case StartMenuStyle.Profile: FlyoutManager.Toggle<StartMenuProfile>(anchor); break;
            default: FlyoutManager.Toggle<StartMenuWin11>(anchor); break;
        }
    }

    private static bool _rewarming;

    /// <summary>
    /// Arma una vez el estilo elegido fuera de la pantalla (ver <see cref="FlyoutWindow.WarmUp"/>): la
    /// primera apertura después de iniciar tardaba casi un segundo en verse.
    /// </summary>
    public static void WarmUp()
    {
        if (!_rewarming)
        {
            // El preparado se descarta si cambian los ajustes o el tema (al arrancar, el acento del
            // fondo): se arma otro unos segundos después, sin nada abierto, para que siga listo.
            _rewarming = true;
            var later = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromSeconds(5),
            };
            later.Tick += (_, _) =>
            {
                later.Stop();
                if (FlyoutManager.HasKeptOrOpen)
                    return;
                WarmUp();
            };
            FlyoutManager.KeptDropped += () =>
            {
                later.Stop();
                later.Start();
            };
        }
        try
        {
            FlyoutWindow menu = Settings.Current.StartStyle switch
            {
                StartMenuStyle.List => new StartMenuList(),
                StartMenuStyle.Fullscreen => new StartMenuFullscreen(),
                StartMenuStyle.Compact => new StartMenuCompact(),
                StartMenuStyle.Spotlight => new StartMenuSpotlight(),
                StartMenuStyle.Icons => new StartMenuIcons(),
                StartMenuStyle.Profile => new StartMenuProfile(),
                _ => new StartMenuWin11(),
            };
            menu.WarmUp();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo preparar el menú Inicio", ex);
        }
    }
}
