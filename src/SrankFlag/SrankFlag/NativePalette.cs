using System;
using System.Numerics;

namespace SrankFlag;

// Pure color calculations, kept separate from native UI access for offline verification.
internal static class NativePalette
{
    internal static double Luminance(Vector4 color)
    {
        static double Linear(float c) => c <= 0.04045f ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(color.X) + 0.7152 * Linear(color.Y) + 0.0722 * Linear(color.Z);
    }

    internal static bool IsLight(Vector4 nativeText) => Luminance(nativeText) < 0.25;

    internal static Vector4 Text(Vector4 source, Vector4 nativeText)
    {
        bool light = IsLight(nativeText);
        float high = MathF.Max(source.X, MathF.Max(source.Y, source.Z));
        float low = MathF.Min(source.X, MathF.Min(source.Y, source.Z));
        if (light && high - low < 0.18f)
        {
            // Preserve the native theme's warm/cool text tint instead of imposing black.
            float muted = Math.Clamp((1f - high) * 0.04f, 0f, 0.025f);
            source = new Vector4(nativeText.X + muted, nativeText.Y + muted, nativeText.Z + muted, source.W);
        }

        double luminance = Luminance(source);
        // Clear White panels can be translucent grey, substantially darker than white.
        double limit = light ? 0.04 : 0.36;
        if ((light && luminance <= limit) || (!light && luminance >= limit))
            return source;

        Vector4 target = light ? new Vector4(0f, 0f, 0f, source.W) : new Vector4(1f, 1f, 1f, source.W);
        float left = 0f, right = 1f;
        for (int i = 0; i < 20; i++)
        {
            float amount = (left + right) * 0.5f;
            double next = Luminance(Vector4.Lerp(source, target, amount));
            if (light ? next > limit : next < limit)
                left = amount;
            else
                right = amount;
        }
        return Vector4.Lerp(source, target, right);
    }

    internal static Vector4 Surface(Vector4 source, Vector4 nativeText, bool rowSurface = false)
    {
        if (!IsLight(nativeText)) return source;
        float high = MathF.Max(source.X, MathF.Max(source.Y, source.Z));
        float low = MathF.Min(source.X, MathF.Min(source.Y, source.Z));
        // Only neutral dark tracks/row surfaces change. Semantic fills keep their hue.
        if (high - low >= 0.25f || Luminance(source) > 0.16) return source;
        return rowSurface
            ? new Vector4(0.98f - source.X * 0.25f, 0.98f - source.Y * 0.25f, 0.98f - source.Z * 0.25f, source.W)
            : new Vector4(0.58f + source.X * 0.25f, 0.58f + source.Y * 0.25f, 0.58f + source.Z * 0.25f, source.W);
    }
}
