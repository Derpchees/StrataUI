using Strata.Shell.Hub;
using Strata.Shell.Hub.Pages;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell;

/// <summary>Las páginas del grupo Optimización del Hub, en su orden.</summary>
public sealed class OptimizationPages : IHubModule
{
    public IReadOnlyList<HubModulePage> Pages { get; } =
    [
        new(Loc.T("Rendimiento"), SymbolRegular.Flash24, typeof(PerformancePage)),
        new(Loc.T("Memoria RAM"), SymbolRegular.Ram20, typeof(MemoryPage)),
        new(Loc.T("Hardware"), SymbolRegular.DeveloperBoard24, typeof(HardwarePage)),
        new(Loc.T("Limpieza"), SymbolRegular.Broom24, typeof(CleanupPage)),
        new(Loc.T("Apps preinstaladas"), SymbolRegular.Delete24, typeof(AppsPage)),
        new(Loc.T("Privacidad"), SymbolRegular.ShieldLock24, typeof(PrivacyPage)),
        new(Loc.T("Inteligencia artificial"), SymbolRegular.BrainCircuit24, typeof(AIPage)),
        new(Loc.T("Sistema"), SymbolRegular.Desktop24, typeof(SystemTweaksPage)),
        new("Windows Update", SymbolRegular.ArrowSync24, typeof(UpdatePage)),
        new(Loc.T("Apariencia de Windows"), SymbolRegular.DarkTheme24, typeof(WindowsAppearancePage)),
        new(Loc.T("Explorador"), SymbolRegular.Folder24, typeof(ExplorerPage)),
        new(Loc.T("Multitarea"), SymbolRegular.SquareMultiple24, typeof(MultitaskingPage)),
        new(Loc.T("Juegos"), SymbolRegular.Games24, typeof(GamingPage)),
        new(Loc.T("Navegadores"), SymbolRegular.Globe24, typeof(BrowsersPage)),
        new(Loc.T("Respaldo"), SymbolRegular.History24, typeof(BackupPage)),
    ];
}
