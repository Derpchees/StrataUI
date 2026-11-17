using System.IO;

namespace Strata.Shell.Services.StartMenu;

/// <summary>
/// Archivos y carpetas del usuario que coinciden con la búsqueda del menú Inicio, leídos del índice de
/// Windows Search (lo mismo que usa el buscador de Windows) por OLE DB con ADO: sin paquetes extra.
/// </summary>
public static class FileSearch
{
    private static bool _failed;

    /// <summary>Hasta <paramref name="count"/> rutas, las modificadas más recientemente primero. Lento: llamar fuera de la UI.</summary>
    public static List<string> Find(string query, int count)
    {
        var found = new List<string>();
        if (query.Trim().Length == 0)
            return found;
        if (_failed)
            return Walk(query.Trim(), count);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Lo de AppData (cachés, accesos directos de Inicio que ya son apps) no se busca, como en Windows.
        var sql = $"SELECT TOP {count * 2} System.ItemPathDisplay FROM SystemIndex " +
                  $"WHERE SCOPE='file:{Escape(profile)}' AND System.ItemNameDisplay LIKE '%{Like(query.Trim())}%' " +
                  $"AND NOT System.ItemPathDisplay LIKE '%\\AppData\\%' ORDER BY System.DateModified DESC";
        dynamic? connection = null;
        try
        {
            connection = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Connection", throwOnError: true)!)!;
            connection.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            dynamic rows = connection.Execute(sql);
            while (!(bool)rows.EOF && found.Count < count)
            {
                if (rows.Fields.Item(0).Value is string path && !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !IsBuildOutput(path))
                    found.Add(path);
                rows.MoveNext();
            }
            rows.Close();
        }
        catch (Exception ex)
        {
            // Sin el servicio de Windows Search (deshabilitado en algunos equipos) no hay índice: se
            // recorren las carpetas del usuario y no se vuelve a intentar en cada tecla.
            _failed = true;
            Log.Info("Sin índice de Windows Search, se buscan los archivos recorriendo las carpetas: " + ex.Message);
            return Walk(query.Trim(), count);
        }
        finally
        {
            try { connection?.Close(); } catch { }
        }
        return found;
    }

    /// <summary>Sin índice: busca por nombre en Escritorio, Documentos, Descargas, Imágenes, Música y Videos.</summary>
    private static List<string> Walk(string query, int count)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var found = new List<(string Path, DateTime At)>();
        foreach (var folder in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos })
            Collect(Environment.GetFolderPath(folder));
        Collect(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        return found.OrderByDescending(f => f.At).Take(count).Select(f => f.Path).ToList();

        void Collect(string root)
        {
            if (!Directory.Exists(root))
                return;
            try
            {
                // Medio segundo en total como mucho: carpetas enormes no deben frenar la búsqueda.
                foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
                {
                    if (watch.ElapsedMilliseconds > 500 || found.Count >= count * 4)
                        return;
                    if (entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) && !IsBuildOutput(entry.FullName)
                        && !entry.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        found.Add((entry.FullName, entry.LastWriteTime));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // Copias de compilación y dependencias de proyectos: repiten los mismos nombres una y otra vez.
    private static readonly string[] Noise = ["\\bin\\", "\\obj\\", "\\node_modules\\", "\\.git\\", "\\.vs\\"];

    internal static bool IsBuildOutput(string path) => Noise.Any(n => path.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Escape(string text) => text.Replace("'", "''");

    // En LIKE, % _ y [ son comodines: entre corchetes valen como texto.
    internal static string Like(string text) =>
        Escape(text).Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
