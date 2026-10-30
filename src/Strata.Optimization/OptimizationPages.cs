using Strata.Shell.Hub;
using Strata.Shell.Hub.Pages;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell;

/// <summary>
/// Optimización en el Hub: una sola entrada (<see cref="OptimizationPage"/>) con sus categorías, y
/// cada categoría con sus páginas en pestañas.
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
        new(Loc.T("Optimización"), SymbolRegular.Flash24, typeof(OptimizationPage)),
    ];
}
