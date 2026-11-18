using System;
using System.Linq;

namespace SrankFlag;

public sealed partial class Plugin
{
    private bool conditionWindowOpen;
    private bool conditionFloatingOpen;
    private bool conditionOnlySubscribed = true;
    private string conditionSearch = "";
    private ConditionSubscriptions? conditionSubscriptions;
    private DateTime lastConditionBuildUtc = DateTime.MinValue;
    private ConditionSubscriptions Subscriptions => conditionSubscriptions ??= new ConditionSubscriptions(pluginDirectory);

    private void ToggleConditions()
    {
        CancelPendingWindowClose();
        conditionWindowOpen=!conditionWindowOpen;
        lastConditionBuildUtc=DateTime.MinValue;
        nativeHud?.SetConditionsWindowOpen(conditionWindowOpen);

    }

    private ConditionActions BuildConditionActions() => new()
    {
        Search=text=> {conditionSearch=text;lastConditionBuildUtc=DateTime.MinValue;},
        Toggle=en=> {Subscriptions.Toggle(en);lastConditionBuildUtc=DateTime.MinValue;},
        SelectAll=selected=> {Subscriptions.SelectAll(selected);lastConditionBuildUtc=DateTime.MinValue;},
        ToggleView=()=> {conditionOnlySubscribed=!conditionOnlySubscribed;lastConditionBuildUtc=DateTime.MinValue;},
        ToggleFloating=OpenConditionFloating,
        Closed=()=> QueueNativeWindowClose(() => conditionWindowOpen=false)
    };

    private void ToggleConditionFloating()
    {
        CancelPendingWindowClose();
        conditionFloatingOpen = !conditionFloatingOpen;
        lastConditionBuildUtc = DateTime.MinValue;
    }

    private void OpenConditionFloating()
    {
        CancelPendingWindowClose();
        conditionFloatingOpen = true;
        lastConditionBuildUtc = DateTime.MinValue;
    }

    private ConditionActions BuildConditionFloatingActions() => new()
    {
        ToggleView = () =>
        {
            conditionWindowOpen = true;
            conditionOnlySubscribed = false;
            conditionSearch = "";
            lastConditionBuildUtc = DateTime.MinValue;
        },
        Closed = () => QueueNativeWindowClose(() => conditionFloatingOpen = false)
    };

    private void TickConditionUi()
    {
        if (nativeHud==null || (DateTime.UtcNow-lastConditionBuildUtc).TotalMilliseconds<250) return;
        lastConditionBuildUtc=DateTime.UtcNow;
        try
        {
            nativeHud.TickConditions(conditionWindowOpen, conditionWindowOpen ? BuildConditionSnapshot() : new ConditionSnapshot());
            nativeHud.TickConditionFloating(conditionFloatingOpen, conditionFloatingOpen ? BuildConditionSnapshot(floating: true) : new ConditionSnapshot());
        }
        catch(Exception ex) { log.Error(ex,"[魔法水桶] 条件订阅同步失败，其余窗口继续运行"); }
    }

    private ConditionSnapshot BuildConditionSnapshot(bool floating = false)
    {
        string search = floating || conditionOnlySubscribed ? "" : conditionSearch;
        var snapshot=new ConditionSnapshot { Dc=currentDc, Search=search, OnlySubscribed=floating || conditionOnlySubscribed,Total=ConditionCatalog.All.Count,Subscribed=ConditionCatalog.All.Count(x=>Subscriptions.Contains(x.En)) };
        var data=srankData?.data;
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach(var definition in ConditionCatalog.All)
        {
            bool subscribed=Subscriptions.Contains(definition.En);
            if(snapshot.OnlySubscribed&&!subscribed)continue;
            if(search.Length>0&&!(definition.Cn+" "+definition.En+" "+definition.Method).Contains(search,StringComparison.OrdinalIgnoreCase))continue;
            bool timed=ConditionForecast.HasSpec(definition.En);
            var window=timed?ConditionForecast.NextWindow(definition.En,now):null;
            var time=ConditionClock.Evaluate(timed,window?.Start,window?.End,now);
            var row=new ConditionEntry {En=definition.En,Name=definition.Cn,Method=definition.Method,Subscribed=subscribed,Phase=time.Phase,Seconds=time.Seconds};
            row.Timer=ConditionClock.TimerText(time,true);
            row.Rule=timed?ConditionForecast.LabelOf(definition.En):"行动 / 前置条件需自行满足；不代表已可触发";
            if(window.HasValue)
            {
                row.Rule+=" · "+ConditionClock.RangeText(window.Value.Start,window.Value.End);
            }
            var hunt=data?.hunts.FirstOrDefault(h=>h.en==definition.En);
            if(data!=null)
            {
                foreach(string world in data.worlds.Where(worldFilter.Contains))
                {
                    double death=hunt?.worlds.FirstOrDefault(x=>x.world==world)?.lastDeath??0;
                    string detail="无击杀记录";
                    bool ready=false;
                    if(hunt!=null&&death>0)
                    {
                        var state=CalcState(hunt,death,now);
                        ready=state.State is "open" or "over";
                        detail=state.State switch {"open"=>$"已进闸 {Math.Round(state.Pct)}%","cd"=>"进闸 "+HuntWindowDisplay.Countdown(state.Eta),"over"=>"窗口已过",_=>"时间未知"};
                    }
                    row.Worlds.Add(new ConditionWorld{Name=world,Detail=detail,Ready=ready});
                }
            }
            snapshot.Rows.Add(row);
        }
        snapshot.Rows.Sort((a,b)=>
        {
            if (!snapshot.OnlySubscribed) return string.CompareOrdinal(a.Name, b.Name);
            static int Priority(ConditionPhase phase)=>phase switch {ConditionPhase.Active=>0,ConditionPhase.Soon=>1,ConditionPhase.Waiting=>2,_=>3};
            int order=Priority(a.Phase).CompareTo(Priority(b.Phase));
            if(order!=0)return order;
            order=a.Seconds.CompareTo(b.Seconds);
            return order!=0?order:string.CompareOrdinal(a.Name,b.Name);
        });
        return snapshot;
    }
}
