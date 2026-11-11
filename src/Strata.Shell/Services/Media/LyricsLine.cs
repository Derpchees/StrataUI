namespace Strata.Shell.Services.Media;

/// <summary>
/// La línea de la letra que se canta ahora. La pone el módulo Letras (si está instalado) y la barra
/// la muestra en su botón, alargado, cuando se activa «Letra en la barra».
/// </summary>
public static class LyricsLine
{
    /// <summary>La línea actual, o vacío si no hay letra sincronizada o no se canta nada.</summary>
    public static string Current { get; private set; } = "";

    /// <summary>Cambió la línea (en el hilo de la UI).</summary>
    public static event Action? Changed;

    /// <summary>Lo llama el módulo Letras desde el hilo de la UI.</summary>
    public static void Set(string? line)
    {
        line ??= "";
        if (line == Current)
            return;
        Current = line;
        Changed?.Invoke();
    }
}
