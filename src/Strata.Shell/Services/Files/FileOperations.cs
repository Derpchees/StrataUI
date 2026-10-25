using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Copiar, mover y mandar a la Papelera con el motor de Windows (SHFileOperation): su ventana de
/// progreso, sus preguntas ante conflictos y Ctrl+Z en el Explorador. Corre en un hilo STA aparte
/// para que la ventana siga respondiendo. El portapapeles usa el formato del Explorador, así que se
/// puede copiar aquí y pegar allí (y al revés).
/// </summary>
public static class FileOperations
{
    public static Task<bool> CopyAsync(IReadOnlyList<string> sources, string folder, IntPtr owner) =>
        RunAsync(FoCopy, sources, folder, FofAllowUndo | FofNoConfirmMkDir | FofRenameOnCollision, owner);

    public static Task<bool> MoveAsync(IReadOnlyList<string> sources, string folder, IntPtr owner) =>
        RunAsync(FoMove, sources, folder, FofAllowUndo | FofNoConfirmMkDir, owner);

    /// <summary>A la Papelera sin preguntar (como Windows 11); pregunta solo si no cabe y se borraría del todo.</summary>
    public static Task<bool> RecycleAsync(IReadOnlyList<string> paths, IntPtr owner) =>
        RunAsync(FoDelete, paths, null, FofAllowUndo | FofNoConfirmation | FofWantNukeWarning, owner);

    /// <summary>Borra para siempre (sin Papelera); Windows pide confirmación.</summary>
    public static Task<bool> DeleteForeverAsync(IReadOnlyList<string> paths, IntPtr owner) =>
        RunAsync(FoDelete, paths, null, 0, owner);

    /// <summary>Cambia el nombre; devuelve el error para mostrarlo o null si salió bien.</summary>
    public static string? Rename(string path, string newName, bool folder)
    {
        // Con "\" o "..\" en el nombre, Path.Combine lo movería a otra carpeta en vez de renombrarlo; y
        // Windows quita en silencio los puntos y espacios del final.
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Localization.Loc.T("El nombre no puede llevar \\ / : * ? \" < > |");
        if (newName.Trim().Length == 0 || newName.EndsWith('.') || newName.EndsWith(' '))
            return Localization.Loc.T("El nombre no puede estar vacío ni terminar en punto o espacio.");
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
            return ex.Message;
        }
    }

    /// <summary>Crea "Nueva carpeta" (o "Nueva carpeta (2)"…) y devuelve su ruta.</summary>
    public static string NewFolder(string parent, string baseName)
    {
        string path = Path.Combine(parent, baseName);
        for (int i = 2; Directory.Exists(path) || File.Exists(path); i++)
            path = Path.Combine(parent, $"{baseName} ({i})");
        Directory.CreateDirectory(path);
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

    /// <summary>Vacía la Papelera (Windows pregunta antes).</summary>
    public static void EmptyRecycleBin(IntPtr owner)
    {
        int result = SHEmptyRecycleBin(owner, null, 0);
        if (result != 0 && result != unchecked((int)0x8000FFFF)) // E_UNEXPECTED: ya estaba vacía
            Log.Info($"Archivos: la Papelera no se vació (0x{result:X8})");
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

    private static Task<bool> RunAsync(uint operation, IReadOnlyList<string> sources, string? target, ushort flags, IntPtr owner)
    {
        var done = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
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
                if (result != 0 && !op.AnyOperationsAborted)
                    Log.Info($"Archivos: la operación {operation} terminó con el código 0x{result:X}");
                done.SetResult(result == 0 && !op.AnyOperationsAborted);
            }
            catch (Exception ex)
            {
                Log.Error("Archivos: falló la operación con archivos", ex);
                done.SetResult(false);
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

    private const uint FoMove = 1, FoCopy = 2, FoDelete = 3;
    private const ushort FofRenameOnCollision = 0x8, FofNoConfirmation = 0x10, FofAllowUndo = 0x40,
                         FofNoConfirmMkDir = 0x200, FofWantNukeWarning = 0x4000;

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
