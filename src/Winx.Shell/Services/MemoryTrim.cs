using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Winx.Shell.Services;

/// <summary>
/// Devuelve a Windows la memoria que WINX ya no usa: después de arrancar (íconos, catálogo de
/// apps) y al cerrar paneles o el Hub, que crean muchos objetos de una vez. Se agrupan las
/// peticiones y se hace en reposo, para no notarse.
/// </summary>
public static class MemoryTrim
{
    private static DispatcherTimer? _timer;

    /// <summary>Programa una limpieza dentro de <paramref name="delay"/> (si ya había una, se pospone).</summary>
    public static void Schedule(TimeSpan delay)
    {
        _timer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        Run();
    }

    private static void Run()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            // Saca del conjunto de trabajo las páginas que no se están usando; vuelven solas si hacen falta.
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(process.Handle, -1, -1);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo liberar memoria", ex);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
