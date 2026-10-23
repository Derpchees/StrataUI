using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using MediaPlayer = Windows.Media.Playback.MediaPlayer;

namespace Strata.Shell.Services.Files;

/// <summary>
/// El reproductor moderno de Windows (MediaPlayer de WinRT, con los códecs de la Tienda: webm, VP9,
/// AV1, HEVC, ogg, opus…) dibujando en WPF. El MediaElement de WPF usa la vía antigua de Windows
/// Media y no abre esos formatos. Cada fotograma se copia a una textura de Direct3D (modo servidor
/// de fotogramas), de ahí a memoria y a un <see cref="WriteableBitmap"/> que muestra la ventana.
/// </summary>
public sealed class FramePlayer : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly object _gate = new();
    private ID3D11Texture2D? _target, _staging;
    private IDirect3DSurface? _surface;
    private int _width, _height;
    private byte[] _pixels = [];
    private bool _pending, _disposed;

    public MediaPlayer Player { get; }

    /// <summary>El fotograma actual (cambia de objeto si cambia el tamaño del video).</summary>
    public WriteableBitmap? Frame { get; private set; }

    /// <summary>Hay un <see cref="Frame"/> nuevo (otro tamaño): la imagen de la ventana debe usarlo.</summary>
    public event Action? FrameChanged;

    public FramePlayer(Dispatcher ui)
    {
        _ui = ui;
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                                [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                                out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        _device = device;
        _context = context;
        Player = new MediaPlayer { AutoPlay = false, IsVideoFrameServerEnabled = true };
        Player.VideoFrameAvailable += OnFrameAvailable;
    }

    /// <summary>Copia el fotograma (en un hilo del reproductor) y pide mostrarlo si no hay otro esperando.</summary>
    private void OnFrameAvailable(MediaPlayer sender, object args)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            int width = (int)sender.PlaybackSession.NaturalVideoWidth, height = (int)sender.PlaybackSession.NaturalVideoHeight;
            if (width <= 0 || height <= 0)
                return;
            try
            {
                if (width != _width || height != _height || _surface is null)
                    CreateTextures(width, height);
                sender.CopyFrameToVideoSurface(_surface);
                _context.CopyResource(_staging!, _target!);
                var mapped = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    int stride = width * 4;
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, _pixels, y * stride, stride);
                }
                finally
                {
                    _context.Unmap(_staging!, 0);
                }
            }
            catch (Exception ex) when (ex is COMException or SharpGen.Runtime.SharpGenException or ArgumentException)
            {
                Log.Info($"Visor: no se pudo copiar un fotograma ({ex.Message.Trim()})");
                return;
            }
            if (_pending)
                return;
            _pending = true;
        }
        _ui.BeginInvoke(Present, DispatcherPriority.Render);
    }

    private void Present()
    {
        lock (_gate)
        {
            _pending = false;
            if (_disposed || _width == 0)
                return;
            bool resized = Frame is null || Frame.PixelWidth != _width || Frame.PixelHeight != _height;
            if (resized)
                Frame = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgr32, null);
            Frame!.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
            if (resized)
                FrameChanged?.Invoke();
        }
    }

    private void CreateTextures(int width, int height)
    {
        _target?.Dispose();
        _staging?.Dispose();
        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        };
        _target = _device.CreateTexture2D(description);
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        _staging = _device.CreateTexture2D(description);

        // La textura como superficie de WinRT (lo que pide CopyFrameToVideoSurface).
        using var dxgiSurface = _target.QueryInterface<IDXGISurface>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var inspectable));
        try
        {
            _surface = WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
        (_width, _height) = (width, height);
        _pixels = new byte[width * height * 4];
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        Player.VideoFrameAvailable -= OnFrameAvailable;
        Player.Pause();
        Player.Source = null;
        Player.Dispose();
        (_surface as IDisposable)?.Dispose();
        _target?.Dispose();
        _staging?.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);
}
