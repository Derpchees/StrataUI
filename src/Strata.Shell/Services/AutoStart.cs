using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace Strata.Shell.Services;

/// <summary>
/// Inicio automático con Windows. Se usa una tarea programada al iniciar sesión (sin retraso y con
/// prioridad normal): arranca antes que las apps de la clave Run, que Windows demora, y así los
/// íconos de bandeja de esas apps llegan a Strata UI y no al Explorador. Sin elevar a propósito: con Strata UI
/// como administrador las apps normales no podrían mandarle sus íconos (Windows bloquea esos
/// mensajes hacia un proceso elevado) y todo lo que se abriera desde Strata UI saldría elevado.
/// Si la tarea no se puede crear, se usa la clave Run como antes.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StrataUI";

    /// <summary>
    /// Argumento con que lo abre el inicio de sesión: así no abre el Hub (salvo que se pida) y una
    /// segunda copia lanzada a la vez (la clave Run que deja el instalador) no se lo pide a la primera.
    /// </summary>
    public const string Arg = "--autostart";

    private static string ExePath => Environment.ProcessPath ?? "";
    private static string Command => $"\"{ExePath}\" {Arg}";
    private static string TaskName => $"StrataUI ({Environment.UserName})";

    // Cuando se llamaba WINX (hasta la 1.3).
    private const string LegacyValueName = "WINX";
    private static string LegacyTaskName => $"WINX ({Environment.UserName})";

    public static bool IsEnabled
    {
        get => TaskCommand() is ({ } command, _) && command.Equals(ExePath, StringComparison.OrdinalIgnoreCase)
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
    /// Strata UI cambió de carpeta. Corre en segundo plano al iniciar.
    /// </summary>
    public static void Migrate()
    {
        try
        {
            bool legacy = RemoveLegacy();
            bool runKey = RunKeyEnabled();
            var (task, arguments) = TaskCommand();
            // También la tarea de antes, sin el argumento de inicio automático.
            if (legacy || runKey || (task is not null && (!task.Equals(ExePath, StringComparison.OrdinalIgnoreCase) || arguments != Arg)))
            {
                if (CreateTask())
                {
                    SetRunKey(false);
                    Log.Info("Inicio automático: ahora con una tarea programada al iniciar sesión");
                }
                else if (runKey && task is not null && task.Equals(ExePath, StringComparison.OrdinalIgnoreCase))
                {
                    // La tarea (creada elevada, no se puede actualizar) ya abre este Strata UI: la clave
                    // Run que dejó el instalador solo lo abría dos veces.
                    SetRunKey(false);
                    Log.Info("Inicio automático: se quitó la clave Run repetida (ya hay una tarea)");
                }
                else if (task is not null && !File.Exists(task))
                {
                    // La tarea la creó un Strata UI elevado (no se puede cambiar sin administrador) y apunta
                    // a un ejecutable que ya no está: sin esto, Strata UI dejaba de iniciar con Windows.
                    SetRunKey(true);
                    Log.Info($"Inicio automático: la tarea apunta a {task}, que ya no existe; se usa la clave Run");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo actualizar el inicio automático", ex);
        }
    }

    /// <summary>Quita el inicio automático de cuando se llamaba WINX; true si estaba activado.</summary>
    private static bool RemoveLegacy()
    {
        bool found = false;
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            if (key?.GetValue(LegacyValueName) != null)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                found = true;
            }
        }
        if (Schtasks($"/Query /TN \"{LegacyTaskName}\"").Code == 0)
        {
            Schtasks($"/Delete /TN \"{LegacyTaskName}\" /F");
            found = true;
        }
        if (found)
            Log.Info("Inicio automático de WINX quitado: pasa a Strata UI");
        return found;
    }

    private static bool RunKeyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        // Con o sin el argumento (el instalador de antes la escribía sin él).
        return key?.GetValue(ValueName) is string value
               && value.StartsWith($"\"{ExePath}\"", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetRunKey(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>El programa que abre la tarea y sus argumentos; (null, null) si no existe.</summary>
    private static (string? Command, string? Arguments) TaskCommand()
    {
        var (code, output) = Schtasks($"/Query /TN \"{TaskName}\" /XML");
        if (code != 0)
            return (null, null);
        try
        {
            var elements = System.Xml.Linq.XDocument.Parse(output).Descendants().ToList();
            var command = elements.FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
            var arguments = elements.FirstOrDefault(e => e.Name.LocalName == "Arguments")?.Value;
            return (command?.Trim().Trim('"'), arguments?.Trim());
        }
        catch (System.Xml.XmlException)
        {
            return (null, null);
        }
    }

    private static bool CreateTask()
    {
        string user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Inicia Strata UI al iniciar sesión.</Description></RegistrationInfo>
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
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape($"\"{ExePath}\"")}</Command><Arguments>{Arg}</Arguments></Exec></Actions>
            </Task>
            """;
        string file = Path.Combine(Path.GetTempPath(), $"strataui-task-{Environment.ProcessId}.xml");
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
        if (TaskCommand().Command is not null)
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
