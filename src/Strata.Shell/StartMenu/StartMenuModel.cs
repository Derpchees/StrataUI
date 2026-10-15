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
        Recent = Settings.Current.StartShowRecent ? StartMenuService.GetRecent(8) : [];
    }

    public ObservableCollection<StartApp> Pinned => StartMenuService.Pinned;
    public ICollectionView AllApps { get; }
    public IReadOnlyList<RecentItem> Recent { get; }
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
        private set { _results = value; Notify(); }
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

    /// <summary>Enter en el buscador: abre el primer resultado o ejecuta el texto como comando.</summary>
    public void Submit()
    {
        if (_results.Count > 0)
            StartCommands.Launch.Execute(_results[0]);
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
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.T("Cambiar ícono…"),
                Content = Loc.T("No se pudo leer una imagen de ese archivo."),
                CloseButtonText = Loc.T("Cerrar"),
            }.ShowDialogAsync();
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
}
