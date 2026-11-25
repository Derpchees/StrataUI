using System.Text;
using System.Windows;
using System.Windows.Media;
using QRCoder;

namespace Strata.Shell.Services.Network;

/// <summary>
/// Código QR para unirse a una red Wi-Fi (formato <c>WIFI:T:WPA;S:…;P:…;;</c> que leen las cámaras
/// de Android e iPhone). Se dibuja como una sola geometría vectorial: nítida a cualquier escala.
/// </summary>
public static class WifiQr
{
    /// <summary>El texto del QR; "" como clave = red abierta.</summary>
    internal static string Payload(string ssid, string key) =>
        key.Length == 0
            ? $"WIFI:T:nopass;S:{Escape(ssid)};;"
            : $"WIFI:T:WPA;S:{Escape(ssid)};P:{Escape(key)};;";

    /// <summary>Los caracteres especiales del formato van precedidos de \.</summary>
    internal static string Escape(string value)
    {
        var text = new StringBuilder(value.Length + 4);
        foreach (char c in value)
        {
            if (c is '\\' or ';' or ',' or ':' or '"')
                text.Append('\\');
            text.Append(c);
        }
        return text.ToString();
    }

    /// <summary>Los módulos oscuros del QR (con margen de 4) en un cuadrado de 1×1 por módulo.</summary>
    public static Geometry Create(string ssid, string key)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(Payload(ssid, key), QRCodeGenerator.ECCLevel.M, forceUtf8: true);
        var matrix = data.ModuleMatrix; // ya incluye la zona de silencio de 4 módulos
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            for (int y = 0; y < matrix.Count; y++)
            {
                var row = matrix[y];
                for (int x = 0; x < row.Length; x++)
                {
                    if (!row[x])
                        continue;
                    int start = x;
                    while (x + 1 < row.Length && row[x + 1])
                        x++; // tramos horizontales seguidos: menos figuras y sin costuras entre módulos
                    context.BeginFigure(new Point(start, y), true, true);
                    context.PolyLineTo([new Point(x + 1, y), new Point(x + 1, y + 1), new Point(start, y + 1)], false, false);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
