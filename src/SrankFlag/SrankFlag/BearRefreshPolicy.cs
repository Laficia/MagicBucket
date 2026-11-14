using System;
using System.Text.Json;

namespace SrankFlag;

internal static class BearRefreshPolicy
{
    internal static bool UsesMainSite(string dc)=>string.Equals(dc,"豆豆柴",StringComparison.Ordinal);
    internal static bool CanStart(string dc,bool manual,TimeSpan sinceLastFetch)
        => (manual||UsesMainSite(dc))&&sinceLastFetch>=(manual?TimeSpan.FromSeconds(10):TimeSpan.FromMinutes(15));
    internal static bool IsCanceled(JsonElement value)=>value.ValueKind==JsonValueKind.True||
        (value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out double number)&&number!=0);
    internal static string CompletionNotice(string dc,string origin,int records,int responses,int failures,bool interrupted)
    {
        string outcome=responses==0?"刷新失败":failures>0||interrupted?"刷新部分完成":"刷新完成";
        return $"{outcome}：{dc} · {origin} · {records} 条有效记录"+
            (failures>0?$" · {failures} 次请求失败":"")+
            (interrupted?" · 请求超时或中止，请重试":"");
    }
    internal static double ResolveDeath(double previous,double reported,double sonar,bool authoritative)
        => !double.IsFinite(reported)||reported<0?previous:authoritative?Math.Max(reported,double.IsFinite(sonar)?sonar:0):Math.Max(previous,reported);
    internal static double LatestKill(JsonElement response)
    {
        double latest=0;
        if(response.ValueKind!=JsonValueKind.Array)return 0;
        foreach(var record in response.EnumerateArray())
            if(record.ValueKind==JsonValueKind.Object&&
               (!record.TryGetProperty("Canceled",out var canceled)||!IsCanceled(canceled))&&
               record.TryGetProperty("Time",out var time)&&time.ValueKind==JsonValueKind.Number&&
               time.TryGetDouble(out double value)&&double.IsFinite(value)&&value>latest)latest=value;
        return latest;
    }
}
