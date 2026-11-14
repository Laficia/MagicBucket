using System;

namespace SrankFlag;

internal enum ConditionPhase { Waiting, Soon, Active, Unknown, Manual }
internal readonly record struct ConditionTime(ConditionPhase Phase, double Seconds);
internal static class ConditionClock
{
    internal static string TimerText(ConditionTime time, bool compact = false) => time.Phase switch
    {
        ConditionPhase.Active => (compact ? "剩 " : "条件已进入 · 剩余 ") + HuntWindowDisplay.Countdown(time.Seconds),
        ConditionPhase.Soon or ConditionPhase.Waiting => (compact ? "还有 " : "距条件开始 ") + HuntWindowDisplay.Countdown(time.Seconds),
        ConditionPhase.Manual => "无固定时间窗",
        _ => "推算范围内暂无窗口"
    };

    internal static string RangeText(double start, double end)
    {
        static string Clock(double value) => DateTimeOffset.FromUnixTimeSeconds((long)value).ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm:ss");
        return "北京时间 " + Clock(start) + " — " + Clock(end);
    }

    internal static ConditionTime Evaluate(bool timed, double? start, double? end, double now)
    {
        if (!timed) return new(ConditionPhase.Manual, 0);
        if (start == null || end == null || end <= now) return new(ConditionPhase.Unknown, 0);
        if (start <= now && now < end) return new(ConditionPhase.Active, end.Value-now);
        double remaining = start.Value-now;
        return new(remaining <= 300 ? ConditionPhase.Soon : ConditionPhase.Waiting, remaining);
    }
}
