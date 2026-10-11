using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Winx.Shell.Services.Themes.Glass;

/// <summary>
/// Efecto Liquid Glass: refracción de lente en los bordes, aberración cromática, saturación,
/// tinte y brillo especular sobre la imagen de fondo capturada. La imagen de entrada es más grande
/// que el vidrio (<see cref="Padding"/> por lado) para poder refractar contenido de alrededor.
/// </summary>
public sealed class LiquidGlassEffect : ShaderEffect
{
    // Coordenadas en píxeles de la imagen; el vidrio es el rectángulo redondeado interior.
    private const string Hlsl = """
        sampler2D input : register(s0);
        float2 imageSize : register(c0);
        float  padding   : register(c1);
        float  radius    : register(c2);
        float  edge      : register(c3);
        float  refraction: register(c4);
        float  saturation: register(c5);
        float4 tint      : register(c6);
        float  specular  : register(c7);
        float  aberration: register(c8);
        float  brightness: register(c9);

        float sdRoundRect(float2 p, float2 halfSize, float r)
        {
            float2 q = abs(p) - halfSize + r;
            return length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
        }

        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float2 p = uv * imageSize - imageSize * 0.5;
            float2 halfSize = imageSize * 0.5 - padding;
            float r = min(radius, min(halfSize.x, halfSize.y));
            float sd = sdRoundRect(p, halfSize, r);

            // Normal hacia afuera (gradiente de la distancia).
            float2 n = float2(sdRoundRect(p + float2(1, 0), halfSize, r) - sdRoundRect(p - float2(1, 0), halfSize, r),
                              sdRoundRect(p + float2(0, 1), halfSize, r) - sdRoundRect(p - float2(0, 1), halfSize, r));
            n = normalize(n + 0.00001);

            // Lente: 1 en el borde, 0 en el centro; curva suave para que el centro quede nítido.
            float t = saturate(1 - (-sd) / edge);
            float bend = t * t * t * refraction;
            float2 offset = n * bend / imageSize;          // se toma contenido de afuera: se "dobla" hacia dentro
            float2 ca = n * aberration * t * t / imageSize;

            float3 col;
            col.r = tex2D(input, uv + offset + ca).r;
            col.g = tex2D(input, uv + offset).g;
            col.b = tex2D(input, uv + offset - ca).b;

            float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
            col = lerp(lum.xxx, col, saturation) * brightness;
            col = lerp(col, tint.rgb, tint.a);

            // Brillo especular: luz desde arriba a la izquierda, reflejo tenue en el lado opuesto.
            float2 light = normalize(float2(-0.55, -0.85));
            float rim = pow(t, 5);
            col += rim * saturate(dot(n, light)) * specular;
            col += pow(t, 8) * saturate(dot(n, -light)) * specular * 0.4;

            float alpha = saturate(0.5 - sd);             // borde suavizado
            return float4(col * alpha, alpha);            // WPF usa alfa premultiplicado
        }
        """;

    private static readonly Lazy<PixelShader?> Shader = new(() =>
    {
        try
        {
            var shader = new PixelShader();
            shader.SetStreamSource(new MemoryStream(ShaderCompiler.Compile(Hlsl)));
            shader.Freeze();
            return shader;
        }
        catch (Exception ex)
        {
            Log.Error("Liquid Glass: no se pudo crear el shader", ex);
            return null;
        }
    });

    /// <summary>El equipo puede usar el shader (compiló y hay aceleración por hardware).</summary>
    public static bool IsSupported => Shader.Value != null && RenderCapability.IsPixelShaderVersionSupported(3, 0);

    public LiquidGlassEffect()
    {
        PixelShader = Shader.Value;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ImageSizeProperty);
        UpdateShaderValue(PaddingProperty);
        UpdateShaderValue(RadiusProperty);
        UpdateShaderValue(EdgeProperty);
        UpdateShaderValue(RefractionProperty);
        UpdateShaderValue(SaturationProperty);
        UpdateShaderValue(TintProperty);
        UpdateShaderValue(SpecularProperty);
        UpdateShaderValue(AberrationProperty);
        UpdateShaderValue(BrightnessProperty);
    }

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidGlassEffect), 0);

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public static readonly DependencyProperty ImageSizeProperty = Register(nameof(ImageSize), new Point(100, 100), 0);
    public static readonly DependencyProperty PaddingProperty = Register(nameof(Padding), 0.0, 1);
    public static readonly DependencyProperty RadiusProperty = Register(nameof(Radius), 16.0, 2);
    public static readonly DependencyProperty EdgeProperty = Register(nameof(Edge), 18.0, 3);
    public static readonly DependencyProperty RefractionProperty = Register(nameof(Refraction), 18.0, 4);
    public static readonly DependencyProperty SaturationProperty = Register(nameof(Saturation), 1.6, 5);
    public static readonly DependencyProperty TintProperty = Register(nameof(Tint), Color.FromArgb(40, 0, 0, 0), 6);
    public static readonly DependencyProperty SpecularProperty = Register(nameof(Specular), 0.55, 7);
    public static readonly DependencyProperty AberrationProperty = Register(nameof(Aberration), 1.5, 8);
    public static readonly DependencyProperty BrightnessProperty = Register(nameof(Brightness), 1.05, 9);

    /// <summary>Tamaño de la imagen de entrada en píxeles físicos (vidrio + padding).</summary>
    public Point ImageSize { get => (Point)GetValue(ImageSizeProperty); set => SetValue(ImageSizeProperty, value); }
    public double Padding { get => (double)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public double Edge { get => (double)GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }
    public double Refraction { get => (double)GetValue(RefractionProperty); set => SetValue(RefractionProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public double Specular { get => (double)GetValue(SpecularProperty); set => SetValue(SpecularProperty, value); }
    public double Aberration { get => (double)GetValue(AberrationProperty); set => SetValue(AberrationProperty, value); }
    public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }

    private static DependencyProperty Register(string name, object defaultValue, int register) =>
        DependencyProperty.Register(name, defaultValue.GetType(), typeof(LiquidGlassEffect),
            new UIPropertyMetadata(defaultValue, PixelShaderConstantCallback(register)));
}
