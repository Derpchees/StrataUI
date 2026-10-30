using Strata.Shell.Hub;
using Strata.Shell.Hub.Pages;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell;

/// <summary>
/// Las entradas de Optimización en el Hub, en su orden. Cada una junta en pestañas las páginas de un
/// mismo tema (antes eran quince entradas sueltas).
/// </summary>
public sealed class OptimizationPages : IHubModule
{
    public OptimizationPages()
    {
        TabbedPage.RegisterHost(typeof(SpeedHubPage), Loc.T("Rendimiento"), SpeedHubPage.Tabs);
        TabbedPage.RegisterHost(typeof(CleanupHubPage), Loc.T("Limpieza y apps"), CleanupHubPage.Tabs);
        TabbedPage.RegisterHost(typeof(PrivacyHubPage), Loc.T("Privacidad"), PrivacyHubPage.Tabs);
        TabbedPage.RegisterHost(typeof(WindowsHubPage), Loc.T("Personalizar Windows"), WindowsHubPage.Tabs);
    }

    public IReadOnlyList<HubModulePage> Pages { get; } =
    [
        new(Loc.T("Rendimiento"), SymbolRegular.Flash24, typeof(SpeedHubPage)),
        new(Loc.T("Limpieza y apps"), SymbolRegular.Broom24, typeof(CleanupHubPage)),
        new(Loc.T("Privacidad"), SymbolRegular.ShieldLock24, typeof(PrivacyHubPage)),
        new(Loc.T("Personalizar Windows"), SymbolRegular.Desktop24, typeof(WindowsHubPage)),
        new(Loc.T("Respaldo"), SymbolRegular.History24, typeof(BackupPage)),
    ];
}
