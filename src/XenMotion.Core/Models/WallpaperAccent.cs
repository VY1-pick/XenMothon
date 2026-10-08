namespace XenMotion.Core.Models;

public static class WallpaperAccent
{
    /// <summary>Dominant chromatic bucket; ignores black bars, white glare and transparent pixels.</summary>
    public static (byte Red, byte Green, byte Blue) Select(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length == 0 || bgra.Length % 4 != 0) throw new ArgumentException("Expected BGRA pixels.", nameof(bgra));
        var weights = new double[512];
        var red = new double[512]; var green = new double[512]; var blue = new double[512];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (bgra[i + 3] < 128 || max < 30 || min > 225) continue;
            double weight = .15 + (max - min) / 255d;
            int bin = (r >> 5) * 64 + (g >> 5) * 8 + (b >> 5);
            weights[bin] += weight; red[bin] += r * weight; green[bin] += g * weight; blue[bin] += b * weight;
        }
        int best = Array.IndexOf(weights, weights.Max());
        if (weights[best] == 0) return (96, 112, 120);
        return ((byte)(red[best] / weights[best]), (byte)(green[best] / weights[best]), (byte)(blue[best] / weights[best]));
    }
}
