using System.Windows;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Files;

/// <summary>Lo que el módulo Archivos le ofrece al resto de Strata UI (<see cref="IFilesModule"/>).</summary>
public sealed class FilesEntry : IFilesModule
{
    public Window CreateWindow(string? path) => new FilesWindow(path);

    public void OpenMedia(IReadOnlyList<string> items, int index) => MediaViewerWindow.Open(items, index);
}
