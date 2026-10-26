using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Copiar y mover con el motor propio (<see cref="FileTransfers"/>: su progreso se ve dentro del
/// explorador); mandar a la Papelera y borrar con el de Windows (SHFileOperation), sin sus ventanas.
/// Las preguntas (borrar para siempre, nombres repetidos, unidades sin Papelera) son de Strata UI,
/// con su tema. El portapapeles usa el formato del Explorador, así que se puede copiar aquí y pegar
/// allí (y al revés). Las funciones que preguntan se llaman desde el hilo de la UI.
/// </summary>
public static class FileOperations
{
    /// <summary>Copia (pegar en la misma carpeta hace "nombre - copia", sin preguntar). False si se canceló o falló algo.</summary>
    public static async Task<bool> CopyAsync(IReadOnlyList<string> sources, string folder, IntPtr owner)
    {
        if (sources.Count == 0 || await ConflictAsync(sources, folder, Loc.T("Copiar"), owner) is not { } conflict)
            return false;
        return await TransferAsync(TransferKind.Copy, sources, folder, conflict, owner);
    }

    public static async Task<bool> MoveAsync(IReadOnlyList<string> sources, string folder, IntPtr owner)
    {
        // Lo que ya está en esa carpeta no se mueve.
        var moving = sources.Where(s => !SameFolder(s, folder)).ToList();
        if (moving.Count == 0 || await ConflictAsync(moving, folder, Loc.T("Mover"), owner) is not { } conflict)
            return false;
        return await TransferAsync(TransferKind.Move, moving, folder, conflict, owner);
    }

    /// <summary>
    /// A una carpeta protegida (Archivos de programa, la raíz de C:…) se pregunta antes y se hace con
    /// permiso de administrador desde el principio; si algo falla por permisos en una carpeta normal,
    /// se pregunta al terminar y se reintenta solo eso.
    /// </summary>
    private static async Task<bool> TransferAsync(TransferKind kind, IReadOnlyList<string> sources, string folder, TransferConflict conflict, IntPtr owner)
    {
        if (!CanWrite(folder))
        {
            if (!await ElevatedFiles.AskAsync(owner, kind == TransferKind.Copy
                    ? Loc.F("Para copiar a «{0}» hace falta permiso de administrador.", FileTransfers.FolderNameOf(folder))
                    : Loc.F("Para mover a «{0}» hace falta permiso de administrador.", FileTransfers.FolderNameOf(folder))))
                return false;
            return await ElevatedFiles.StartTransfer(new FileTransfer(kind, sources, folder, conflict) { Elevated = true }).Finished.Task;
        }
        var transfer = FileTransfers.Begin(new FileTransfer(kind, sources, folder, conflict));
        if (await transfer.Finished.Task)
            return true;
        return transfer.CanRetryAsAdmin && await FileTransfers.RetryAsAdminAsync(transfer, owner);
    }

    /// <summary>Se puede escribir en la carpeta sin permiso de administrador (se prueba creando un archivo que se borra solo).</summary>
    internal static bool CanWrite(string folder)
    {
        if (!Directory.Exists(folder))
            return true; // que lo diga la copia
        try
        {
            using (new FileStream(Path.Combine(folder, $".strata-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.Write,
                                  FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return true; // llena, de solo lectura, desconectada: no lo arregla el administrador
        }
    }

    /// <summary>
    /// A la Papelera sin preguntar (como Windows 11). Lo que está en una unidad sin Papelera (USB, red)
    /// se borraría del todo: eso se pregunta antes.
    /// </summary>
    public static async Task<bool> RecycleAsync(IReadOnlyList<string> paths, IntPtr owner)
    {
        if (paths.Any(p => !HasRecycleBin(p)))
        {
            if (!await ConfirmAsync(Loc.T("Eliminar definitivamente"),
                    Loc.T("Esta unidad no tiene Papelera: lo que elimines se borrará para siempre."),
                    Loc.T("Eliminar"), owner))
                return false;
            return await DeleteAsync(paths, recycle: false, owner);
        }
        return await DeleteAsync(paths, recycle: true, owner);
    }

    /// <summary>
    /// Borra con el motor de Windows sin sus ventanas de error; lo que no se pudo por permisos se
    /// pregunta (con el tema de Strata UI) y se borra con permiso de administrador.
    /// </summary>
    private static async Task<bool> DeleteAsync(IReadOnlyList<string> paths, bool recycle, IntPtr owner)
    {
        // Si algo no cabe en la Papelera, Windows avisa (es lo único que sigue preguntando él).
        ushort flags = (ushort)(FofNoConfirmation | FofSilent | FofNoErrorUi | (recycle ? FofAllowUndo | FofWantNukeWarning : 0));
        var (result, aborted) = await RunAsync(FoDelete, paths, null, flags, owner);
        if (result == 0 && !aborted)
            return true;
        var left = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (aborted || left.Count == 0 || result is not (ErrorAccessDenied or DeAccessDeniedSource or ErrorPrivilegeNotHeld))
            return false;
        string message = left.Count == 1
            ? Loc.F("Para eliminar «{0}» hace falta permiso de administrador.", Path.GetFileName(left[0].TrimEnd('\\')))
            : Loc.F("Para eliminar {0} elementos hace falta permiso de administrador.", left.Count);
        if (!await ElevatedFiles.AskAsync(owner, message))
            return false;
        if (await ElevatedFiles.RunAsync(new ElevatedFiles.Job { Op = recycle ? "recycle" : "delete", Paths = left }) is { Length: > 0 } error)
        {
            Log.Info($"Archivos: no se pudo eliminar como administrador ({error})");
            return false;
        }
        return true;
    }

    /// <summary>Borra para siempre (sin Papelera), después de preguntar.</summary>
    /// <param name="names">Lo que se muestra al preguntar (en la Papelera las rutas son "$R…").</param>
    public static async Task<bool> DeleteForeverAsync(IReadOnlyList<string> paths, IntPtr owner, IReadOnlyList<string>? names = null)
    {
        if (paths.Count == 0)
            return false;
        string message = paths.Count == 1
            ? Loc.F("«{0}» se borrará para siempre. No se puede deshacer.", names?.FirstOrDefault() ?? Path.GetFileName(paths[0].TrimEnd('\\')))
            : Loc.F("{0} elementos se borrarán para siempre. No se puede deshacer.", paths.Count);
        if (!await ConfirmAsync(Loc.T("Eliminar definitivamente"), message, Loc.T("Eliminar"), owner))
            return false;
        return await DeleteAsync(paths, recycle: false, owner);
    }

    private static bool SameFolder(string path, string folder) =>
        string.Equals(Path.GetDirectoryName(path.TrimEnd('\\'))?.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Qué hacer si en el destino ya hay algo con el mismo nombre: reemplazar o conservar los dos
    /// ("nombre (2)"). Null si se canceló. En la misma carpeta (copiar y pegar ahí), "nombre - copia".
    /// </summary>
    private static async Task<TransferConflict?> ConflictAsync(IReadOnlyList<string> sources, string folder, string title, IntPtr owner)
    {
        if (sources.All(s => SameFolder(s, folder)))
            return TransferConflict.Duplicate;
        var repeated = sources.Where(s => !SameFolder(s, folder))
            .Select(s => Path.GetFileName(s.TrimEnd('\\')))
            .Where(name => name.Length > 0 && (File.Exists(Path.Combine(folder, name)) || Directory.Exists(Path.Combine(folder, name))))
            .ToList();
        if (repeated.Count == 0)
            return TransferConflict.KeepBoth;
        string folderName = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } n ? n : folder;
        string message = repeated.Count == 1
            ? Loc.F("En «{0}» ya hay algo llamado «{1}».", folderName, repeated[0])
            : Loc.F("En «{0}» ya hay {1} elementos con los mismos nombres.", folderName, repeated.Count);
        return await Controls.StrataDialog.ShowAsync(OwnerWindow(owner), title, message,
                   Loc.T("Reemplazar"), Loc.T("Conservar ambos"), Loc.T("Cancelar")) switch
        {
            0 => TransferConflict.Replace,
            1 => TransferConflict.KeepBoth,
            _ => null,
        };
    }

    private static Task<bool> ConfirmAsync(string title, string message, string action, IntPtr owner) =>
        Controls.StrataDialog.ConfirmAsync(OwnerWindow(owner), title, message, action);

    /// <summary>La ventana de la app con ese HWND (el explorador, el escritorio): el diálogo sale sobre ella, en su monitor.</summary>
    internal static Window? OwnerWindow(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? null
            : Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && new System.Windows.Interop.WindowInteropHelper(w).Handle == hwnd);

    /// <summary>Las unidades fijas locales tienen Papelera; las extraíbles y de red, no.</summary>
    private static bool HasRecycleBin(string path)
    {
        try
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return false;
            return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (ArgumentException)
        {
            return true; // sin saberlo, Windows decide (y avisa si no cabe)
        }
    }

    /// <summary>
    /// Cambia el nombre; si hace falta permiso de administrador, lo pregunta y lo hace elevado.
    /// Devuelve el error para mostrarlo, "" si se canceló o null si salió bien.
    /// </summary>
    public static async Task<string?> RenameAsync(string path, string newName, bool folder, IntPtr owner)
    {
        string? error = Rename(path, newName, folder, out bool denied);
        if (error is null || !denied)
            return error;
        if (!await ElevatedFiles.AskAsync(owner, Loc.F("Para cambiar el nombre de «{0}» hace falta permiso de administrador.", Path.GetFileName(path.TrimEnd('\\')))))
            return "";
        return await ElevatedFiles.RunAsync(new ElevatedFiles.Job { Op = "rename", Paths = [path], NewName = newName });
    }

    /// <summary>Cambia el nombre; devuelve el error para mostrarlo o null si salió bien.</summary>
    public static string? Rename(string path, string newName, bool folder) => Rename(path, newName, folder, out _);

    private static string? Rename(string path, string newName, bool folder, out bool denied)
    {
        denied = false;
        // Con "\" o "..\" en el nombre, Path.Combine lo movería a otra carpeta en vez de renombrarlo; y
        // Windows quita en silencio los puntos y espacios del final.
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Loc.T("El nombre no puede llevar \\ / : * ? \" < > |");
        if (newName.Trim().Length == 0 || newName.EndsWith('.') || newName.EndsWith(' '))
            return Loc.T("El nombre no puede estar vacío ni terminar en punto o espacio.");
        string target = Path.Combine(Path.GetDirectoryName(path)!, newName);
        try
        {
            if (folder)
                Directory.Move(path, target);
            else
                File.Move(path, target);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            denied = ex is UnauthorizedAccessException;
            return ex.Message;
        }
    }

    /// <summary>Crea "Nueva carpeta" (o "Nueva carpeta (2)"…) y devuelve su ruta.</summary>
    public static string NewFolder(string parent, string baseName)
    {
        string path = FreeName(parent, baseName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Como <see cref="NewFolder"/>, pero en una carpeta protegida pregunta y la crea con permiso de
    /// administrador. Null si se canceló o falló (el error queda en el registro).
    /// </summary>
    public static async Task<string?> NewFolderAsync(string parent, string baseName, IntPtr owner)
    {
        string path = FreeName(parent, baseName);
        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            if (!await ElevatedFiles.AskAsync(owner, Loc.F("Para crear una carpeta en «{0}» hace falta permiso de administrador.", FileTransfers.FolderNameOf(parent))))
                return null;
            if (await ElevatedFiles.RunAsync(new ElevatedFiles.Job { Op = "mkdir", Paths = [path] }) is { } error)
            {
                if (error.Length > 0)
                    Log.Info($"Archivos: no se pudo crear la carpeta como administrador ({error})");
                return null;
            }
            return path;
        }
    }

    private static string FreeName(string parent, string baseName)
    {
        string path = Path.Combine(parent, baseName);
        for (int i = 2; Directory.Exists(path) || File.Exists(path); i++)
            path = Path.Combine(parent, $"{baseName} ({i})");
        return path;
    }

    public static void SetClipboard(IReadOnlyList<string> paths, bool cut)
    {
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        var data = new DataObject();
        data.SetFileDropList(list);
        // Lo que lee el Explorador para saber si pegar mueve (2) o copia (5: copiar o vincular).
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
        try
        {
            Clipboard.SetDataObject(data, true);
        }
        catch (COMException ex)
        {
            Log.Error("Archivos: el portapapeles está ocupado", ex);
        }
    }

    /// <summary>Archivos del portapapeles y si se cortaron; null si no hay archivos.</summary>
    public static (string[] Paths, bool Cut)? ReadClipboard()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList())
                return null;
            var paths = Clipboard.GetFileDropList().Cast<string>().ToArray();
            bool cut = Clipboard.GetData("Preferred DropEffect") is MemoryStream stream && stream.Length >= 4
                       && (BitConverter.ToInt32(stream.ToArray(), 0) & 2) != 0;
            return paths.Length > 0 ? (paths, cut) : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static bool ClipboardHasFiles()
    {
        try
        {
            return Clipboard.ContainsFileDropList();
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>Abre con su programa; una carpeta o un acceso directo a carpeta lo resuelve quien llama.</summary>
    public static bool Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Archivos: no se pudo abrir {path}", ex);
            return false;
        }
    }

    /// <summary>"Abrir con…" de Windows, en su propio proceso.</summary>
    public static void OpenWith(string path) => Start("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}");

    /// <summary>Vacía la Papelera, después de preguntar. False si se canceló.</summary>
    public static async Task<bool> EmptyRecycleBinAsync(IntPtr owner)
    {
        if (!await ConfirmAsync(Loc.T("Vaciar la Papelera"),
                Loc.T("Todo lo que hay en la Papelera se borrará para siempre. No se puede deshacer."),
                Loc.T("Vaciar"), owner))
            return false;
        // Sin la pregunta ni la ventana de progreso de Windows (ya se hizo la nuestra); en otro hilo,
        // porque una Papelera grande tarda.
        var done = new TaskCompletionSource<int>();
        var thread = new Thread(() => done.SetResult(SHEmptyRecycleBin(owner, null, SherbNoConfirmation | SherbNoProgressUi)))
        {
            IsBackground = true,
            Name = "Strata UI: vaciar la Papelera",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        int result = await done.Task;
        if (result != 0 && result != unchecked((int)0x8000FFFF)) // E_UNEXPECTED: ya estaba vacía
            Log.Info($"Archivos: la Papelera no se vació (0x{result:X8})");
        return true;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr owner, string? root, uint flags);

    /// <summary>"Conectar unidad de red" de Windows (asignar una letra a \\servidor\recurso).</summary>
    public static void ConnectNetworkDrive() => Start("rundll32.exe", "shell32.dll,SHHelpShortcuts_RunDLL Connect");

    /// <summary>Muestra la carpeta (o el archivo seleccionado) en el Explorador de Windows.</summary>
    public static void ShowInExplorer(string path, bool select) =>
        Start("explorer.exe", select ? $"/select,\"{path}\"" : $"\"{path}\"");

    public static void OpenTerminal(string folder)
    {
        if (!Start("wt.exe", $"-d \"{folder.TrimEnd('\\')}\\.\""))
            Start("powershell.exe", $"-NoExit -Command Set-Location -LiteralPath '{folder.Replace("'", "''")}'", folder);
    }

    private static bool Start(string file, string arguments, string? workingDirectory = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, WorkingDirectory = workingDirectory ?? "" });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Archivos: no se pudo abrir {file} {arguments}", ex);
            return false;
        }
    }

    /// <summary>SHFileOperation en un hilo STA aparte: su código de resultado y si se canceló algo.</summary>
    private static Task<(int Result, bool Aborted)> RunAsync(uint operation, IReadOnlyList<string> sources, string? target, ushort flags, IntPtr owner)
    {
        var done = new TaskCompletionSource<(int, bool)>();
        var thread = new Thread(() =>
        {
            try
            {
                int result = ShellOperation(operation, sources, target, flags, owner, out bool aborted);
                done.SetResult((result, aborted));
            }
            catch (Exception ex)
            {
                Log.Error("Archivos: falló la operación con archivos", ex);
                done.SetResult((-1, false));
            }
        })
        {
            IsBackground = true,
            Name = "Strata UI: operación con archivos",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>SHFileOperation en este hilo (también en el proceso elevado).</summary>
    internal static int ShellOperation(uint operation, IReadOnlyList<string> sources, string? target, ushort flags, IntPtr owner, out bool aborted)
    {
        var op = new ShFileOpStruct
        {
            Owner = owner,
            Function = operation,
            // Lista terminada en doble nulo, como pide SHFileOperation.
            From = string.Join('\0', sources) + "\0\0",
            To = target is null ? null : target + "\0\0",
            Flags = flags,
        };
        int result = SHFileOperation(ref op);
        aborted = op.AnyOperationsAborted;
        if (result != 0 && !aborted)
            Log.Info($"Archivos: la operación {operation} terminó con el código 0x{result:X}");
        return result;
    }

    internal const uint FoDelete = 3;
    internal const ushort FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofWantNukeWarning = 0x4000, FofSilent = 0x4, FofNoErrorUi = 0x400;
    // Errores de SHFileOperation por permisos (el de Win32 y el propio "sin acceso al origen").
    private const int ErrorAccessDenied = 5, DeAccessDeniedSource = 0x78, ErrorPrivilegeNotHeld = 1314;
    private const uint SherbNoConfirmation = 0x1, SherbNoProgressUi = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Owner;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct operation);
}
