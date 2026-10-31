using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Strata.Shell.Services.Localization;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Strata.Shell.Services.Files;

/// <summary>Piezas de los menús contextuales de archivos (Archivos y el escritorio de Strata UI).</summary>
public static class FileMenus
{
    public static MenuItem Item(string header, SymbolRegular? icon, Action action, bool enabled = true, bool bold = false)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (icon is { } symbol)
            item.Icon = new SymbolIcon { Symbol = symbol };
        if (bold)
            item.FontWeight = FontWeights.SemiBold;
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"Menú de archivos: \"{header}\" falló", ex);
            }
        };
        return item;
    }

    /// <summary>
    /// Las opciones que Windows y las apps agregan al menú (NanaZip, un lector de PDF, Enviar a,
    /// Propiedades, Nuevo…), leídas en el momento en otro proceso (<see cref="ShellMenuSession"/>),
    /// también las de programas que se instalen después. Llegan ordenadas por categorías. Devuelve
    /// cuándo ya están en el menú (ver <see cref="OpenWhenReady"/>).
    /// </summary>
    public static Task AddWindowsItems(ContextMenu menu, IReadOnlyList<string> paths, bool background, bool desktop = false)
    {
        const string TerminalVerb = "{9F156763-7844-4DC4-B2B1-901F640F5155}";
        if (ShellMenuSession.Start(paths, background, desktop) is not { } session)
            return Task.CompletedTask;
        var separator = new Separator();
        var loading = new MenuItem { Header = Loc.T("Cargando opciones de Windows…"), IsEnabled = false };
        menu.Items.Add(separator);
        menu.Items.Add(loading);
        menu.Closed += Closed;
        var filled = FillAsync();
        Pending.AddOrUpdate(menu, filled);
        return filled;

        void Closed(object? sender, RoutedEventArgs e)
        {
            menu.Closed -= Closed;
            // Después del clic (si fue en una de estas, ya se pidió ejecutarla).
            menu.Dispatcher.BeginInvoke(session.Dispose, DispatcherPriority.Background);
        }

        async Task FillAsync()
        {
            var items = await session.Items;
            int index = menu.Items.IndexOf(loading);
            if (index < 0)
                return; // el menú ya se volvió a armar
            menu.Items.Remove(loading);
            // El fondo ya trae «Abrir terminal aquí» propio: el de Windows Terminal quedaría repetido.
            if (background)
                items = items.Where(i => !string.Equals(i.Verb, TerminalVerb, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0)
            {
                menu.Items.Remove(separator);
                return;
            }
            bool first = true;
            foreach (var (category, group) in Categorize(items))
            {
                if (!first)
                    menu.Items.Insert(index++, new Separator());
                first = false;
                menu.Items.Insert(index++, Caption(category));
                foreach (var item in group)
                    menu.Items.Insert(index++, WindowsItem(item, session));
            }
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContextMenu, Task> Pending = new();

    /// <summary>
    /// Abre el menú cuando ya tiene las opciones de Windows, para que aparezca completo y no crezca
    /// después; si Windows tarda (una extensión lenta), se abre igual con "Cargando…".
    /// Devuelve false si no hay nada que esperar (se abre como siempre).
    /// </summary>
    public static bool OpenWhenReady(ContextMenu menu, UIElement target, Point at)
    {
        if (!Pending.TryGetValue(menu, out var pending) || pending.IsCompleted)
            return false;
        _ = OpenAsync();
        return true;

        async Task OpenAsync()
        {
            await Task.WhenAny(pending, Task.Delay(1500));
            // Otro clic derecho mientras tanto volvió a armar el menú: lo abre ese, no este.
            if (!Pending.TryGetValue(menu, out var latest) || latest != pending || menu.IsOpen)
                return;
            // Donde se hizo clic (no donde esté ahora el puntero); al cerrarse vuelve a lo de siempre.
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
            menu.PlacementTarget = target;
            menu.HorizontalOffset = at.X;
            menu.VerticalOffset = at.Y;
            menu.Closed += Reset;
            menu.IsOpen = true;
        }

        void Reset(object? sender, RoutedEventArgs e)
        {
            menu.Closed -= Reset;
            menu.ClearValue(ContextMenu.PlacementProperty);
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
            menu.ClearValue(ContextMenu.HorizontalOffsetProperty);
            menu.ClearValue(ContextMenu.VerticalOffsetProperty);
        }
    }

    private enum Category { Open, Apps, Share, New, System }

    private static string CategoryName(Category category) => category switch
    {
        Category.Open => Loc.T("Abrir y editar"),
        Category.Share => Loc.T("Compartir y enviar"),
        Category.New => Loc.T("Nuevo"),
        Category.System => Loc.T("Sistema"),
        _ => Loc.T("Aplicaciones"),
    };

    /// <summary>Título de una sección (no se puede elegir).</summary>
    private static MenuItem Caption(Category category) => new()
    {
        Header = CategoryName(category),
        IsEnabled = false,
        IsHitTestVisible = false,
        Focusable = false,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
    };

    // Comienzos de texto que se reconocen aunque la opción no tenga verbo (en los idiomas de Windows más comunes).
    private static readonly string[] OpenWords = ["Open", "Edit", "Print", "Play", "Run ", "Abrir", "Editar", "Imprimir", "Reproducir", "Ejecutar",
        "Ouvrir", "Modifier", "Imprimer", "Öffnen", "Bearbeiten", "Drucken"];
    private static readonly string[] SystemWords = ["Include in library", "Incluir en", "Inclure dans", "In Bibliothek", "Restore previous",
        "Restaurar versiones", "Create shortcut", "Crear acceso directo", "Pin to", "Anclar"];
    private static readonly string[] ShareWords = ["Send", "Share", "Give access", "Enviar", "Compartir", "Conceder acceso", "Dar acceso",
        "Envoyer", "Partager", "Donner accès", "Senden", "Teilen", "Zugriff gewähren", "Partilhar", "Conceder acesso"];

    private static readonly HashSet<string> OpenVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "openas", "edit", "print", "printto", "play", "enqueue", "runas", "runasuser", "preview", "openwith" };
    private static readonly HashSet<string> ShareVerbs = new(StringComparer.OrdinalIgnoreCase) { "Windows.ModernShare", "Windows.Share", "sendto" };
    private static readonly HashSet<string> SystemVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "PreviousVersions", "link", "pintohomefile", "properties", "display", "personalize", "Windows.Library" };

    /// <summary>
    /// Ordena las opciones de Windows en secciones (abrir y editar, aplicaciones, compartir y
    /// enviar, nuevo, sistema) con Propiedades al final. Dentro de cada una queda el orden de Windows.
    /// </summary>
    private static IEnumerable<(Category, List<NativeMenuItem>)> Categorize(List<NativeMenuItem> items)
    {
        var groups = new SortedDictionary<Category, List<NativeMenuItem>>();
        NativeMenuItem? properties = null;
        foreach (var item in items.Where(i => !i.Separator))
        {
            if (string.Equals(item.Verb, "properties", StringComparison.OrdinalIgnoreCase))
            {
                properties = item;
                continue;
            }
            var category = CategoryOf(item);
            if (!groups.TryGetValue(category, out var list))
                groups[category] = list = [];
            list.Add(item);
        }
        if (properties != null)
        {
            if (!groups.TryGetValue(Category.System, out var list))
                groups[Category.System] = list = [];
            list.Add(properties);
        }
        return groups.Select(g => (g.Key, g.Value));
    }

    private static Category CategoryOf(NativeMenuItem item)
    {
        var verbs = item.Children is { } children ? children.Select(c => c.Verb).OfType<string>().ToList() : [];
        if (item.Verb is { } verb)
        {
            if (OpenVerbs.Contains(verb) || verb.StartsWith("open", StringComparison.OrdinalIgnoreCase))
                return Category.Open;
            if (ShareVerbs.Contains(verb))
                return Category.Share;
            if (SystemVerbs.Contains(verb))
                return Category.System;
        }
        // Submenús: "Abrir con" termina en "Elegir otra aplicación"; "Nuevo" trae "Carpeta".
        if (verbs.Contains("openas", StringComparer.OrdinalIgnoreCase))
            return Category.Open;
        if (verbs.Contains("NewFolder", StringComparer.OrdinalIgnoreCase) || verbs.Contains("NewLink", StringComparer.OrdinalIgnoreCase))
            return Category.New;
        if (StartsWithAny(item.Text, ShareWords))
            return Category.Share;
        if (StartsWithAny(item.Text, SystemWords))
            return Category.System;
        if (StartsWithAny(item.Text, OpenWords))
            return Category.Open;
        return Category.Apps;
    }

    private static bool StartsWithAny(string text, string[] words) =>
        words.Any(w => text.StartsWith(w, StringComparison.CurrentCultureIgnoreCase));

    private static Control WindowsItem(NativeMenuItem item, ShellMenuSession session)
    {
        if (item.Separator)
            return new Separator();
        // "_" es la tecla de acceso en WPF: se duplica para que se vea tal cual.
        var menuItem = new MenuItem { Header = item.Text.Replace("_", "__"), IsEnabled = item.Enabled, IsChecked = item.Checked };
        if (ShellMenuSession.IconOf(item) is { } icon)
            menuItem.Icon = new System.Windows.Controls.Image { Source = icon, Width = 16, Height = 16 };
        if (item.Children is { } children)
        {
            foreach (var child in children)
                menuItem.Items.Add(WindowsItem(child, session));
        }
        else
        {
            menuItem.Click += (_, _) => session.Invoke(item.Id);
        }
        return menuItem;
    }
}
