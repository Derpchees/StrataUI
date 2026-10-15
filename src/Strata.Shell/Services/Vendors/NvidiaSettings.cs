using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Vendors;

/// <summary>Un ajuste del perfil global del controlador de NVIDIA y sus opciones (null = el del controlador).</summary>
public sealed record NvidiaSetting(uint Id, string Title, string Description, (string Name, uint? Value)[] Options);

/// <summary>
/// Ajustes globales del controlador de NVIDIA (los de "Configuración global" del Panel de control),
/// con NVAPI. Se leen y se escriben en un proceso aparte (<c>StrataUI.exe --nvidia</c>): una falla del
/// controlador no tumba la barra. Escribir pide administrador. Lo que había antes se guarda en
/// <see cref="StrataSettings.NvidiaOriginals"/> para poder restaurarlo.
/// </summary>
public static class NvidiaSettings
{
    public const string Arg = "--nvidia";

    public static IReadOnlyList<NvidiaSetting> All { get; } =
    [
        new(0x1057EB71, Loc.T("Modo de administración de energía"),
            Loc.T("Máximo rendimiento: la tarjeta no baja su frecuencia entre cuadros (gasta más en reposo)."),
            [(Loc.T("Predeterminado del controlador"), null), (Loc.T("Máximo rendimiento"), 1)]),
        new(0x007BA09E, Loc.T("Modo de baja latencia"),
            Loc.T("El procesador prepara un solo cuadro por adelantado: menos retraso en juegos sin NVIDIA Reflex."),
            [(Loc.T("Predeterminado del controlador"), null), (Loc.T("Activado"), 1)]),
        new(0x00CE2691, Loc.T("Calidad del filtrado de texturas"),
            Loc.T("Alto rendimiento: unos FPS más a cambio de texturas un poco menos nítidas de lejos."),
            [(Loc.T("Predeterminado del controlador"), null), (Loc.T("Alto rendimiento"), 0x14)]),
        new(0x00AC8497, Loc.T("Tamaño de la caché de sombreadores"),
            Loc.T("Ilimitada: los juegos no vuelven a compilar sombreadores que ya tenían (menos tirones)."),
            [(Loc.T("Predeterminado del controlador"), null), (Loc.T("Ilimitada"), 0xFFFFFFFF)]),
    ];

    /// <summary>El equipo tiene una tarjeta NVIDIA (y su controlador).</summary>
    public static bool Available =>
        HardwareInfo.GpuNames.Any(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        && File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"));

    // ── Desde Strata UI ─────────────────────────────────────────────────────

    /// <summary>Valores actuales (null = sin cambiar, el del controlador). Null si no se pudo leer.</summary>
    public static async Task<Dictionary<uint, uint?>?> ReadAsync()
    {
        var file = TempFile();
        try
        {
            int code = await RunAsync(elevated: false, "get", string.Join(',', All.Select(s => s.Id.ToString(CultureInfo.InvariantCulture))), file);
            if (code != 0 || !File.Exists(file))
                return null;
            var values = new Dictionary<uint, uint?>();
            foreach (var line in File.ReadAllLines(file))
            {
                var parts = line.Split('=');
                if (parts.Length == 2 && uint.TryParse(parts[0], CultureInfo.InvariantCulture, out var id))
                    values[id] = uint.TryParse(parts[1], CultureInfo.InvariantCulture, out var value) ? value : null;
            }
            return values;
        }
        finally
        {
            TryDelete(file);
        }
    }

    /// <summary>Cambia un ajuste (null = volver al del controlador). Guarda antes el valor original.</summary>
    public static async Task<bool> SetAsync(NvidiaSetting setting, uint? value, uint? current)
    {
        var originals = Settings.Current.NvidiaOriginals;
        if (!originals.ContainsKey(setting.Id))
        {
            originals[setting.Id] = current;
            Settings.Save();
        }
        int code = await RunAsync(elevated: true, "set", Format(setting.Id, value));
        Log.Info($"NVIDIA {setting.Title} → {value?.ToString(CultureInfo.InvariantCulture) ?? "predeterminado"} (código {code})");
        return code == 0;
    }

    /// <summary>Vuelve a dejar todo como estaba antes de cambiarlo desde Strata UI.</summary>
    public static async Task<bool> RestoreAsync()
    {
        var originals = Settings.Current.NvidiaOriginals;
        if (originals.Count == 0)
            return true;
        int code = await RunAsync(elevated: true, "set", string.Join(',', originals.Select(p => Format(p.Key, p.Value))));
        if (code != 0)
            return false;
        Settings.Current.NvidiaOriginals.Clear();
        Settings.Save();
        return true;
    }

    private static string Format(uint id, uint? value) =>
        string.Create(CultureInfo.InvariantCulture, $"{id}={(value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "default")}");

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"strataui-nvidia-{Guid.NewGuid():N}.txt");

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
    }

    private static async Task<int> RunAsync(bool elevated, params string[] args)
    {
        try
        {
            if (elevated && !Tweaks.Elevation.IsAdmin)
                return await Tweaks.Elevation.RunSelfAsync([Arg, .. args]);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(Arg);
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo hablar con el controlador de NVIDIA", ex);
            return -1;
        }
    }

    // ── Proceso aparte ──────────────────────────────────────────────────────

    /// <summary>
    /// StrataUI.exe --nvidia get id1,id2 archivo | set id=valor[,id=default…]: solo los ajustes de
    /// <see cref="All"/>; "get" escribe "id=valor" (vacío si no está cambiado) en el archivo temporal.
    /// </summary>
    internal static int RunHelper(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[1] == "get")
            {
                var file = Path.GetFullPath(args[3]);
                if (!Path.GetFileName(file).StartsWith("strataui-nvidia-", StringComparison.Ordinal))
                    return 2;
                var ids = ParseIds(args[2]);
                if (ids is null)
                    return 2;
                using var drs = new Drs();
                File.WriteAllLines(file, ids.Select(id =>
                    string.Create(CultureInfo.InvariantCulture, $"{id}={(drs.Get(id) is { } v ? v.ToString(CultureInfo.InvariantCulture) : "")}")));
                return 0;
            }
            if (args.Length == 3 && args[1] == "set")
            {
                var changes = new List<(uint Id, uint? Value)>();
                foreach (var pair in args[2].Split(','))
                {
                    var parts = pair.Split('=');
                    if (parts.Length != 2 || !uint.TryParse(parts[0], CultureInfo.InvariantCulture, out var id) || All.All(s => s.Id != id))
                        return 2;
                    if (parts[1] == "default")
                        changes.Add((id, null));
                    else if (uint.TryParse(parts[1], CultureInfo.InvariantCulture, out var value))
                        changes.Add((id, value));
                    else
                        return 2;
                }
                using var drs = new Drs();
                foreach (var (id, value) in changes)
                    drs.Set(id, value);
                drs.Save();
                return 0;
            }
            return 2;
        }
        catch (Exception ex)
        {
            Log.Error("NVAPI falló", ex);
            return 3;
        }
    }

    private static List<uint>? ParseIds(string text)
    {
        var ids = new List<uint>();
        foreach (var part in text.Split(','))
        {
            if (!uint.TryParse(part, CultureInfo.InvariantCulture, out var id) || All.All(s => s.Id != id))
                return null;
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>Sesión de NVAPI DRS sobre el perfil global actual.</summary>
    private sealed unsafe class Drs : IDisposable
    {
        private const int SettingSize = 12320; // NVDRS_SETTING_V1
        private const uint SettingVersion = SettingSize | (1u << 16);
        private const int SettingNotFound = -160;

        private readonly IntPtr _session;
        private readonly IntPtr _profile;

        public Drs()
        {
            Check(Fn<NoArgs>(0x0150E828)(), "NvAPI_Initialize");
            Check(Fn<OutHandle>(0x0694D52E)(out _session), "NvAPI_DRS_CreateSession");
            Check(Fn<Handle>(0x375DBD6B)(_session), "NvAPI_DRS_LoadSettings");
            Check(Fn<HandleOut>(0x617BFF9F)(_session, out _profile), "NvAPI_DRS_GetCurrentGlobalProfile");
        }

        /// <summary>Valor del ajuste en el perfil, o null si no está cambiado (vale el del controlador).</summary>
        public uint? Get(uint id)
        {
            var buffer = stackalloc byte[SettingSize];
            new Span<byte>(buffer, SettingSize).Clear();
            *(uint*)buffer = SettingVersion;
            int result = Fn<GetSetting>(0x73BF8338)(_session, _profile, id, (IntPtr)buffer);
            if (result == SettingNotFound)
                return null;
            Check(result, "NvAPI_DRS_GetSetting");
            return *(uint*)(buffer + 8220); // currentValue.u32CurrentValue
        }

        public void Set(uint id, uint? value)
        {
            if (value is null)
            {
                int deleted = Fn<DeleteSetting>(0xE4A26362)(_session, _profile, id);
                if (deleted != SettingNotFound)
                    Check(deleted, "NvAPI_DRS_DeleteProfileSetting");
                return;
            }
            var buffer = stackalloc byte[SettingSize];
            new Span<byte>(buffer, SettingSize).Clear();
            *(uint*)buffer = SettingVersion;
            *(uint*)(buffer + 4100) = id;       // settingId
            *(uint*)(buffer + 4104) = 0;        // NVDRS_DWORD_TYPE
            *(uint*)(buffer + 8220) = value.Value;
            Check(Fn<SetSetting>(0x577DD202)(_session, _profile, (IntPtr)buffer), "NvAPI_DRS_SetSetting");
        }

        public void Save() => Check(Fn<Handle>(0xFCBC7E14)(_session), "NvAPI_DRS_SaveSettings");

        public void Dispose()
        {
            if (_session != IntPtr.Zero)
                Fn<Handle>(0xDAD9CFF8)(_session);
        }

        private static void Check(int result, string what)
        {
            if (result != 0)
                throw new InvalidOperationException($"{what} devolvió {result}");
        }

        private static T Fn<T>(uint id) where T : Delegate =>
            QueryInterface(id) is var pointer && pointer != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<T>(pointer)
                : throw new InvalidOperationException($"NVAPI no tiene la función 0x{id:X8}");

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgs();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OutHandle(out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Handle(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleOut(IntPtr handle, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSetting(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSetting(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSetting(IntPtr session, IntPtr profile, uint id);
    }
}
