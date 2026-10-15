using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Strata.Shell.Services.StartMenu;

/// <summary>
/// Intercepta la tecla Windows pulsada sola para abrir el menú Inicio de Strata UI en lugar del de
/// Windows. Los atajos (Win+E, Win+R, Win+Shift+S…) no se tocan: si se pulsa otra tecla mientras
/// Win está abajo, la combinación sigue su curso normal; las excepciones son Win+X, que abre el menú del
/// clic derecho en Inicio de Strata UI, y Win+N, que abre su centro de notificaciones.
/// </summary>
/// <remarks>
/// Windows abre su Inicio al soltar Win sin haber pulsado otra tecla. Justo antes de dejar pasar
/// ese "soltar", se inyecta una tecla sin función (VK 0xFF): Windows lo interpreta como una
/// combinación y no abre su menú. La técnica es la misma que usan Open-Shell y AutoHotkey.
/// </remarks>
public static class WinKeyHook
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int VkLWin = 0x5B, VkRWin = 0x5C, VkMask = 0xFF, VkX = 0x58, VkN = 0x4E;
    private const int VkCapital = 0x14, VkNumLock = 0x90, VkScroll = 0x91;
    private const int VkVolumeMute = 0xAD, VkVolumeDown = 0xAE, VkVolumeUp = 0xAF;
    private const uint LlkhfInjected = 0x10;
    private const uint KeyEventFKeyUp = 0x0002;

    private const uint WmQuit = 0x0012;

    private static IntPtr _hook;
    private static HookProc? _proc; // referencia viva para el GC
    private static Thread? _thread;
    private static uint _threadId;
    private static bool _winDown;
    private static bool _comboUsed;
    private static bool _maskOnRelease;
    private static volatile bool _handleWin;
    private static volatile bool _handleWinN;
    private static volatile bool _watchLocks;

    /// <summary>Win se pulsó y soltó sola (se invoca en el hilo de la UI).</summary>
    public static event Action? WinPressed;

    /// <summary>Win+X: el menú del clic derecho en Inicio (se invoca en el hilo de la UI).</summary>
    public static event Action? WinXPressed;

    /// <summary>Win+N: el centro de notificaciones (se invoca en el hilo de la UI).</summary>
    public static event Action? WinNPressed;

    /// <summary>Se soltó Bloq Mayús, Bloq Num o Bloq Despl (código de tecla; en el hilo de la UI).</summary>
    public static event Action<int>? LockKeyPressed;

    /// <summary>
    /// Tecla de subir, bajar o silenciar el volumen (código de tecla; en el hilo de la UI). Con el
    /// OSD de Strata UI activo no llegan a Windows, que si no mostraría también el suyo: Strata UI cambia el
    /// volumen. Se atienden también las simuladas (teclados de macros, mandos).
    /// </summary>
    public static event Action<int>? VolumeKeyPressed;

    /// <summary>
    /// El hook vive en un hilo propio con su bucle de mensajes: si la UI se bloquea un momento,
    /// el teclado del sistema no se retrasa (Windows espera al hook en cada tecla) ni Windows
    /// desinstala el hook por tardar demasiado.
    /// </summary>
    /// <param name="winKey">Tecla Windows sola y Win+X (menú Inicio de Strata UI).</param>
    /// <param name="winN">Win+N (centro de notificaciones de Strata UI).</param>
    /// <param name="lockKeys">OSD: avisar de las teclas de bloqueo (sin interceptarlas) y atender las de volumen.</param>
    public static void Update(bool winKey, bool winN, bool lockKeys = false)
    {
        _handleWin = winKey;
        _handleWinN = winN;
        _watchLocks = lockKeys;
        bool enabled = winKey || winN || lockKeys;
        if (enabled && _thread == null)
        {
            var ready = new ManualResetEventSlim();
            _thread = new Thread(() => HookThread(ready)) { IsBackground = true, Name = "Strata UI WinKeyHook" };
            _thread.Start();
            ready.Wait(2000);
        }
        else if (!enabled && _thread != null)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(2000);
            _thread = null;
        }
    }

    private static void HookThread(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _proc = Callback;
        using (var module = Process.GetCurrentProcess().MainModule!)
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(module.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            Log.Error($"No se pudo instalar el hook de teclado (error {Marshal.GetLastWin32Error()})");
        ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
    }

    private static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (_watchLocks && info.VkCode is VkVolumeMute or VkVolumeDown or VkVolumeUp)
            {
                if ((int)wParam is WmKeyDown or WmSysKeyDown)
                {
                    int vk = info.VkCode;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => VolumeKeyPressed?.Invoke(vk));
                }
                return (IntPtr)1;
            }
            if ((info.Flags & LlkhfInjected) == 0)
            {
                int message = (int)wParam;
                bool isWin = info.VkCode is VkLWin or VkRWin;
                bool down = message is WmKeyDown or WmSysKeyDown;
                bool up = message is WmKeyUp or WmSysKeyUp;

                if (up && _watchLocks && info.VkCode is VkCapital or VkNumLock or VkScroll)
                {
                    int vk = info.VkCode;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => LockKeyPressed?.Invoke(vk));
                }

                if (isWin && down)
                {
                    if (!_winDown)
                        _comboUsed = _maskOnRelease = false;
                    _winDown = true;
                }
                else if (!isWin && down && _winDown && info.VkCode == VkN && _handleWinN)
                {
                    // Igual que Win+X: la N no llega a Windows y se enmascara al soltar Win.
                    _comboUsed = _maskOnRelease = true;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => WinNPressed?.Invoke());
                    return (IntPtr)1;
                }
                else if (!isWin && down && _winDown && info.VkCode == VkX && _handleWin)
                {
                    // Win+X: se abre el menú de Strata UI y la X no llega a Windows. Al soltar Win hay
                    // que enmascararlo, porque Windows solo vio Win y abriría su Inicio.
                    _comboUsed = _maskOnRelease = true;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => WinXPressed?.Invoke());
                    return (IntPtr)1;
                }
                else if (!isWin && down && _winDown)
                {
                    _comboUsed = true; // es un atajo: Windows se encarga
                }
                else if (isWin && up)
                {
                    _winDown = false;
                    if (!_comboUsed && _handleWin)
                    {
                        SendMaskKey();
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => WinPressed?.Invoke());
                    }
                    else if (_maskOnRelease)
                    {
                        SendMaskKey();
                    }
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static void SendMaskKey()
    {
        var inputs = new[]
        {
            new Input { Type = 1, Keyboard = new KeybdInput { Vk = VkMask } },
            new Input { Type = 1, Keyboard = new KeybdInput { Vk = VkMask, Flags = KeyEventFKeyUp } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT es una unión; se reserva el tamaño de MOUSEINPUT (el miembro más grande).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeybdInput Keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
}
