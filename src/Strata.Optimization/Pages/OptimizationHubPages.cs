using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell.Hub.Pages;

/// <summary>Velocidad: respuesta del sistema, memoria, juegos y el controlador de la gráfica.</summary>
public sealed class SpeedHubPage() : TabbedPage(SymbolRegular.Flash24, Loc.T("Rendimiento"),
    Loc.T("Que Windows responda más rápido y deje más recursos para lo que estás usando."), screenPicker: false, Tabs, OptimizationParent.Value)
{
    internal static readonly IReadOnlyList<Tab> Tabs =
    [
        new(Loc.T("General"), SymbolRegular.Flash24, typeof(PerformancePage)),
        new(Loc.T("Memoria RAM"), SymbolRegular.Ram20, typeof(MemoryPage)),
        new(Loc.T("Juegos"), SymbolRegular.Games24, typeof(GamingPage)),
        new(Loc.T("Hardware"), SymbolRegular.DeveloperBoard24, typeof(HardwarePage)),
    ];
}

/// <summary>Espacio en disco y lo que viene instalado de más.</summary>
public sealed class CleanupHubPage() : TabbedPage(SymbolRegular.Broom24, Loc.T("Limpieza y apps"),
    Loc.T("Archivos que sobran, apps preinstaladas y extras de los navegadores."), screenPicker: false, Tabs, OptimizationParent.Value)
{
    internal static readonly IReadOnlyList<Tab> Tabs =
    [
        new(Loc.T("Limpieza"), SymbolRegular.Broom24, typeof(CleanupPage)),
        new(Loc.T("Apps preinstaladas"), SymbolRegular.Delete24, typeof(AppsPage)),
        new(Loc.T("Navegadores"), SymbolRegular.Globe24, typeof(BrowsersPage)),
    ];
}

/// <summary>Lo que Windows envía, sugiere o decide por su cuenta.</summary>
public sealed class PrivacyHubPage() : TabbedPage(SymbolRegular.ShieldLock24, Loc.T("Privacidad"),
    Loc.T("Telemetría, anuncios, funciones de IA y actualizaciones automáticas."), screenPicker: false, Tabs, OptimizationParent.Value)
{
    internal static readonly IReadOnlyList<Tab> Tabs =
    [
        new(Loc.T("Privacidad"), SymbolRegular.ShieldLock24, typeof(PrivacyPage)),
        new(Loc.T("Inteligencia artificial"), SymbolRegular.BrainCircuit24, typeof(AIPage)),
        new("Windows Update", SymbolRegular.ArrowSync24, typeof(UpdatePage)),
    ];
}

/// <summary>Cómo se ve y se comporta Windows (no Strata UI).</summary>
public sealed class WindowsHubPage() : TabbedPage(SymbolRegular.Desktop24, Loc.T("Personalizar Windows"),
    Loc.T("Comportamiento del sistema, su aspecto, el Explorador y la multitarea."), screenPicker: false, Tabs, OptimizationParent.Value)
{
    internal static readonly IReadOnlyList<Tab> Tabs =
    [
        new(Loc.T("Sistema"), SymbolRegular.Desktop24, typeof(SystemTweaksPage)),
        new(Loc.T("Apariencia de Windows"), SymbolRegular.DarkTheme24, typeof(WindowsAppearancePage)),
        new(Loc.T("Explorador"), SymbolRegular.Folder24, typeof(ExplorerPage)),
        new(Loc.T("Multitarea"), SymbolRegular.SquareMultiple24, typeof(MultitaskingPage)),
    ];
}

/// <summary>A dónde vuelven las categorías: la página de Optimización.</summary>
internal static class OptimizationParent
{
    public static (string Page, string Title) Value => ("Optimization", Loc.T("Optimización"));
}
