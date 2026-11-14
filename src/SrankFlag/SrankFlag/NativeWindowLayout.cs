using System;

namespace SrankFlag;

internal static class NativeWindowLayout
{
    internal readonly record struct Main(float WorldX, float WorldWidth, float NameX, float NameWidth, float ProgressX, float ProgressWidth);
    internal static Main MainColumns(float width)
    {
        float world = Math.Clamp(width * .17f, 64, 180);
        float name = Math.Clamp(width * .25f, 94, 360);
        float nameX = 8 + world + 8, progressX = nameX + name + 8;
        return new Main(8, world, nameX, name, progressX, MathF.Max(48, width - progressX - 42));
    }
    internal static float[] HistoryColumns(float width) => [width*.22f,width*.31f,width*.20f,width*.12f,width*.15f];
}
