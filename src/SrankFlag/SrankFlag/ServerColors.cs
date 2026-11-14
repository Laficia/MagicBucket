using System;
using System.Collections.Generic;
using System.Numerics;

namespace SrankFlag;

internal static class ServerColors
{
    // Main site's SRK_float server badge palette, read on 2026-11-14.
    private static readonly Dictionary<string, uint> Site = new(StringComparer.Ordinal)
    {
        ["水晶塔"] = 0xf5a742, ["银泪湖"] = 0x15c238, ["太阳海岸"] = 0xb1c215,
        ["伊修加德"] = 0x70b8ff, ["红茶川"] = 0xff697f
    };
    internal static Vector4 ForWorld(string world)
    {
        if (Site.TryGetValue(world, out uint rgb)) return new((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
        uint hash = 2166136261;
        foreach (char c in world) hash = unchecked((hash ^ c) * 16777619);
        float hue = hash % 3600 / 600f, saturation = .55f + ((hash >> 12) % 16) / 100f;
        float chroma = .95f * saturation, x = chroma * (1 - MathF.Abs(hue % 2 - 1)), m = .95f - chroma;
        var color = (int)hue switch { 0 => new Vector3(chroma,x,0), 1 => new Vector3(x,chroma,0), 2 => new Vector3(0,chroma,x), 3 => new Vector3(0,x,chroma), 4 => new Vector3(x,0,chroma), _ => new Vector3(chroma,0,x) };
        return new Vector4(color + new Vector3(m), 1);
    }
}
