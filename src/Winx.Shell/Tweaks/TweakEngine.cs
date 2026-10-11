using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Winx.Shell.Services;

namespace Winx.Shell.Tweaks;

public enum ApplyResult { Applied, Cancelled, Failed }

/// <summary>Lee y aplica ajustes del catálogo, guardando siempre el estado original.</summary>
public static class TweakEngine
{
    public static TweakBackup Backup { get; } = new();

    /// <summary>Algún ajuste aplicado necesita reiniciar el Explorador para verse.</summary>
    public static event Action? ExplorerRestartNeeded;

    /// <summary>Índice de la opción activa, o null si el registro no coincide con ninguna.</summary>
    public static int? CurrentState(Tweak tweak)
    {
        var effective = tweak.Settings.Select(Effective).ToArray();
        for (int option = 0; option < tweak.Choices.Length; option++)
        {
            int index = option;
            if (tweak.Settings.Select((s, i) => Same(effective[i], s.States[index])).All(x => x))
                return option;
        }
        return null;
    }

    public static async Task<ApplyResult> ApplyAsync(Tweak tweak, int state)
    {
        try
        {
            if (!tweak.RequiresAdmin || Elevation.IsAdmin)
            {
                ApplyLocal(tweak, state, Backup);
            }
            else
            {
                int code = await Elevation.RunSelfAsync("--apply-tweak", tweak.Id,
                    state.ToString(CultureInfo.InvariantCulture), Backup.FilePath);
                Backup.Reload();
                if (code == Elevation.Cancelled)
                    return ApplyResult.Cancelled;
                if (code != 0)
                    return ApplyResult.Failed;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo aplicar '{tweak.Id}'", ex);
            return ApplyResult.Failed;
        }

        Log.Info($"Ajuste '{tweak.Id}' → {tweak.Choices[state]}");
        RunRefresh(tweak.Refresh);
        return ApplyResult.Applied;
    }

    /// <summary>Restaura todos los valores respaldados. Devuelve false si quedó algo pendiente.</summary>
    public static async Task<bool> RestoreAllAsync()
    {
        var failed = Backup.Restore(Hive.CurrentUser);
        if (Backup.Data.Values.Values.Any(v => v.Hive == Hive.LocalMachine))
        {
            if (Elevation.IsAdmin)
                failed.AddRange(Backup.Restore(Hive.LocalMachine));
            else
            {
                await Elevation.RunSelfAsync("--restore-tweaks", Backup.FilePath);
                Backup.Reload();
            }
        }
        RunRefresh(TweakRefresh.Theme);
        RunRefresh(TweakRefresh.Mouse);
        ExplorerRestartNeeded?.Invoke();
        return failed.Count == 0 && Backup.Data.Values.Count == 0;
    }

    internal static void ApplyLocal(Tweak tweak, int state, TweakBackup backup)
    {
        backup.SaveOriginals(tweak.Settings);
        foreach (var s in tweak.Settings)
        {
            var target = s.States[state];
            if (target is null)
            {
                RegistryStore.DeleteValue(s.Hive, s.Path, s.Name);
                if (s.OwnerKey != null)
                    RegistryStore.DeleteTree(s.Hive, s.OwnerKey);
            }
            else
            {
                RegistryStore.Write(s.Hive, s.Path, s.Name, s.Kind, Convert(s.Kind, target));
            }
        }
    }

    /// <summary>Punto de entrada del proceso elevado (ver App.OnStartup).</summary>
    public static int RunElevatedCommand(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "--apply-tweak":
                    var tweak = TweakCatalog.All.Single(t => t.Id == args[1]);
                    ApplyLocal(tweak, int.Parse(args[2], CultureInfo.InvariantCulture), new TweakBackup(args[3]));
                    return 0;
                case "--restore-tweaks":
                    return new TweakBackup(args[1]).Restore(Hive.LocalMachine).Count == 0 ? 0 : 2;
                case "--restore-point":
                    SystemRestore.Create("WINX");
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Comando elevado falló: {string.Join(' ', args)}", ex);
        }
        return 1;
    }

    private static object? Effective(RegSetting s) =>
        RegistryStore.Read(s.Hive, s.Path, s.Name)?.Data ?? s.Default;

    // Compara como texto: el registro puede devolver "0" (REG_SZ) o 0 (DWORD) para lo mismo.
    private static bool Same(object? a, object? b) =>
        a is null || b is null
            ? a is null && b is null
            : string.Equals(System.Convert.ToString(a, CultureInfo.InvariantCulture),
                            System.Convert.ToString(b, CultureInfo.InvariantCulture),
                            StringComparison.OrdinalIgnoreCase);

    private static object Convert(RegistryValueKind kind, object value) => kind switch
    {
        RegistryValueKind.DWord => System.Convert.ToInt32(value, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
        _ => System.Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };

    // --- Aplicar en vivo lo que Windows solo lee al iniciar sesión ---

    private static void RunRefresh(TweakRefresh refresh)
    {
        try
        {
            switch (refresh)
            {
                case TweakRefresh.Theme:
                    Broadcast("ImmersiveColorSet");
                    break;
                case TweakRefresh.Explorer:
                    Broadcast(null);
                    ExplorerRestartNeeded?.Invoke();
                    break;
                case TweakRefresh.Mouse:
                    ApplyMouse();
                    break;
                case TweakRefresh.Snap:
                    var arranging = RegistryStore.Read(Hive.CurrentUser, @"Control Panel\Desktop", "WindowArrangementActive");
                    SystemParametersInfo(SPI_SETWINARRANGING, arranging?.Data is "0" ? 0u : 1u, IntPtr.Zero, SPIF_SENDCHANGE);
                    break;
                case TweakRefresh.StickyKeys:
                    ApplyStickyKeys();
                    break;
                default:
                    Broadcast("Policy");
                    Broadcast(null);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo refrescar ({refresh})", ex);
        }
    }

    private static void ApplyMouse()
    {
        int Read(string name, int fallback) =>
            int.TryParse(RegistryStore.Read(Hive.CurrentUser, @"Control Panel\Mouse", name)?.Data as string, out var v) ? v : fallback;
        var values = new[] { Read("MouseThreshold1", 6), Read("MouseThreshold2", 10), Read("MouseSpeed", 1) };
        var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            SystemParametersInfo(SPI_SETMOUSE, 0, handle.AddrOfPinnedObject(), SPIF_SENDCHANGE);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void ApplyStickyKeys()
    {
        var flagsText = RegistryStore.Read(Hive.CurrentUser, @"Control Panel\Accessibility\StickyKeys", "Flags")?.Data as string;
        if (!uint.TryParse(flagsText, out var flags))
            return;
        var sticky = new StickyKeysInfo { Size = (uint)Marshal.SizeOf<StickyKeysInfo>(), Flags = flags };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<StickyKeysInfo>());
        try
        {
            Marshal.StructureToPtr(sticky, ptr, false);
            SystemParametersInfo(SPI_SETSTICKYKEYS, sticky.Size, ptr, SPIF_SENDCHANGE);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void Broadcast(string? area) =>
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, area, 0x0002, 1000, out _);

    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPI_SETSTICKYKEYS = 0x003B;
    private const uint SPI_SETWINARRANGING = 0x0083;
    private const uint SPIF_SENDCHANGE = 0x01 | 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct StickyKeysInfo { public uint Size; public uint Flags; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string? lParam,
                                                    uint flags, uint timeout, out IntPtr result);
}

/// <summary>Punto de restauración del sistema (requiere administrador).</summary>
internal static class SystemRestore
{
    public static void Create(string description)
    {
        var info = new RestorePointInfo
        {
            EventType = 100,       // BEGIN_SYSTEM_CHANGE
            RestorePointType = 12, // MODIFY_SETTINGS
            Description = description,
        };
        if (!SRSetRestorePointW(ref info, out var status))
            throw new InvalidOperationException($"SRSetRestorePoint falló (estado {status.Status})");
        info.EventType = 101;      // END_SYSTEM_CHANGE
        info.SequenceNumber = status.SequenceNumber;
        SRSetRestorePointW(ref info, out _);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestorePointInfo
    {
        public int EventType;
        public int RestorePointType;
        public long SequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateMgrStatus { public int Status; public long SequenceNumber; }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
    private static extern bool SRSetRestorePointW(ref RestorePointInfo info, out StateMgrStatus status);
}
