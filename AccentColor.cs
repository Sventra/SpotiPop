using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpotiPop;

/// <summary>Picks a vivid glow colour from album art.</summary>
public static class AccentColor
{
    public static readonly Color Default = Color.FromRgb(0x1E, 0xD7, 0x60);

    public static Color From(BitmapSource? art)
    {
        if (art is null) return Default;

        const int size = 16;
        var scaled = new TransformedBitmap(art, new ScaleTransform((double)size / art.PixelWidth, (double)size / art.PixelHeight));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var width = bgra.PixelWidth;
        var pixels = new byte[width * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, width * 4, 0);

        // Weight each pixel by saturation and brightness so dull backgrounds don't win over the cover's real colour.
        double r = 0, g = 0, b = 0, total = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            double pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2];
            var max = Math.Max(pr, Math.Max(pg, pb));
            var min = Math.Min(pr, Math.Min(pg, pb));
            var saturation = max == 0 ? 0 : (max - min) / max;
            var weight = saturation * saturation * (max / 255) + 0.01;
            r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
        }
        r /= total; g /= total; b /= total;

        // Normalise brightness and push away from grey so the glow always reads.
        var peak = Math.Max(r, Math.Max(g, b));
        if (peak < 1) return Default;
        var gain = 255 / peak;
        r *= gain; g *= gain; b *= gain;
        var grey = (r + g + b) / 3;
        return Color.FromRgb(Clamp(grey + (r - grey) * 1.3), Clamp(grey + (g - grey) * 1.3), Clamp(grey + (b - grey) * 1.3));
    }

    /// <summary>Darkened accent for fills that must not compete with bright controls.</summary>
    public static Color Dim(Color c) => Color.FromRgb((byte)(c.R * 0.5), (byte)(c.G * 0.5), (byte)(c.B * 0.5));

    static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
