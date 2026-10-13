using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace Winx.Shell.Services;

/// <summary>
/// Inicio automático con Windows. Se usa una tarea programada al iniciar sesión (sin retraso y con
/// prioridad normal): arranca antes que las apps de la clave Run, que Windows demora, y así los
/// íconos de bandeja de esas apps llegan a WINX y no al Explorador. Sin elevar a propósito: con WINX
/// como administrador las apps normales no podrían mandarle sus íconos (Windows bloquea esos
/// mensajes hacia un proceso elevado) y todo lo que se abriera desde WINX saldría elevado.
/// Si la tarea no se puede crear, se usa la clave Run como antes.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WINX";

    private static string ExePath => Environment.ProcessPath ?? "";
    private static string Command => $"\"{ExePath}\"";
    private static string TaskName => $"WINX ({Environment.UserName})";

    public static bool IsEnabled
    {
        get => TaskCommand() is { } command && command.Equals(ExePath, StringComparison.OrdinalIgnoreCase)
               || RunKeyEnabled();
        set
        {
            if (value)
            {
                if (CreateTask())
                    SetRunKey(false);
                else
                    SetRunKey(true);
            }
            else
            {
                DeleteTask();
                SetRunKey(false);
            }
        }
    }

    /// <summary>
    /// Pasa a la tarea programada a quien tenía la clave Run (versiones anteriores), y la actualiza si
    /// WINX cambió de carpeta. Corre en segundo plano al iniciar.
    /// </summary>
    public static void Migrate()
    {
        try
        {
            bool runKey = RunKeyEnabled();
            string? task = TaskCommand();
            if (runKey || (task is not null && !task.Equals(ExePath, StringComparison.OrdinalIgnoreCase)))
            {
                if (CreateTask())
                {
                    SetRunKey(false);
                    Log.Info("Inicio automático: ahora con una tarea programada al iniciar sesión");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo actualizar el inicio automático", ex);
        }
    }

    private static bool RunKeyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value
               && value.Equals(Command, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetRunKey(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>El programa que abre la tarea, o null si no existe.</summary>
    private static string? TaskCommand()
    {
        var (code, output) = Schtasks($"/Query /TN \"{TaskName}\" /XML");
        if (code != 0)
            return null;
        try
        {
            var command = System.Xml.Linq.XDocument.Parse(output).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
            return command?.Trim().Trim('"');
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static bool CreateTask()
    {
        string user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Inicia WINX al iniciar sesión.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(Command)}</Command></Exec></Actions>
            </Task>
            """;
        string file = Path.Combine(Path.GetTempPath(), $"winx-task-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            var (code, output) = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            if (code != 0)
                Log.Info($"No se pudo crear la tarea de inicio automático: {output.Trim()}");
            return code == 0;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void DeleteTask()
    {
        if (TaskCommand() is not null)
            Schtasks($"/Delete /TN \"{TaskName}\" /F");
    }

    private static (int Code, string Output) Schtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
