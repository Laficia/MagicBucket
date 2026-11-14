using System;

namespace SrankFlag;

internal static class HuntWindowDisplay
{
    internal static bool Includes(string state, bool open) => state == (open ? "open" : "cd");

    internal static string Countdown(double seconds)
    {
        if (!double.IsFinite(seconds)) return "时间未知";
        long total = (long)Math.Ceiling(Math.Max(0, seconds));
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}";
    }
}
