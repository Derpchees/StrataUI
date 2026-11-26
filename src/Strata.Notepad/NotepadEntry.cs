using System.Windows;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Notepad;

/// <summary>Lo que la app usa del módulo Bloc de notas: una sola ventana con pestañas.</summary>
public sealed class NotepadEntry : INotepadModule
{
    public Window Open(IReadOnlyList<string> paths) => NotepadWindow.Open(paths);
}
