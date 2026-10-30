using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Lo que Archivos hace con permiso de administrador: copiar o mover a carpetas protegidas, borrar,
/// cambiar nombres, crear carpetas y formatear. Primero pregunta con el tema de Strata UI (no con las
/// ventanas de Windows) y después abre <c>StrataUI.exe --file-op &lt;tubería&gt;</c> elevado (UAC). El
/// trabajo va por una tubería con nombre aleatorio, no por la línea de comandos; el proceso elevado
/// lo valida, lo hace y devuelve el progreso por la misma tubería (la tarjeta de Archivos lo muestra).
/// </summary>
public static class ElevatedFiles
{
    public const string Arg = "--file-op";
    private const string PipePrefix = "StrataUI-files-";

    /// <summary>El trabajo que se le pide al proceso elevado (en JSON, una línea).</summary>
    internal sealed class Job
    {
        public string Op { get; set; } = "";
        public string? Conflict { get; set; }
        public string? Folder { get; set; }
        public List<string> Paths { get; set; } = [];
        public Dictionary<string, string>? Targets { get; set; }
        public string? NewName { get; set; }
        public string? FileSystem { get; set; }
        public string? Label { get; set; }
        public bool Quick { get; set; }
    }

    /// <summary>Pregunta si seguir con permiso de administrador (con el escudo, como Windows).</summary>
    internal static Task<bool> AskAsync(IntPtr owner, string message) =>
        Controls.StrataDialog.ConfirmAdminAsync(FileOperations.OwnerWindow(owner), Loc.T("Se necesita permiso de administrador"), message);

    /// <summary>Copia o mueve en el proceso elevado, con el progreso en la tarjeta de <paramref name="transfer"/>.</summary>
    internal static FileTransfer StartTransfer(FileTransfer transfer) => FileTransfers.Run(transfer, async t =>
    {
        var job = new Job
        {
            Op = t.Kind == TransferKind.Copy ? "copy" : "move",
            Conflict = t.Conflict.ToString(),
            Folder = t.Folder,
            Paths = t.Sources.ToList(),
            Targets = t.Targets,
        };
        int code = await ExchangeAsync(job, t, t.Cancellation.Token);
        if (code == Tweaks.Elevation.Cancelled)
            t.StateValue = TransferState.Canceled;
        else if (t.IsActive)
            t.StateValue = code == 0 ? TransferState.Done : TransferState.Failed;
    });

    /// <summary>Formatea en el proceso elevado (la tarjeta muestra que está trabajando).</summary>
    internal static FileTransfer StartFormat(string root, string fileSystem, string label, bool quick)
    {
        var transfer = new FileTransfer(TransferKind.Format, [root], root.TrimEnd('\\'), TransferConflict.Replace) { Elevated = true };
        return FileTransfers.Run(transfer, async t =>
        {
            t.StateValue = TransferState.Running;
            int code = await ExchangeAsync(new Job { Op = "format", Paths = [root], FileSystem = fileSystem, Label = label, Quick = quick }, t, CancellationToken.None);
            if (code == Tweaks.Elevation.Cancelled)
                t.StateValue = TransferState.Canceled;
            else
                t.StateValue = code == 0 ? TransferState.Done : TransferState.Failed;
        });
    }

    /// <summary>Una operación corta (borrar, cambiar nombre, crear carpeta). Null si salió bien; "" si se rechazó el UAC; si no, el error.</summary>
    internal static async Task<string?> RunAsync(Job job)
    {
        var errors = new FileTransfer(TransferKind.Copy, job.Paths, job.Folder ?? "", TransferConflict.Replace);
        int code = await ExchangeAsync(job, errors, CancellationToken.None);
        if (code == 0)
            return null;
        if (code == Tweaks.Elevation.Cancelled)
            return "";
        lock (errors.Errors)
            return errors.Errors.FirstOrDefault() ?? Loc.F("No se pudo completar (código {0}).", code);
    }

    /// <summary>
    /// Abre el proceso elevado, le pasa el trabajo y lee su progreso hasta que termina. Devuelve su
    /// código de salida (<see cref="Tweaks.Elevation.Cancelled"/> si se rechazó el UAC).
    /// </summary>
    private static async Task<int> ExchangeAsync(Job job, FileTransfer transfer, CancellationToken token)
    {
        string name = PipePrefix + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var process = Tweaks.Elevation.RunSelfAsync(Arg, name);
        using var stopWaiting = new CancellationTokenSource();
        var connect = pipe.WaitForConnectionAsync(stopWaiting.Token);
        if (await Task.WhenAny(connect, process) != connect)
        {
            // Terminó sin conectarse: se rechazó el UAC o no pudo arrancar.
            stopWaiting.Cancel();
            try
            {
                await connect;
            }
            catch (OperationCanceledException)
            {
            }
            int early = await process;
            if (early != Tweaks.Elevation.Cancelled)
                transfer.AddError(Loc.F("No se pudo completar (código {0}).", early), admin: false);
            return early;
        }
        var utf8 = new UTF8Encoding(false);
        using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(job));
        await using var cancel = token.Register(() =>
        {
            try
            {
                writer.WriteLine("C");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        });
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
            Apply(transfer, line);
        return await process;
    }

    /// <summary>Una línea del proceso elevado: progreso (P), destino (T), error (E), origen con errores (F) o estado final (S).</summary>
    private static void Apply(FileTransfer transfer, string line)
    {
        var parts = line.Split('\t');
        switch (parts[0])
        {
            case "P" when parts.Length >= 6:
                transfer.TotalBytes = long.Parse(parts[1]);
                Interlocked.Exchange(ref transfer.DoneBytes, long.Parse(parts[2]));
                transfer.TotalFiles = int.Parse(parts[3]);
                transfer.DoneFiles = int.Parse(parts[4]);
                transfer.CurrentName = parts[5];
                transfer.StateValue = TransferState.Running;
                break;
            case "T" when parts.Length >= 3:
                transfer.Resolved[parts[1]] = parts[2];
                break;
            case "E" when parts.Length >= 2:
                transfer.AddError(parts[1], admin: false, parts.Length >= 3 && parts[2].Length > 0 ? parts[2] : null);
                break;
            case "F" when parts.Length >= 2:
                lock (transfer.Errors)
                    transfer.FailedSources.Add(parts[1]);
                break;
            case "S" when parts.Length >= 2 && Enum.TryParse<TransferState>(parts[1], out var state):
                transfer.StateValue = state;
                break;
        }
    }

    // ── Proceso elevado ─────────────────────────────────────────────────────

    /// <summary>Punto de entrada de <c>--file-op &lt;tubería&gt;</c>: hace solo el trabajo validado.</summary>
    internal static int Run(string[] args)
    {
        if (args.Length != 2 || !args[1].StartsWith(PipePrefix, StringComparison.Ordinal) || args[1].Length != PipePrefix.Length + 32)
            return 2;
        try
        {
            // Asíncrona: la espera del "cancelar" se puede cortar al terminar (cerrar una tubería con una
            // lectura pendiente en otro hilo se quedaba esperando a esa lectura).
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(15000);
            var utf8 = new UTF8Encoding(false);
            using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true };
            var send = new object();
            void Send(params string[] fields)
            {
                lock (send)
                    writer.WriteLine(string.Join('\t', fields.Select(f => f.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '))));
            }
            var job = JsonSerializer.Deserialize<Job>(reader.ReadLine() ?? "");
            string? problem = job is null ? "vacío" : Validate(job);
            if (job is null || problem != null)
            {
                Log.Info($"Archivos (administrador): trabajo rechazado ({problem})");
                Send("E", Loc.T("La operación no es válida."));
                return 2;
            }
            Log.Info($"Archivos (administrador): {job.Op} de {job.Paths.Count} elemento(s)");
            return job.Op switch
            {
                "copy" or "move" => Transfer(job, reader, Send),
                "delete" or "recycle" => Delete(job, Send),
                "rename" => Simple(Send, job.Paths[0], () =>
                {
                    string target = Path.Combine(Path.GetDirectoryName(job.Paths[0].TrimEnd('\\'))!, job.NewName!);
                    if (Directory.Exists(job.Paths[0]))
                        Directory.Move(job.Paths[0], target);
                    else
                        File.Move(job.Paths[0], target);
                }),
                "mkdir" => Simple(Send, job.Paths[0], () => Directory.CreateDirectory(job.Paths[0])),
                "format" => DriveFormat.Format(job.Paths[0], job.FileSystem!, job.Label ?? "", job.Quick, message => Send("E", message)),
                _ => 2,
            };
        }
        catch (Exception ex)
        {
            Log.Error("Archivos (administrador): falló la operación", ex);
            return 1;
        }
    }

    private static int Simple(Action<string[]> send, string path, Action work)
    {
        try
        {
            work();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            send(["E", ex.Message, path]);
            return 1;
        }
    }

    private static int Transfer(Job job, StreamReader reader, Action<string[]> send)
    {
        var kind = job.Op == "copy" ? TransferKind.Copy : TransferKind.Move;
        var transfer = new FileTransfer(kind, job.Paths, job.Folder!, Enum.Parse<TransferConflict>(job.Conflict!))
        {
            Targets = job.Targets is null ? null : new Dictionary<string, string>(job.Targets, StringComparer.OrdinalIgnoreCase),
            Elevated = true,
        };
        transfer.Clock.Start();
        // "C" desde Strata UI: cancelar. Se deja de escuchar al terminar.
        using var stopListening = new CancellationTokenSource();
        var listener = Task.Run(async () =>
        {
            try
            {
                while (await reader.ReadLineAsync(stopListening.Token) is { } line)
                    if (line == "C")
                        transfer.Cancel();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        });
        var worker = new Thread(() => FileTransfers.Execute(transfer)) { IsBackground = true };
        worker.Start();
        while (!worker.Join(200))
            send(["P", transfer.TotalBytes.ToString(), Interlocked.Read(ref transfer.DoneBytes).ToString(),
                  transfer.TotalFiles.ToString(), transfer.DoneFiles.ToString(), transfer.CurrentName]);
        foreach (var (source, target) in transfer.Resolved)
            send(["T", source, target]);
        lock (transfer.Errors)
        {
            foreach (var error in transfer.Errors)
                send(["E", error]);
            // Los orígenes con errores, para que un nuevo intento sea solo de esos.
            foreach (var source in transfer.FailedSources)
                send(["F", source]);
        }
        send(["S", transfer.State.ToString()]);
        stopListening.Cancel();
        listener.Wait(2000);
        return transfer.State == TransferState.Done ? 0 : 1;
    }

    private static int Delete(Job job, Action<string[]> send)
    {
        int result = FileOperations.ShellOperation(FileOperations.FoDelete, job.Paths, null,
            (ushort)(FileOperations.FofNoConfirmation | FileOperations.FofSilent | FileOperations.FofNoErrorUi
                     | (job.Op == "recycle" ? FileOperations.FofAllowUndo : 0)), IntPtr.Zero, out bool aborted);
        if (result == 0 && !aborted)
            return 0;
        send(["E", Loc.F("No se pudo eliminar (código 0x{0:X}).", result)]);
        return 1;
    }

    /// <summary>
    /// Qué no se hace nunca, aunque lo pida Strata UI: rutas que no son de archivos (dispositivos,
    /// flujos), destinos fuera de la carpeta elegida, borrar la raíz de una unidad o las carpetas del
    /// sistema, nombres con barras y formatear la unidad de Windows. Null si el trabajo es válido.
    /// </summary>
    internal static string? Validate(Job job)
    {
        if (job.Paths.Count == 0 || job.Paths.Count > 100_000 || !job.Paths.All(SafePath))
            return "rutas";
        switch (job.Op)
        {
            case "copy" or "move":
                if (job.Folder is null || !SafePath(job.Folder) || !Enum.TryParse<TransferConflict>(job.Conflict, out _))
                    return "destino";
                // Mover una carpeta del sistema es quitarla de su lugar, como borrarla.
                if (job.Op == "move" && job.Paths.Any(Protected))
                    return "protegida";
                if (job.Targets is { } targets && targets.Any(t => !job.Paths.Contains(t.Key, StringComparer.OrdinalIgnoreCase)
                                                                    || !SafePath(t.Value) || !IsBelow(t.Value, job.Folder)))
                    return "destinos";
                return null;
            case "delete" or "recycle":
                return job.Paths.Any(Protected) ? "protegida" : null;
            case "rename":
                return job.Paths.Count == 1 && !Protected(job.Paths[0]) && job.NewName is { Length: > 0 } newName
                       && newName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && newName is not ("." or "..")
                       && !newName.EndsWith('.') && !newName.EndsWith(' ')
                    ? null : "nombre";
            case "mkdir":
                return job.Paths.Count == 1 ? null : "carpeta";
            case "format":
                return job.Paths.Count == 1 && DriveFormat.CanFormat(job.Paths[0]) && job.FileSystem is "NTFS" or "exFAT" or "FAT32"
                    ? null : "unidad";
        }
        return "operación";
    }

    private static bool SafePath(string path) =>
        path.Length is > 2 and < 32_000 && Path.IsPathFullyQualified(path)
        && !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal)
        && path.IndexOfAny(['*', '?', '"', '<', '>', '|', '\0']) < 0
        && path.IndexOf(':', 2) < 0 // sin flujos alternativos
        && !path.Split('\\').Any(part => part is ".." or ".");

    /// <summary>Dentro de la carpeta (no ella misma).</summary>
    private static bool IsBelow(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>La raíz de una unidad o una carpeta del sistema (Windows, Archivos de programa, Usuarios): no se borra ni se renombra.</summary>
    internal static bool Protected(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd('\\');
        if (Path.GetPathRoot(full)?.TrimEnd('\\') is { } root && string.Equals(root, full, StringComparison.OrdinalIgnoreCase))
            return true;
        var system = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        return system.Any(s => s.Length > 0 && string.Equals(s.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase));
    }
}
