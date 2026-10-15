using Wpf.Ui.Controls;

namespace Strata.Shell.Hub;

/// <summary>Página que un módulo agrega a la navegación del Hub.</summary>
public sealed record HubModulePage(string Title, SymbolRegular Icon, Type PageType);

/// <summary>
/// Lo que expone un módulo descargable (ver <see cref="Services.OptimizationModule"/>): la app busca
/// en el ensamblado una clase pública con constructor sin parámetros que implemente esto.
/// </summary>
public interface IHubModule
{
    /// <summary>Páginas en el orden en que van en la navegación.</summary>
    IReadOnlyList<HubModulePage> Pages { get; }
}
