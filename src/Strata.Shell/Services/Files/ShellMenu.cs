using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Strata.Shell.Services.Files;

/// <summary>Una opción del menú contextual de Windows, ya leída (texto, ícono, submenú).</summary>
public sealed record NativeMenuItem(int Id, string Text, string? Icon, bool Enabled, bool Checked, bool Separator,
                                   List<NativeMenuItem>? Children, string? Verb = null);

/// <summary>
/// Las opciones que Windows y las apps agregan al menú contextual (NanaZip, un lector de PDF, Enviar
/// a, Propiedades, Nuevo…), mezcladas en el menú de Archivos. Las extensiones del Shell se cargan
/// dentro de quien arma el menú: por eso lo arma un proceso aparte (<c>StrataUI.exe --shell-menu</c>)
/// que devuelve la lista de opciones y, si se elige una, la ejecuta; si una extensión falla, solo se
/// cae ese proceso. Sigue vivo mientras tenga ventanas abiertas (Propiedades) y se cierra solo.
/// </summary>
public sealed class ShellMenuSession : IDisposable
{
    public const string Arg = "--shell-menu";

    /// <summary>Lo que ya ofrece Archivos (o no sirve fuera del Explorador): no se repite.</summary>
    private static readonly HashSet<string> SkippedVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "explore", "opennewwindow", "opennewprocess", "opennewtab", "cut", "copy", "paste", "delete",
        "rename", "copyaspath", "pintohome", "pintostartscreen", "unpinfromhome", "refresh", "undo", "redo", "view",
        "arrange", "groupby",
    };

    private readonly Process _process;
    private bool _invoked;

    /// <summary>Las opciones de Windows (vacía si no se pudieron leer a tiempo).</summary>
    public Task<List<NativeMenuItem>> Items { get; }

    private ShellMenuSession(Process process, string request)
    {
        _process = process;
        Items = ReadItemsAsync(request);
    }

    /// <summary>
    /// Empieza a leer el menú de Windows de esos elementos, del fondo de la carpeta o, con
    /// <paramref name="desktop"/>, del fondo del escritorio (Configuración de pantalla, Personalizar,
    /// el panel de la tarjeta de video…).
    /// </summary>
    public static ShellMenuSession? Start(IReadOnlyList<string> paths, bool background, bool desktop = false)
    {
        try
        {
            // Uno ya abierto y esperando: arrancar el proceso tardaba más que leer el menú (segundos
            // contra medio segundo), y el menú se veía crecer de a poco.
            Process? process;
            lock (SpareLock)
            {
                process = _spare is { HasExited: false } ? _spare : null;
                _spare = null;
            }
            process ??= Launch();
            if (process is null)
                return null;
            string request = JsonSerializer.Serialize(new Request(paths.ToList(), background || desktop, (GetKeyState(VkShift) & 0x8000) != 0, desktop));
            var session = new ShellMenuSession(process, request);
            // El siguiente, cuando este ya leyó (para no competir con él).
            _ = session.Items.ContinueWith(_ => Prewarm(), TaskScheduler.Default);
            return session;
        }
        catch (Exception ex)
        {
            Log.Error("Archivos: no se pudo abrir el menú de Windows", ex);
            return null;
        }
    }

    private static readonly object SpareLock = new();
    private static Process? _spare;
    private static Timer? _spareExpiry;

    /// <summary>
    /// Deja un proceso del menú abierto y esperando, para que el próximo clic derecho no tenga que
    /// arrancarlo. Si en un rato no se usa, se cierra (no ocupa memoria de más).
    /// </summary>
    public static void Prewarm()
    {
        lock (SpareLock)
        {
            if (_spare is { HasExited: false })
                return;
            _spare?.Dispose();
            _spare = Launch(warm: true);
            _spareExpiry ??= new Timer(_ => DropSpare());
            _spareExpiry.Change(TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        }
    }

    private static void DropSpare()
    {
        lock (SpareLock)
        {
            if (_spare is null)
                return;
            try
            {
                _spare.StandardInput.Close(); // sin pedido, termina solo
            }
            catch (Exception)
            {
            }
            _spare.Dispose();
            _spare = null;
        }
    }

    private const string WarmArg = "--warm";

    private static Process? Launch(bool warm = false)
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(Arg);
            if (warm)
                start.ArgumentList.Add(WarmArg);
            return Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Archivos: no se pudo abrir el menú de Windows", ex);
            return null;
        }
    }

    private async Task<List<NativeMenuItem>> ReadItemsAsync(string request)
    {
        try
        {
            await _process.StandardInput.WriteLineAsync(request);
            await _process.StandardInput.FlushAsync();
            var line = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(6));
            return line is { Length: > 0 } ? JsonSerializer.Deserialize<List<NativeMenuItem>>(line) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or JsonException or InvalidOperationException)
        {
            Log.Info($"Archivos: el menú de Windows no respondió ({ex.Message.Trim()})");
            return [];
        }
    }

    /// <summary>Ejecuta la opción elegida (el proceso la ejecuta y sigue mientras tenga ventanas).</summary>
    public void Invoke(int id)
    {
        if (_invoked)
            return;
        _invoked = true;
        try
        {
            // El clic fue en Strata UI: lo que abra la opción (Propiedades, NanaZip) puede pasar al frente.
            AllowSetForegroundWindow(_process.Id);
            GetCursorPos(out var point);
            _process.StandardInput.WriteLine($"invoke {id} {point.X} {point.Y}");
            _process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Log.Error("Archivos: no se pudo ejecutar la opción del menú de Windows", ex);
        }
    }

    /// <summary>Se cerró el menú sin elegir una opción de Windows: el proceso termina.</summary>
    public void Dispose()
    {
        if (!_invoked)
        {
            try
            {
                _process.StandardInput.Close();
            }
            catch (Exception)
            {
            }
        }
        _process.Dispose();
    }

    /// <summary>Un ícono de una opción (PNG en base64) como imagen.</summary>
    public static ImageSource? IconOf(NativeMenuItem item)
    {
        if (item.Icon is null)
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(Convert.FromBase64String(item.Icon));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private sealed record Request(List<string> Paths, bool Background, bool Extended, bool Desktop = false);

    // ── Proceso aparte ──────────────────────────────────────────────────────

    /// <summary>Arma el menú, devuelve sus opciones y, si se elige una, la ejecuta. Solo rutas completas que existen.</summary>
    internal static int Run()
    {
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), true);
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        if (Environment.GetCommandLineArgs().Contains(WarmArg))
            WarmUp();
        Request? request;
        try
        {
            request = JsonSerializer.Deserialize<Request>(input.ReadLine() ?? "");
        }
        catch (JsonException)
        {
            return 2;
        }
        if (request is null || request.Paths.Count == 0
            || request.Paths.Any(p => !Path.IsPathFullyQualified(p) || (!File.Exists(p) && !Directory.Exists(p)))
            || (request.Background && request.Paths.Count != 1))
        {
            output.WriteLine("[]");
            return 2;
        }

        var owner = new OwnerWindow();
        var pidls = new List<IntPtr>();
        IContextMenu? menu = null;
        IntPtr hmenu = IntPtr.Zero;
        try
        {
            menu = request.Desktop ? DesktopMenu(owner.Handle)
                : request.Background ? BackgroundMenu(request.Paths[0], owner.Handle, pidls)
                : ItemsMenu(request.Paths, owner.Handle, pidls);
            if (menu is null)
            {
                output.WriteLine("[]");
                return 1;
            }
            owner.Menu = menu;
            hmenu = CreatePopupMenu();
            uint flags = CmfNormal | CmfExplore | (request.Extended ? CmfExtendedVerbs : 0);
            if (menu.QueryContextMenu(hmenu, 0, FirstCommand, LastCommand, flags) < 0)
            {
                output.WriteLine("[]");
                return 1;
            }
            output.WriteLine(JsonSerializer.Serialize(Read(menu, owner, hmenu)));

            // Espera la respuesta atendiendo los mensajes (algunas extensiones los necesitan).
            var answer = Task.Run(input.ReadLine);
            while (!answer.IsCompleted)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(15);
            }
            var parts = answer.Result?.Split(' ') ?? [];
            if (parts is not ["invoke", var idText, var xText, var yText]
                || !int.TryParse(idText, out int id) || id < FirstCommand || id > LastCommand)
                return 0;
            int.TryParse(xText, out int x);
            int.TryParse(yText, out int y);
            var invoke = new CmInvokeCommandInfoEx
            {
                cbSize = Marshal.SizeOf<CmInvokeCommandInfoEx>(),
                fMask = CmicMaskUnicode | CmicMaskPtInvoke,
                hwnd = owner.Handle,
                lpVerb = (IntPtr)(id - FirstCommand),
                lpVerbW = (IntPtr)(id - FirstCommand),
                nShow = SwShowNormal,
                ptInvoke = new NativePoint { X = x, Y = y },
                lpDirectoryW = request.Background ? request.Paths[0] : Path.GetDirectoryName(request.Paths[0]),
            };
            menu.InvokeCommand(ref invoke);
            WaitForWindows();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Menú de Windows: falló", ex);
            return 1;
        }
        finally
        {
            if (hmenu != IntPtr.Zero)
                DestroyMenu(hmenu);
            if (menu != null)
                Marshal.ReleaseComObject(menu);
            pidls.ForEach(Marshal.FreeCoTaskMem);
            owner.DestroyHandle();
        }
    }

    /// <summary>
    /// Mientras espera el pedido, arma y descarta los menús de un archivo, una carpeta y su fondo: así
    /// las extensiones (7-Zip, Defender, Git, Enviar a…) ya quedan cargadas y el menú de verdad sale en
    /// una fracción del tiempo (cargarlas era más de un segundo de cada clic derecho).
    /// </summary>
    private static void WarmUp()
    {
        string file = Environment.ProcessPath!;
        string folder = Path.GetDirectoryName(file)!;
        foreach (var kind in new[] { 0, 1, 2 })
        {
            // Llegó el pedido de verdad: lo cargado hasta acá ya sirve, no se lo hace esperar.
            if (RequestWaiting())
                return;
            var owner = new OwnerWindow();
            var pidls = new List<IntPtr>();
            IContextMenu? menu = null;
            IntPtr hmenu = IntPtr.Zero;
            try
            {
                menu = kind == 2 ? BackgroundMenu(folder, owner.Handle, pidls)
                    : ItemsMenu([kind == 0 ? file : folder], owner.Handle, pidls);
                if (menu is null)
                    continue;
                owner.Menu = menu;
                hmenu = CreatePopupMenu();
                if (menu.QueryContextMenu(hmenu, 0, FirstCommand, LastCommand, CmfNormal | CmfExplore) >= 0)
                    Read(menu, owner, hmenu);
            }
            catch (Exception ex)
            {
                Log.Info($"Menú de Windows: no se pudo preparar ({ex.Message.Trim()})");
            }
            finally
            {
                if (hmenu != IntPtr.Zero)
                    DestroyMenu(hmenu);
                if (menu != null)
                    Marshal.ReleaseComObject(menu);
                pidls.ForEach(Marshal.FreeCoTaskMem);
                owner.DestroyHandle();
            }
        }
    }

    /// <summary>Si ya llegó una línea entera por la entrada (sin leerla), o se cerró.</summary>
    private static bool RequestWaiting()
    {
        var buffer = new byte[4096];
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            if (!PeekNamedPipe(GetStdHandle(-10), pinned.AddrOfPinnedObject(), (uint)buffer.Length, out uint read, out uint available, IntPtr.Zero))
                return true;
            return available > read || Array.IndexOf(buffer, (byte)'\n', 0, (int)read) >= 0;
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>Recorre el menú (y sus submenús, que algunas extensiones llenan recién al abrirse).</summary>
    private static List<NativeMenuItem> Read(IContextMenu menu, OwnerWindow owner, IntPtr hmenu)
    {
        var items = new List<NativeMenuItem>();
        int count = GetMenuItemCount(hmenu);
        for (int i = 0; i < count; i++)
        {
            var info = new MenuItemInfo
            {
                cbSize = (uint)Marshal.SizeOf<MenuItemInfo>(),
                fMask = MiimFType | MiimState | MiimId | MiimSubMenu | MiimString | MiimBitmap,
            };
            var text = new StringBuilder(256);
            info.dwTypeData = Marshal.AllocCoTaskMem(512 * 2);
            info.cch = 511;
            try
            {
                if (!GetMenuItemInfo(hmenu, (uint)i, true, ref info))
                    continue;
                if ((info.fType & MftSeparator) != 0)
                {
                    items.Add(new NativeMenuItem(0, "", null, false, false, true, null));
                    continue;
                }
                string label = Marshal.PtrToStringUni(info.dwTypeData) ?? "";
                if ((info.fType & MftOwnerDraw) != 0 && label.Length == 0)
                    continue; // dibujada por la extensión: no hay texto que mostrar
                // "Anclar a Inicio de Strata UI" (el menú del Explorador de Strata UI): Archivos ya lo tiene.
                if (label.Contains("Strata UI", StringComparison.OrdinalIgnoreCase))
                    continue;
                string? verb = info.hSubMenu == IntPtr.Zero && info.wID >= FirstCommand && info.wID <= LastCommand
                    ? Verb(menu, info.wID - FirstCommand) : null;
                if (verb != null && SkippedVerbs.Contains(verb))
                    continue;
                List<NativeMenuItem>? children = null;
                if (info.hSubMenu != IntPtr.Zero)
                {
                    owner.Forward(WmInitMenuPopup, info.hSubMenu, (IntPtr)i);
                    children = Read(menu, owner, info.hSubMenu);
                    if (children.Count == 0)
                        continue;
                }
                items.Add(new NativeMenuItem((int)info.wID, Clean(label), Png(info.hbmpItem),
                                             (info.fState & (MfsDisabled | MfsGrayed)) == 0, (info.fState & MfsChecked) != 0, false, children, verb));
            }
            finally
            {
                Marshal.FreeCoTaskMem(info.dwTypeData);
            }
        }
        // Sin separadores repetidos ni en los bordes.
        var tidy = new List<NativeMenuItem>();
        foreach (var item in items)
        {
            if (item.Separator && (tidy.Count == 0 || tidy[^1].Separator))
                continue;
            tidy.Add(item);
        }
        while (tidy.Count > 0 && tidy[^1].Separator)
            tidy.RemoveAt(tidy.Count - 1);
        return tidy;
    }

    /// <summary>"&amp;Propiedades\tAlt+Entrar" → "Propiedades".</summary>
    private static string Clean(string label)
    {
        int tab = label.IndexOf('\t');
        if (tab >= 0)
            label = label[..tab];
        return label.Replace("&&", "\u0001").Replace("&", "").Replace('\u0001', '&').Trim();
    }

    private static string? Verb(IContextMenu menu, uint offset)
    {
        var buffer = Marshal.AllocCoTaskMem(512);
        try
        {
            return menu.GetCommandString((UIntPtr)offset, GcsVerbW, IntPtr.Zero, buffer, 255) >= 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    /// <summary>El ícono de la opción (si es un mapa de bits de verdad, no uno de los de sistema) como PNG en base64.</summary>
    private static string? Png(IntPtr bitmap)
    {
        if ((long)bitmap is >= -1 and <= 11)
            return null;
        if (GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out var info) == 0 || info.Width is <= 0 or > 64 || info.Height is <= 0 or > 64)
            return null;
        int width = info.Width, height = info.Height, stride = width * 4;
        var pixels = new byte[stride * height];
        var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) == 0)
                return null;
        }
        finally
        {
            DeleteDC(dc);
        }
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length && !hasAlpha; i += 4)
            hasAlpha = pixels[i] != 0;
        var source = BitmapSource.Create(width, height, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    /// <summary>Menú de los elementos elegidos (todos de la misma carpeta).</summary>
    private static IContextMenu? ItemsMenu(List<string> paths, IntPtr owner, List<IntPtr> pidls)
    {
        IShellFolder? parent = null;
        var children = new List<IntPtr>();
        foreach (var path in paths)
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) < 0)
                return null;
            pidls.Add(pidl);
            var iid = typeof(IShellFolder).GUID;
            if (SHBindToParent(pidl, ref iid, out var folder, out var child) < 0)
                return null;
            parent ??= (IShellFolder)folder;
            children.Add(child);
        }
        var menuIid = typeof(IContextMenu).GUID;
        return parent!.GetUIObjectOf(owner, (uint)children.Count, children.ToArray(), ref menuIid, IntPtr.Zero, out var menu) < 0
            ? null
            : menu as IContextMenu;
    }

    /// <summary>Menú del fondo del escritorio (el de la raíz del Shell, con lo que registran Windows y los controladores).</summary>
    private static IContextMenu? DesktopMenu(IntPtr owner)
    {
        if (SHGetDesktopFolder(out var desktop) < 0 || desktop is not IShellFolder folder)
            return null;
        var menuIid = typeof(IContextMenu).GUID;
        return folder.CreateViewObject(owner, ref menuIid, out var menu) < 0 ? null : menu as IContextMenu;
    }

    /// <summary>Menú del espacio libre de una carpeta (Nuevo, Propiedades de la carpeta…).</summary>
    private static IContextMenu? BackgroundMenu(string folder, IntPtr owner, List<IntPtr> pidls)
    {
        if (SHParseDisplayName(folder, IntPtr.Zero, out var pidl, 0, out _) < 0)
            return null;
        pidls.Add(pidl);
        var iid = typeof(IShellFolder).GUID;
        if (SHBindToObject(IntPtr.Zero, pidl, IntPtr.Zero, ref iid, out var bound) < 0 || bound is not IShellFolder shellFolder)
            return null;
        var menuIid = typeof(IContextMenu).GUID;
        return shellFolder.CreateViewObject(owner, ref menuIid, out var menu) < 0 ? null : menu as IContextMenu;
    }

    /// <summary>
    /// Lo elegido puede abrir ventanas de este proceso (Propiedades, Abrir con…): se espera a que se
    /// cierren, atendiendo sus mensajes, y luego termina.
    /// </summary>
    private static void WaitForWindows()
    {
        var started = Stopwatch.StartNew();
        var lastWindow = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromHours(2))
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(50);
            if (HasVisibleWindow())
                lastWindow.Restart();
            // Un momento de gracia: la ventana de Propiedades tarda en aparecer.
            else if (lastWindow.Elapsed > TimeSpan.FromSeconds(3))
                return;
        }
    }

    private static bool HasVisibleWindow()
    {
        uint self = (uint)Environment.ProcessId;
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self && IsWindowVisible(hwnd))
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Ventana oculta dueña del menú: le pasa al menú los mensajes de sus submenús (Enviar a, Abrir con).</summary>
    private sealed class OwnerWindow : System.Windows.Forms.NativeWindow
    {
        public IContextMenu? Menu { get; set; }

        public OwnerWindow() => CreateHandle(new System.Windows.Forms.CreateParams());

        /// <summary>Un mensaje como si viniera del menú (para que una extensión llene su submenú).</summary>
        public void Forward(int message, IntPtr wParam, IntPtr lParam)
        {
            if (Menu is IContextMenu3 menu3)
                menu3.HandleMenuMsg2((uint)message, wParam, lParam, out _);
            else if (Menu is IContextMenu2 menu2)
                menu2.HandleMenuMsg((uint)message, wParam, lParam);
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg is WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)
            {
                if (Menu is IContextMenu3 menu3 && menu3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var result) >= 0)
                {
                    m.Result = result;
                    return;
                }
                if (Menu is IContextMenu2 menu2 && menu2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) >= 0)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }

    private const uint FirstCommand = 1, LastCommand = 0x7FFF;
    private const uint CmfNormal = 0, CmfExplore = 0x4, CmfExtendedVerbs = 0x100, GcsVerbW = 4;
    private const int CmicMaskUnicode = 0x4000, CmicMaskPtInvoke = 0x20000000, SwShowNormal = 1;
    private const int WmInitMenuPopup = 0x117, WmDrawItem = 0x2B, WmMeasureItem = 0x2C, WmMenuChar = 0x120, VkShift = 0x10;
    private const uint MiimState = 0x1, MiimId = 0x2, MiimSubMenu = 0x4, MiimString = 0x40, MiimBitmap = 0x80, MiimFType = 0x100;
    private const uint MftSeparator = 0x800, MftOwnerDraw = 0x100, MfsDisabled = 0x2, MfsGrayed = 0x1, MfsChecked = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint cbSize, fMask, fType, fState, wID;
        public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CmInvokeCommandInfoEx
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        public string? lpParametersW;
        public string? lpDirectoryW;
        public string? lpTitleW;
        public NativePoint ptInvoke;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eaten, out IntPtr pidl, IntPtr attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumerator);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int CompareIDs(IntPtr param, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr owner, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr owner, uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref Guid iid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object result);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr result);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfoEx info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint max);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")]
    private static extern bool PeekNamedPipe(IntPtr pipe, IntPtr buffer, uint size, out uint read, out uint available, IntPtr left);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder, out IntPtr child);
    [DllImport("shell32.dll")]
    private static extern int SHBindToObject(IntPtr parent, IntPtr pidl, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder([MarshalAs(UnmanagedType.Interface)] out object folder);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MenuItemInfo info);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
}
