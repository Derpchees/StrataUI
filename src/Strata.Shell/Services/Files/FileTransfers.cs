using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files;

public enum TransferKind { Copy, Move, Extract, Compress, Format }

/// <summary>Qué hacer con un nombre que ya está en el destino (se decide antes de empezar).</summary>
public enum TransferConflict
{
    /// <summary>Sobrescribir (las carpetas se combinan).</summary>
    Replace,
    /// <summary>"nombre (2)".</summary>
    KeepBoth,
    /// <summary>Pegar en la misma carpeta: "nombre - copia".</summary>
    Duplicate,
}

public enum TransferState { Preparing, Running, Done, Canceled, Failed }

/// <summary>
/// Una tarea con progreso que se muestra dentro del explorador (sin ventanas de Windows): copiar,
/// mover, extraer, comprimir o formatear. La actualiza el hilo de trabajo (o el proceso aparte que
/// la hace: el elevado, el de los comprimidos); las propiedades se avisan desde la UI cada tanto.
/// </summary>
public sealed class FileTransfer : INotifyPropertyChanged
{
    internal FileTransfer(TransferKind kind, IReadOnlyList<string> sources, string folder, TransferConflict conflict)
    {
        Kind = kind;
        Sources = sources;
        Folder = folder;
        Conflict = conflict;
    }

    public TransferKind Kind { get; }
    public IReadOnlyList<string> Sources { get; }
    /// <summary>La carpeta destino; al comprimir, el archivo que se crea; al formatear, la unidad.</summary>
    public string Folder { get; }
    internal TransferConflict Conflict { get; }
    internal readonly CancellationTokenSource Cancellation = new();
    internal readonly Stopwatch Clock = new();
    internal readonly TaskCompletionSource<bool> Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Destino exacto de cada origen (al reintentar como administrador: el mismo que la primera vez).</summary>
    internal Dictionary<string, string>? Targets { get; init; }
    /// <summary>Dónde quedó (o iba a quedar) cada origen; con esto se reintenta sin duplicar.</summary>
    internal readonly Dictionary<string, string> Resolved = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Orígenes con algún error (solo esos se reintentan).</summary>
    internal readonly HashSet<string> FailedSources = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Lo hace un proceso con permiso de administrador (no se puede volver a pedir).</summary>
    public bool Elevated { get; init; }
    /// <summary>La carpeta o el archivo que quedó al terminar (para seleccionarlo).</summary>
    public string? Result { get; internal set; }

    // Escritas por el hilo de trabajo.
    internal long TotalBytes, DoneBytes;
    internal int TotalFiles, DoneFiles;
    internal volatile string CurrentName = "";
    internal volatile TransferState StateValue = TransferState.Preparing;
    internal readonly List<string> Errors = new();
    internal volatile bool NeedsAdmin;
    private double _speed;
    private long _lastBytes;
    private TimeSpan _lastTime;

    public TransferState State => StateValue;
    public bool IsActive => StateValue is TransferState.Preparing or TransferState.Running;
    public bool HasErrors => StateValue == TransferState.Failed;
    public bool CanRetryAsAdmin => HasErrors && NeedsAdmin && !Elevated && Kind is TransferKind.Copy or TransferKind.Move;
    public double Progress => TotalBytes > 0 ? Math.Clamp((double)Interlocked.Read(ref DoneBytes) / TotalBytes, 0, 1)
                              : TotalFiles > 0 ? (double)DoneFiles / TotalFiles : 0;
    public bool IsIndeterminate => StateValue == TransferState.Preparing || (IsActive && Kind == TransferKind.Format);

    public Wpf.Ui.Controls.SymbolRegular Symbol => StateValue switch
    {
        TransferState.Done => Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24,
        TransferState.Failed => Wpf.Ui.Controls.SymbolRegular.ErrorCircle24,
        TransferState.Canceled => Wpf.Ui.Controls.SymbolRegular.DismissCircle24,
        _ => Kind switch
        {
            TransferKind.Copy => Wpf.Ui.Controls.SymbolRegular.Copy24,
            TransferKind.Extract or TransferKind.Compress => Wpf.Ui.Controls.SymbolRegular.FolderZip24,
            TransferKind.Format => Wpf.Ui.Controls.SymbolRegular.HardDrive20,
            _ => Wpf.Ui.Controls.SymbolRegular.FolderArrowRight24,
        },
    };

    private string What => Sources.Count == 1
        ? Loc.F("«{0}»", Path.GetFileName(Sources[0].TrimEnd('\\'))) // las comillas de cada idioma
        : Loc.F("{0} elementos", Sources.Count);

    private string FolderName => Path.GetFileName(Folder.TrimEnd('\\')) is { Length: > 0 } name ? name : Folder;

    public string Title => (Kind, StateValue) switch
    {
        (TransferKind.Copy, TransferState.Done) => Loc.F("Se copió {0} a {1}", What, FolderName),
        (TransferKind.Move, TransferState.Done) => Loc.F("Se movió {0} a {1}", What, FolderName),
        (TransferKind.Extract, TransferState.Done) => Loc.F("Se extrajo {0} en {1}", What, FolderName),
        (TransferKind.Compress, TransferState.Done) => Loc.F("Se creó {0}", FolderName),
        (TransferKind.Format, TransferState.Done) => Loc.F("Se formateó {0}", Folder),
        (_, TransferState.Canceled) => Loc.T("Cancelado"),
        (TransferKind.Copy, TransferState.Failed) => Loc.F("No se pudo copiar todo a {0}", FolderName),
        (TransferKind.Move, TransferState.Failed) => Loc.F("No se pudo mover todo a {0}", FolderName),
        (TransferKind.Extract, TransferState.Failed) => Loc.F("No se pudo extraer todo en {0}", FolderName),
        (TransferKind.Compress, TransferState.Failed) => Loc.F("No se pudo crear {0}", FolderName),
        (TransferKind.Format, TransferState.Failed) => Loc.F("No se pudo formatear {0}", Folder),
        (TransferKind.Copy, _) => Loc.F("Copiando {0} a {1}", What, FolderName),
        (TransferKind.Extract, _) => Loc.F("Extrayendo {0} en {1}", What, FolderName),
        (TransferKind.Compress, _) => Loc.F("Comprimiendo {0} en {1}", What, FolderName),
        (TransferKind.Format, _) => Loc.F("Formateando {0}", Folder),
        _ => Loc.F("Moviendo {0} a {1}", What, FolderName),
    };

    public string Detail
    {
        get
        {
            switch (StateValue)
            {
                case TransferState.Preparing when Kind == TransferKind.Format:
                case TransferState.Running when Kind == TransferKind.Format:
                    return Loc.T("Esto puede tardar unos minutos.");
                case TransferState.Preparing:
                    return Loc.T("Calculando…");
                case TransferState.Failed:
                    lock (Errors)
                        return Errors.Count == 0 ? "" : Errors.Count == 1 ? Errors[0] : Loc.F("{0} elementos con errores. {1}", Errors.Count, Errors[0]);
                case TransferState.Done or TransferState.Canceled:
                    return "";
            }
            long done = Interlocked.Read(ref DoneBytes);
            string text = Loc.F("{0} de {1}", FileEntry.FormatSize(done), FileEntry.FormatSize(TotalBytes));
            if (_speed > 1024)
            {
                text += " · " + Loc.F("{0}/s", FileEntry.FormatSize((long)_speed));
                double seconds = (TotalBytes - done) / _speed;
                if (seconds >= 1)
                    text += " · " + (seconds < 60 ? Loc.F("quedan {0} s", (int)Math.Ceiling(seconds))
                                     : Loc.F("quedan {0} min", (int)Math.Ceiling(seconds / 60)));
            }
            return text;
        }
    }

    /// <summary>Lo que se está copiando ahora (debajo del detalle).</summary>
    public string Current => IsActive ? CurrentName : "";

    public void Cancel() => Cancellation.Cancel();

    /// <summary>Vuelve a avisar las propiedades (desde la UI) y recalcula la velocidad.</summary>
    internal void Refresh()
    {
        var now = Clock.Elapsed;
        long bytes = Interlocked.Read(ref DoneBytes);
        double elapsed = (now - _lastTime).TotalSeconds;
        if (elapsed >= 0.4)
        {
            double instant = (bytes - _lastBytes) / elapsed;
            // Promedio suave: la velocidad de un disco salta mucho de un momento a otro.
            _speed = _speed <= 0 ? instant : _speed * 0.7 + instant * 0.3;
            _lastBytes = bytes;
            _lastTime = now;
        }
        foreach (var name in new[] { nameof(Title), nameof(Detail), nameof(Current), nameof(Progress), nameof(IsIndeterminate),
                                     nameof(IsActive), nameof(HasErrors), nameof(CanRetryAsAdmin), nameof(State), nameof(Symbol) })
            Notify(name);
    }

    internal void AddError(string message, bool admin, string? path = null)
    {
        lock (Errors)
        {
            Errors.Add(message);
            if (path != null && Sources.FirstOrDefault(s => FileTransfers.IsInside(path, s)) is { } source)
                FailedSources.Add(source);
        }
        if (admin)
            NeedsAdmin = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Copiar y mover con progreso propio (CopyFileEx), que el explorador muestra en su ventana. Mover
/// dentro de la misma unidad es cambiar de lugar (instantáneo); entre unidades, copiar y borrar.
/// Lo que falla por permisos se reintenta en un proceso con permiso de administrador
/// (<see cref="ElevatedFiles"/>), con la misma tarjeta de progreso.
/// </summary>
public static class FileTransfers
{
    /// <summary>Las de esta sesión que siguen a la vista (las terminadas bien se quitan solas).</summary>
    public static ObservableCollection<FileTransfer> Active { get; } = new();

    /// <summary>Ventanas que muestran las transferencias (las de Archivos). Sin ninguna, el resultado se avisa con una notificación.</summary>
    public static int Viewers { get; set; }

    private static DispatcherTimer? _refresh;

    /// <summary>Copia o mueve y devuelve si todo salió bien.</summary>
    internal static Task<bool> Start(TransferKind kind, IReadOnlyList<string> sources, string folder, TransferConflict conflict) =>
        Begin(new FileTransfer(kind, sources, folder, conflict)).Finished.Task;

    /// <summary>Empieza a copiar o mover con el motor propio, en este proceso.</summary>
    internal static FileTransfer Begin(FileTransfer transfer) => Run(transfer, t =>
    {
        Execute(t);
        return Task.CompletedTask;
    });

    /// <summary>
    /// Muestra la tarea en la lista y la hace en otro hilo con <paramref name="work"/> (que actualiza
    /// su progreso y deja su estado final); al terminar avisa y la quita si salió bien.
    /// </summary>
    internal static FileTransfer Run(FileTransfer transfer, Func<FileTransfer, Task> work)
    {
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => Run(transfer, work));
        Active.Add(transfer);
        if (Application.Current != null)
        {
            if (_refresh is null)
            {
                _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
                _refresh.Tick += (_, _) =>
                {
                    foreach (var t in Active)
                        t.Refresh();
                    if (!Active.Any(t => t.IsActive))
                        _refresh.Stop();
                };
            }
            _refresh.Start();
        }
        transfer.Clock.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                await work(transfer);
            }
            catch (OperationCanceledException)
            {
                transfer.StateValue = TransferState.Canceled;
            }
            catch (Exception ex)
            {
                Log.Error($"Archivos: falló la tarea {transfer.Kind} a {transfer.Folder}", ex);
                transfer.AddError(ex.Message, admin: false);
                transfer.StateValue = TransferState.Failed;
            }
            if (transfer.IsActive)
                transfer.StateValue = transfer.Errors.Count > 0 ? TransferState.Failed : TransferState.Done;
            if (Application.Current is { } current)
                _ = current.Dispatcher.BeginInvoke(() => Complete(transfer));
            else
                transfer.Finished.TrySetResult(transfer.State == TransferState.Done); // sin la app (pruebas)
        });
        return transfer;
    }

    /// <summary>Quita una terminada de la lista (la X de su tarjeta).</summary>
    public static void Dismiss(FileTransfer transfer)
    {
        if (!transfer.IsActive)
            Active.Remove(transfer);
    }

    /// <summary>
    /// Lo que falló, otra vez con permiso de administrador (pregunta antes, con el tema de Strata UI):
    /// solo los orígenes con errores y al mismo destino que la primera vez (lo ya copiado se reemplaza
    /// por lo mismo, sin "nombre (2)").
    /// </summary>
    public static async Task<bool> RetryAsAdminAsync(FileTransfer transfer, IntPtr owner, bool ask = true)
    {
        if (ask && !await ElevatedFiles.AskAsync(owner, transfer.Kind == TransferKind.Copy
                ? Loc.F("Para copiar a «{0}» hace falta permiso de administrador.", FolderNameOf(transfer.Folder))
                : Loc.F("Para mover a «{0}» hace falta permiso de administrador.", FolderNameOf(transfer.Folder))))
            return false;
        Active.Remove(transfer);
        List<string> sources;
        lock (transfer.Errors)
            sources = transfer.FailedSources.Count > 0 ? transfer.Sources.Where(transfer.FailedSources.Contains).ToList() : transfer.Sources.ToList();
        var targets = sources.Where(transfer.Resolved.ContainsKey).ToDictionary(s => s, s => transfer.Resolved[s], StringComparer.OrdinalIgnoreCase);
        var retry = new FileTransfer(transfer.Kind, sources, transfer.Folder, TransferConflict.Replace) { Targets = targets, Elevated = true };
        return await ElevatedFiles.StartTransfer(retry).Finished.Task;
    }

    internal static string FolderNameOf(string folder) => Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;

    private sealed record Job(string Source, string Target, long Size);

    /// <summary>Copia o mueve en este hilo (también lo usa el proceso elevado, sin la interfaz).</summary>
    internal static void Execute(FileTransfer transfer)
    {
        var token = transfer.Cancellation.Token;
        var jobs = new List<Job>();
        var folders = new List<string>();      // carpetas a crear (también las vacías)
        var emptied = new List<string>();      // al mover entre unidades: carpetas de origen a borrar al final
        try
        {
            foreach (string source in transfer.Sources)
            {
                token.ThrowIfCancellationRequested();
                string clean = source.TrimEnd('\\');
                string name = Path.GetFileName(clean);
                if (name.Length == 0)
                    continue;
                if (!File.Exists(clean) && !Directory.Exists(clean))
                {
                    transfer.AddError(Loc.F("«{0}» ya no está en su carpeta.", name), admin: false);
                    continue;
                }
                // Lo que falla al leer un elemento (sin permiso, se desconectó) no frena a los demás.
                try
                {
                    Plan(transfer, clean, name, jobs, folders, emptied, token);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    transfer.AddError($"{name}: {ex.Message}", ex is not IOException, clean);
                }
            }
            transfer.TotalFiles = jobs.Count;
            transfer.TotalBytes = jobs.Where(j => j.Size > 0).Sum(j => j.Size);
            transfer.StateValue = TransferState.Running;

            foreach (var folder in folders)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    transfer.AddError($"{Path.GetFileName(folder)}: {ex.Message}", ex is UnauthorizedAccessException,
                                      transfer.Resolved.FirstOrDefault(r => IsInside(folder, r.Value)).Key);
                }
            }
            long completed = 0;
            foreach (var job in jobs)
            {
                token.ThrowIfCancellationRequested();
                transfer.CurrentName = Path.GetFileName(job.Source);
                if (job.Size < 0)
                    MoveWhole(transfer, job);
                else if (CopyOne(transfer, job, completed, token) && transfer.Kind == TransferKind.Move)
                    DeleteSource(transfer, job.Source);
                if (job.Size > 0)
                    completed += job.Size;
                Interlocked.Exchange(ref transfer.DoneBytes, completed);
                transfer.DoneFiles++;
            }
            // Las carpetas de origen ya vacías (si algo falló, quedan con lo que no se movió).
            foreach (var folder in emptied)
                DeleteEmptyFolders(folder);
            lock (transfer.Errors)
                transfer.StateValue = transfer.Errors.Count > 0 ? TransferState.Failed : TransferState.Done;
        }
        catch (OperationCanceledException)
        {
            transfer.StateValue = TransferState.Canceled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            transfer.AddError(ex.Message, ex is UnauthorizedAccessException or System.Security.SecurityException);
            transfer.StateValue = TransferState.Failed;
        }
        Log.Info($"Archivos: {transfer.Kind} de {transfer.Sources.Count} elemento(s) a {transfer.Folder}: {transfer.StateValue}"
                 + $" ({transfer.DoneFiles}/{transfer.TotalFiles} archivos, {transfer.Clock.ElapsedMilliseconds} ms"
                 + (transfer.Elevated ? ", como administrador" : "")
                 + (transfer.Errors.Count > 0 ? $", {transfer.Errors.Count} errores: {transfer.Errors[0]}" : "") + ")");
    }

    /// <summary>Qué archivos y carpetas hacen falta para un elemento de origen.</summary>
    private static void Plan(FileTransfer transfer, string clean, string name, List<Job> jobs, List<string> folders,
                             List<string> emptied, CancellationToken token)
    {
        bool isFolder = Directory.Exists(clean);
        string target = TargetFor(transfer, clean, name, isFolder);
        transfer.Resolved[clean] = target;
        if (isFolder && IsInside(transfer.Folder, clean))
        {
            transfer.AddError(Loc.F("«{0}» no se puede poner dentro de sí misma.", name), admin: false);
            return;
        }
        transfer.CurrentName = name;
        // Misma unidad y sin nada que combinar: se cambia de lugar de una vez.
        if (transfer.Kind == TransferKind.Move && SameVolume(clean, transfer.Folder)
            && !(isFolder && Directory.Exists(target)))
        {
            jobs.Add(new Job(clean, target, -1));
            return;
        }
        if (!isFolder)
        {
            jobs.Add(new Job(clean, target, new FileInfo(clean).Length));
            return;
        }
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint, // los enlaces de carpeta podrían dar vueltas
        };
        // Se lee todo antes de agregar nada: si falla a medias, ese elemento no se copia en parte.
        var subfolders = new DirectoryInfo(clean).EnumerateDirectories("*", options)
            .Select(dir => Path.Combine(target, Path.GetRelativePath(clean, dir.FullName))).ToList();
        var files = new List<Job>();
        foreach (var file in new DirectoryInfo(clean).EnumerateFiles("*", options))
        {
            token.ThrowIfCancellationRequested();
            files.Add(new Job(file.FullName, Path.Combine(target, Path.GetRelativePath(clean, file.FullName)), file.Length));
        }
        folders.Add(target);
        folders.AddRange(subfolders);
        jobs.AddRange(files);
        if (transfer.Kind == TransferKind.Move)
            emptied.Add(clean);
    }

    private static void Complete(FileTransfer transfer)
    {
        transfer.Refresh();
        transfer.Finished.TrySetResult(transfer.State == TransferState.Done);
        // Desde el escritorio, sin Archivos abierto: lo largo o lo que falló se avisa.
        if (Viewers == 0 && (transfer.State == TransferState.Failed || transfer.Clock.Elapsed > TimeSpan.FromSeconds(3)))
            Notifications.NotificationService.ShowLocal(transfer.Title, transfer.Detail);
        if (transfer.State is TransferState.Done or TransferState.Canceled)
        {
            // Bien terminada: se ve un momento y se va.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(transfer.State == TransferState.Done ? 4 : 2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Active.Remove(transfer);
            };
            timer.Start();
        }
    }

    /// <summary>Dónde queda cada elemento según lo que se decidió para los nombres repetidos.</summary>
    private static string TargetFor(FileTransfer transfer, string source, string name, bool isFolder)
    {
        if (transfer.Targets?.TryGetValue(source, out var exact) == true)
            return exact;
        string target = Path.Combine(transfer.Folder, name);
        if (!File.Exists(target) && !Directory.Exists(target))
            return target;
        if (transfer.Conflict == TransferConflict.Replace && !SamePath(source, target))
            return target;
        string stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isFolder ? "" : Path.GetExtension(name);
        if (transfer.Conflict == TransferConflict.Duplicate || SamePath(source, target))
        {
            string copy = Loc.F("{0} - copia", stem);
            target = Path.Combine(transfer.Folder, copy + extension);
            for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)
                target = Path.Combine(transfer.Folder, $"{copy} ({i}){extension}");
            return target;
        }
        for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)
            target = Path.Combine(transfer.Folder, $"{stem} ({i}){extension}");
        return target;
    }

    private static bool CopyOne(FileTransfer transfer, Job job, long completed, CancellationToken token)
    {
        // Para reemplazar, se copia aparte y el viejo se cambia al final: si se cancela o falla a
        // medias, el que ya estaba sigue entero (escribirle encima lo perdía).
        bool replacing = File.Exists(job.Target);
        string writeTo = replacing
            ? Path.Combine(Path.GetDirectoryName(job.Target)!, $".{Path.GetFileName(job.Target)}.{Guid.NewGuid():N}.strata-part")
            : job.Target;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.Target)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            transfer.AddError($"{Path.GetFileName(job.Source)}: {ex.Message}", ex is UnauthorizedAccessException, job.Source);
            return false;
        }
        int cancel = 0;
        CopyProgress progress = (total, transferred, _, _, _, _, _, _, _) =>
        {
            Interlocked.Exchange(ref transfer.DoneBytes, completed + transferred);
            return token.IsCancellationRequested ? ProgressCancel : ProgressContinue;
        };
        // Los grandes, sin la caché de Windows (como el Explorador): más rápido y sin llenar la memoria.
        uint flags = CopyFileAllowDecryptedDestination | (job.Size >= 256L * 1024 * 1024 ? CopyFileNoBuffering : 0);
        bool copied = CopyFileEx(job.Source, writeTo, progress, IntPtr.Zero, ref cancel, flags);
        int error = copied ? 0 : Marshal.GetLastWin32Error();
        GC.KeepAlive(progress);
        if (copied && replacing)
        {
            try
            {
                // Uno de solo lectura no se deja reemplazar.
                var attributes = File.GetAttributes(job.Target);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(job.Target, attributes & ~FileAttributes.ReadOnly);
                File.Move(writeTo, job.Target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DeletePart(writeTo);
                transfer.AddError($"{Path.GetFileName(job.Source)}: {ex.Message}", ex is UnauthorizedAccessException, job.Source);
                return false;
            }
        }
        if (copied)
            return true;
        if (replacing)
            DeletePart(writeTo);
        if (error == ErrorRequestAborted)
            throw new OperationCanceledException(token);
        transfer.AddError($"{Path.GetFileName(job.Source)}: {new System.ComponentModel.Win32Exception(error).Message}",
                          error is ErrorAccessDenied or ErrorPrivilegeNotHeld, job.Source);
        return false;
    }

    private static void DeletePart(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void MoveWhole(FileTransfer transfer, Job job)
    {
        try
        {
            if (Directory.Exists(job.Source))
                Directory.Move(job.Source, job.Target);
            else
                File.Move(job.Source, job.Target, overwrite: transfer.Conflict == TransferConflict.Replace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            transfer.AddError($"{Path.GetFileName(job.Source)}: {ex.Message}", ex is UnauthorizedAccessException, job.Source);
        }
    }

    private static void DeleteSource(FileTransfer transfer, string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Se copió pero el original sigue (en uso, sin permiso): se avisa, nada se pierde.
            transfer.AddError(Loc.F("{0}: se copió, pero no se pudo quitar el original ({1})", Path.GetFileName(path), ex.Message),
                              ex is UnauthorizedAccessException, path);
        }
    }

    private static void DeleteEmptyFolders(string folder)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder))
                DeleteEmptyFolders(dir);
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>La carpeta destino es la de origen o está dentro de ella.</summary>
    internal static bool IsInside(string folder, string source)
    {
        string f = Path.GetFullPath(folder).TrimEnd('\\') + "\\", s = Path.GetFullPath(source).TrimEnd('\\') + "\\";
        return f.StartsWith(s, StringComparison.OrdinalIgnoreCase);
    }

    private delegate int CopyProgress(long totalSize, long transferred, long streamSize, long streamTransferred,
                                      uint stream, uint reason, IntPtr sourceFile, IntPtr targetFile, IntPtr data);

    private const int ProgressContinue = 0, ProgressCancel = 1;
    private const int ErrorAccessDenied = 5, ErrorPrivilegeNotHeld = 1314, ErrorRequestAborted = 1235;
    private const uint CopyFileAllowDecryptedDestination = 0x8, CopyFileNoBuffering = 0x1000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CopyFileEx(string source, string target, CopyProgress progress, IntPtr data, ref int cancel, uint flags);
}
