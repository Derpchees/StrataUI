using System.IO;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Files;

/// <summary>
/// Formatear una unidad desde Archivos (clic derecho en la unidad), con el diálogo de Strata UI y en
/// el proceso elevado (<see cref="ElevatedFiles"/>), con MSFT_Volume.Format (lo mismo que usa
/// <c>Format-Volume</c>). Nunca la unidad de Windows, ni la de Strata UI o la de los datos del usuario
/// (formatearla las tumbaría), ni lectoras o unidades de red.
/// </summary>
public static class DriveFormat
{
    /// <summary>FAT32: Windows solo formatea unidades de hasta 32 GB con él.</summary>
    private const long Fat32Limit = 32L * 1024 * 1024 * 1024;

    /// <summary>Se puede formatear esta unidad ("E:\").</summary>
    public static bool CanFormat(string root)
    {
        try
        {
            if (root.Length < 2 || root[1] != ':' || !char.IsAsciiLetter(root[0]))
                return false;
            string drive = char.ToUpperInvariant(root[0]) + @":\";
            if (!string.Equals(root.TrimEnd('\\') + "\\", drive, StringComparison.OrdinalIgnoreCase))
                return false;
            var info = new DriveInfo(drive);
            if (info.DriveType is not (DriveType.Fixed or DriveType.Removable))
                return false;
            return !Untouchable().Contains(drive[0]);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Letras que no se formatean: Windows, el arranque, Strata UI y las carpetas del usuario.</summary>
    private static HashSet<char> Untouchable()
    {
        var letters = new HashSet<char>();
        foreach (var path in new[]
                 {
                     Environment.SystemDirectory,
                     Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     Environment.GetEnvironmentVariable("SystemDrive") ?? "",
                     AppContext.BaseDirectory,
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            if (path.Length >= 2 && path[1] == ':')
                letters.Add(char.ToUpperInvariant(path[0]));
        }
        return letters;
    }

    /// <summary>Pregunta cómo formatear (con el aviso de que se borra todo) y lo hace con permiso de administrador.</summary>
    public static async Task<FileTransfer?> AskAndFormatAsync(string root, IntPtr owner)
    {
        if (!CanFormat(root))
            return null;
        DriveInfo info;
        long size;
        string name, label, current;
        try
        {
            info = new DriveInfo(root);
            size = info.IsReady ? info.TotalSize : 0;
            label = info.IsReady ? info.VolumeLabel : "";
            current = info.IsReady ? info.DriveFormat : "";
            name = Places.NameOf(info.RootDirectory.FullName);
        }
        catch (IOException)
        {
            return null;
        }
        var systems = new List<string> { "NTFS", "exFAT" };
        if (size is > 0 and <= Fat32Limit)
            systems.Add("FAT32");
        string preferred = systems.Contains(current, StringComparer.OrdinalIgnoreCase)
            ? systems.First(s => s.Equals(current, StringComparison.OrdinalIgnoreCase))
            : info.DriveType == DriveType.Removable ? "exFAT" : "NTFS";

        var fileSystem = new ComboBox { ItemsSource = systems, SelectedItem = preferred, Margin = new Thickness(0, 6, 0, 14) };
        var nameBox = new TextBox { Text = label, Margin = new Thickness(0, 6, 0, 14) };
        void Limit() => nameBox.MaxLength = fileSystem.SelectedItem as string == "NTFS" ? 32 : 11;
        Limit();
        fileSystem.SelectionChanged += (_, _) => Limit();
        var quick = new CheckBox { Content = Loc.T("Formato rápido"), IsChecked = true };
        var panel = new StackPanel();
        panel.Children.Add(Caption(Loc.T("Sistema de archivos")));
        panel.Children.Add(fileSystem);
        panel.Children.Add(Caption(Loc.T("Nombre de la unidad")));
        panel.Children.Add(nameBox);
        panel.Children.Add(quick);

        string message = Loc.F("Se borrará todo lo que hay en {0} ({1}). No se puede deshacer.", name, FileEntry.FormatSize(size));
        if (await Controls.StrataDialog.ShowAsync(FileOperations.OwnerWindow(owner), Loc.F("Formatear {0}", name), message, panel, true,
                                                  Loc.T("Formatear"), Loc.T("Cancelar")) != 0)
            return null;
        string chosen = fileSystem.SelectedItem as string ?? preferred;
        Log.Info($"Archivos: formatear {root} en {chosen} (rápido: {quick.IsChecked == true})");
        return ElevatedFiles.StartFormat(info.RootDirectory.FullName, chosen, CleanLabel(nameBox.Text, chosen), quick.IsChecked == true);
    }

    private static System.Windows.Controls.TextBlock Caption(string text)
    {
        var block = new System.Windows.Controls.TextBlock { Text = text, FontSize = 13 };
        block.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return block;
    }

    /// <summary>Sin los caracteres que no admite el nombre de una unidad y dentro de su largo (11 en FAT, 32 en NTFS).</summary>
    internal static string CleanLabel(string label, string fileSystem)
    {
        var invalid = fileSystem == "NTFS" ? "\"*/:<>?\\|" : "\"*/:<>?\\|.,;=+[]";
        var clean = new string(label.Trim().Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray());
        int max = fileSystem == "NTFS" ? 32 : 11;
        return clean.Length > max ? clean[..max] : clean;
    }

    // ── Proceso elevado ─────────────────────────────────────────────────────

    /// <summary>Formatea (en el proceso elevado, ya validado). 0 si salió bien.</summary>
    internal static int Format(string root, string fileSystem, string label, bool quick, Action<string> error)
    {
        if (!CanFormat(root))
        {
            error(Loc.T("Esta unidad no se puede formatear."));
            return 2;
        }
        char letter = char.ToUpperInvariant(root[0]);
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_Volume"));
            foreach (ManagementObject volume in searcher.Get())
            {
                using (volume)
                {
                    if (volume["DriveLetter"] is not char found || char.ToUpperInvariant(found) != letter)
                        continue;
                    using var parameters = volume.GetMethodParameters("Format");
                    parameters["FileSystem"] = fileSystem;
                    parameters["FileSystemLabel"] = CleanLabel(label, fileSystem);
                    parameters["Full"] = !quick;
                    parameters["Force"] = true; // la desmonta aunque algo tenga abierto un archivo
                    using var result = volume.InvokeMethod("Format", parameters, null);
                    uint code = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
                    Log.Info($"Archivos (administrador): formatear {letter}: en {fileSystem} → {code}");
                    if (code != 0)
                        error(Loc.F("Windows no pudo formatear la unidad (código {0}).", code));
                    return code == 0 ? 0 : 1;
                }
            }
            error(Loc.T("No se encontró la unidad."));
            return 1;
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Log.Error($"Archivos (administrador): no se pudo formatear {letter}:", ex);
            error(ex.Message);
            return 1;
        }
    }
}
