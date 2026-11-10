using System.Windows;
using Strata.Shell.Flyouts;
using Strata.Shell.Services.Modules;

namespace Strata.Shell.Lyrics;

/// <summary>
/// Lo que la app usa del módulo Letras. Al cargarse empieza a seguir lo que suena: busca la letra de
/// cada canción y va marcando la línea que se canta (Services/Media/LyricsLine).
/// </summary>
public sealed class LyricsModuleEntry : ILyricsModule
{
    public LyricsModuleEntry() => LyricsEngine.Start();

    public void Toggle(FrameworkElement anchor) => FlyoutManager.Toggle<LyricsFlyout>(anchor);
}
