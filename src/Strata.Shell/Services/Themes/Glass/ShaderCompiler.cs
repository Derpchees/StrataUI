using System.Runtime.InteropServices;
using System.Text;

namespace Strata.Shell.Services.Themes.Glass;

/// <summary>
/// Compila HLSL a bytecode de pixel shader en tiempo de ejecución con d3dcompiler_47.dll, que
/// viene con Windows. Evita depender de fxc.exe (Windows SDK) para compilar el proyecto.
/// </summary>
internal static class ShaderCompiler
{
    public static byte[] Compile(string source, string entryPoint = "main", string profile = "ps_3_0")
    {
        var bytes = Encoding.ASCII.GetBytes(source);
        int hr = D3DCompile(bytes, (IntPtr)bytes.Length, null, IntPtr.Zero, IntPtr.Zero, entryPoint, profile,
                            OptimizationLevel3, 0, out var code, out var errors);
        try
        {
            if (hr < 0 || code is null)
            {
                string message = errors is null ? $"0x{hr:X8}" : Marshal.PtrToStringAnsi(errors.GetBufferPointer()) ?? "";
                throw new InvalidOperationException($"No se pudo compilar el shader: {message}");
            }
            var result = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), result, 0, result.Length);
            return result;
        }
        finally
        {
            if (code != null) Marshal.ReleaseComObject(code);
            if (errors != null) Marshal.ReleaseComObject(errors);
        }
    }

    private const uint OptimizationLevel3 = 1 << 15;

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] IntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll")]
    private static extern int D3DCompile(byte[] srcData, IntPtr srcDataSize,
        [MarshalAs(UnmanagedType.LPStr)] string? sourceName, IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entryPoint, [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags1, uint flags2, out ID3DBlob? code, out ID3DBlob? errorMessages);
}
