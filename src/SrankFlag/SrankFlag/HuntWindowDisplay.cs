using System;

namespace SrankFlag;

internal static class HuntWindowDisplay
{
    internal static bool Includes(string state, bool open) => open ? state is "open" or "over" : state == "cd";

    internal static (string State,double Eta,double Pct) Calculate(double lastDeath,double now,double cdH=0,double winH=0)
    {
        if(!double.IsFinite(lastDeath)||lastDeath<=0||!double.IsFinite(now))return ("unknown",0,0);
        double cd=cdH>0?cdH:84,window=winH>=0?winH:48;
        double opens=lastDeath+cd*3600,ends=opens+window*3600;
        if(now<opens)return ("cd",opens-now,0);
        // Reaching the maximum respawn time does not close the gate. It stays
        // available at 100% until a new kill/maintenance record starts cooldown.
        return ("open",Math.Max(0,ends-now),window>0?Math.Clamp((now-opens)/(window*3600)*100,0,100):100);
    }

    internal static string Countdown(double seconds)
    {
        if (!double.IsFinite(seconds)) return "时间未知";
        long total = (long)Math.Ceiling(Math.Max(0, seconds));
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}";
    }
}
