using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;

namespace Strata.Shell.Services.Files;

/// <summary>Lo que se decidió para los nombres repetidos: qué se copia o mueve y a dónde.</summary>
internal sealed record ConflictPlan(IReadOnlyList<string> Sources, TransferConflict Conflict, Dictionary<string, string>? Targets = null);

/// <summary>
/// La pregunta de los nombres repetidos, como la de Windows: reemplazar, omitir, conservar ambos o
/// elegir archivo por archivo (comparando tamaño y fecha de los dos).
/// </summary>
internal static class ConflictPrompt
{
    private enum Choice { None, Replace, Skip, KeepBoth, Pick }

    /// <param name="repeated">Los orígenes cuyo nombre ya está en <paramref name="folder"/>.</param>
    public static async Task<ConflictPlan?> AskAsync(Window? owner, string title, IReadOnlyList<string> sources,
                                                     IReadOnlyList<string> repeated, string folder)
    {
        string folderName = FileTransfers.FolderNameOf(folder);
        bool one = repeated.Count == 1;
        string message = one
            ? Loc.F("En «{0}» ya hay algo llamado «{1}».", folderName, NameOf(repeated[0]))
            : Loc.F("En «{0}» ya hay {1} elementos con los mismos nombres.", folderName, repeated.Count);

        var choice = Choice.None;
        var options = new StackPanel();
        void Add(Choice value, SymbolRegular symbol, string text)
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Content = text,
                Icon = new SymbolIcon(symbol),
                Appearance = ControlAppearance.Secondary,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 6),
            };
            button.Click += (_, _) =>
            {
                choice = value;
                Window.GetWindow(button)?.Close();
            };
            options.Children.Add(button);
        }
        Add(Choice.Replace, SymbolRegular.Checkmark24,
            one ? Loc.T("Reemplazar el archivo en el destino") : Loc.T("Reemplazar los archivos en el destino"));
        Add(Choice.Skip, SymbolRegular.Dismiss24, one ? Loc.T("Omitir este archivo") : Loc.T("Omitir estos archivos"));
        Add(Choice.KeepBoth, SymbolRegular.Copy24, Loc.T("Conservar ambos"));
        Add(Choice.Pick, SymbolRegular.TaskListLtr24, Loc.T("Elegir para cada archivo"));

        await Controls.StrataDialog.ShowAsync(owner, Loc.T("Reemplazar u omitir archivos"), message, options, false, Loc.T("Cancelar"));
        return choice switch
        {
            Choice.Replace => new ConflictPlan(sources, TransferConflict.Replace),
            Choice.KeepBoth => new ConflictPlan(sources, TransferConflict.KeepBoth),
            Choice.Skip => new ConflictPlan(sources.Except(repeated).ToList(), TransferConflict.KeepBoth),
            Choice.Pick => await PickAsync(owner, sources, repeated, folder, folderName),
            _ => null,
        };
    }

    /// <summary>Una fila por nombre repetido: marcar el que llega, el que ya está o los dos.</summary>
    private static async Task<ConflictPlan?> PickAsync(Window? owner, IReadOnlyList<string> sources, IReadOnlyList<string> repeated,
                                                       string folder, string folderName)
    {
        var rows = new List<(string Source, CheckBox Incoming, CheckBox Existing)>();
        var list = new StackPanel();
        foreach (string source in repeated)
        {
            string name = NameOf(source);
            string existing = Path.Combine(folder, name);
            var (incomingInfo, existingInfo) = (Describe(source), Describe(existing));

            var header = new System.Windows.Controls.TextBlock
            {
                Text = name,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 10, 0, 4),
                ToolTip = name,
            };
            header.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            var incoming = Option(incomingInfo, incomingInfo.Time > existingInfo.Time);
            var kept = Option(existingInfo, existingInfo.Time > incomingInfo.Time);
            var grid = Columns(incoming, kept);
            list.Children.Add(header);
            list.Children.Add(grid);
            rows.Add((source, incoming, kept));
        }

        // Los de arriba marcan o desmarcan toda la columna.
        var allIncoming = new CheckBox { Content = Loc.T("Los que llegan"), IsChecked = false, FontWeight = FontWeights.SemiBold };
        var allExisting = new CheckBox { Content = Loc.F("Los que ya están en «{0}»", folderName), IsChecked = false, FontWeight = FontWeights.SemiBold };
        allIncoming.Click += (_, _) => rows.ForEach(r => r.Incoming.IsChecked = allIncoming.IsChecked == true);
        allExisting.Click += (_, _) => rows.ForEach(r => r.Existing.IsChecked = allExisting.IsChecked == true);

        var content = new StackPanel();
        content.Children.Add(Columns(allIncoming, allExisting));
        var separator = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 0) };
        separator.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        content.Children.Add(separator);
        content.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0),
        });

        if (await Controls.StrataDialog.ShowAsync(owner, Loc.T("Elegir qué conservar"),
                Loc.T("Si marcas los dos, el que llega se guarda con un número en el nombre. Si no marcas el que llega, se omite."),
                content, false, 620, Loc.T("Continuar"), Loc.T("Cancelar")) != 0)
            return null;

        // Lo que no tiene conflicto va a su lugar; lo repetido, a donde se eligió (o no va).
        var skipped = rows.Where(r => r.Incoming.IsChecked != true).Select(r => r.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(r => r.Incoming.IsChecked == true))
        {
            string name = NameOf(row.Source);
            targets[row.Source] = row.Existing.IsChecked == true
                ? Numbered(folder, name, Directory.Exists(row.Source), taken)
                : Path.Combine(folder, name);
        }
        var left = sources.Where(s => !skipped.Contains(s)).ToList();
        return new ConflictPlan(left, TransferConflict.Replace, targets);
    }

    /// <summary>"nombre (2)" libre en la carpeta (y que no use otro de esta misma vez).</summary>
    private static string Numbered(string folder, string name, bool isFolder, HashSet<string> taken)
    {
        string stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isFolder ? "" : Path.GetExtension(name);
        string target;
        for (int i = 2; ; i++)
        {
            target = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(target) && !Directory.Exists(target) && taken.Add(target))
                return target;
        }
    }

    private static string NameOf(string path) => Path.GetFileName(path.TrimEnd('\\'));

    private sealed record Info(string Size, DateTime Time);

    private static Info Describe(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return new Info(Loc.T("Carpeta"), Directory.GetLastWriteTime(path));
            var file = new FileInfo(path);
            return new Info(FileEntry.FormatSize(file.Length), file.LastWriteTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Info("", DateTime.MinValue);
        }
    }

    private static CheckBox Option(Info info, bool newer)
    {
        var text = new StackPanel();
        var date = new System.Windows.Controls.TextBlock
        {
            Text = info.Time == DateTime.MinValue ? "" : info.Time.ToString("g", CultureInfo.CurrentCulture)
                   + (newer ? "  · " + Loc.T("Más reciente") : ""),
            FontSize = 12,
        };
        date.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                                  newer ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        var size = new System.Windows.Controls.TextBlock { Text = info.Size, FontSize = 12 };
        size.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        text.Children.Add(date);
        text.Children.Add(size);
        return new CheckBox { Content = text, VerticalContentAlignment = VerticalAlignment.Top };
    }

    private static Grid Columns(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
}
