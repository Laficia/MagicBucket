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
    internal static (double Timestamp,bool Maintenance) MainSiteLatest(JsonElement rows)
    {
        double latest=0;bool maintenance=false;
        if(rows.ValueKind!=JsonValueKind.Array)return (0,false);
        foreach(var row in rows.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Array||row.GetArrayLength()<2||row[1].ValueKind!=JsonValueKind.Number||
               !row[1].TryGetDouble(out double timestamp)||!double.IsFinite(timestamp)||timestamp<=0)continue;
            // /api/live tuples are [line, timestamp, isMaint]. The third value
            // is not the public Bear API's Canceled field.
            bool isMaint=row.GetArrayLength()>2&&row[2].ValueKind==JsonValueKind.True;
            if(timestamp>latest){latest=timestamp;maintenance=isMaint;}
            else if(timestamp==latest)maintenance|=isMaint;
        }
        return (latest,maintenance);
    }
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
