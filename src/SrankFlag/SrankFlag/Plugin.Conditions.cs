using System;
using System.Collections.Generic;
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
        Toggle=key=>
        {
            var pair = key.Split('\u0001', 2);
            if (pair.Length == 2) ToggleWatchHunt(pair[0], pair[1]);
            else Subscriptions.Toggle(key);
            lastConditionBuildUtc=DateTime.MinValue;
        },
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

    private IReadOnlyList<string> PinnedWorlds(string en)
        => prefs.watchedWorlds?.GetValueOrDefault(en) is { } worlds ? worlds : Array.Empty<string>();

    private bool IsWatchingHunt(string en, string world) => PinnedWorlds(en).Contains(world, StringComparer.Ordinal);

    private void ToggleWatchHunt(string en, string world)
    {
        var definition = ConditionCatalog.Find(en);
        if (definition == null || string.IsNullOrWhiteSpace(world)) return;
        prefs.watchedWorlds ??= new(StringComparer.Ordinal);
        if (!prefs.watchedWorlds.TryGetValue(en, out var worlds) || worlds == null)
            prefs.watchedWorlds[en] = worlds = [];
        bool removed = worlds.Remove(world);
        if (removed && worlds.Count == 0) prefs.watchedWorlds.Remove(en);
        if (!removed)
        {
            worlds.Add(world);
            OpenConditionFloating();
        }
        SavePrefs();
        lastConditionBuildUtc = DateTime.MinValue;
        Print($"{world} · {definition.Cn} {(removed ? "已移出悬浮窗" : "已加入悬浮窗")}");
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
        if (nativeHud==null || (DateTime.UtcNow-lastConditionBuildUtc).TotalMilliseconds<1000) return;
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
            var pinnedWorlds = PinnedWorlds(definition.En);
            bool showPinned = floating || !snapshot.OnlySubscribed;
            bool matchesHunt = search.Length == 0 || (definition.Cn + " " + definition.En + " " + definition.Method).Contains(search, StringComparison.OrdinalIgnoreCase);
            if ((!matchesHunt || (snapshot.OnlySubscribed && !subscribed)) &&
                (!showPinned || !pinnedWorlds.Any(world => matchesHunt || world.Contains(search, StringComparison.OrdinalIgnoreCase)))) continue;
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

            if (showPinned)
            {
                foreach (string world in pinnedWorlds.Distinct(StringComparer.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(world) || (!matchesHunt && !world.Contains(search, StringComparison.OrdinalIgnoreCase))) continue;
                    var pinned = new ConditionEntry
                    {
                        En = row.En, Name = row.Name, Method = row.Method, Timer = row.Timer, Rule = row.Rule,
                        Phase = row.Phase, Seconds = row.Seconds, Subscribed = true, PinnedWorld = world
                    };
                    var worldHunt = srankData?.dcs.FirstOrDefault(dc => dc.worlds.Contains(world))?.hunts.FirstOrDefault(h => h.en == definition.En);
                    pinned.Worlds.Add(BuildConditionWorld(worldHunt, world, now));
                    snapshot.Rows.Add(pinned);
                }
            }
            if (!matchesHunt || (snapshot.OnlySubscribed && !subscribed) || (floating && pinnedWorlds.Count > 0)) continue;
            var hunt=data?.hunts.FirstOrDefault(h=>h.en==definition.En);
            if(data!=null)
            {
                foreach(string world in data.worlds.Where(worldFilter.Contains))
                {
                    row.Worlds.Add(BuildConditionWorld(hunt, world, now));
                }
            }
            snapshot.Rows.Add(row);
        }
        snapshot.Rows.Sort((a,b)=>
        {
            int pinned = (b.PinnedWorld.Length != 0).CompareTo(a.PinnedWorld.Length != 0);
            if (pinned != 0) return pinned;
            if (!snapshot.OnlySubscribed)
            {
                int byName = string.CompareOrdinal(a.Name, b.Name);
                return byName != 0 ? byName : string.CompareOrdinal(a.PinnedWorld, b.PinnedWorld);
            }
            static int Priority(ConditionPhase phase)=>phase switch {ConditionPhase.Active=>0,ConditionPhase.Soon=>1,ConditionPhase.Waiting=>2,_=>3};
            int order=Priority(a.Phase).CompareTo(Priority(b.Phase));
            if(order!=0)return order;
            order=a.Seconds.CompareTo(b.Seconds);
            return order!=0?order:string.CompareOrdinal(a.Name,b.Name);
        });
        return snapshot;
    }

    private ConditionWorld BuildConditionWorld(HuntData? hunt, string world, long now)
    {
        var result = new ConditionWorld { Name = world, Detail = "无击杀记录" };
        var record = hunt?.worlds.FirstOrDefault(item => item.world == world);
        if (hunt == null || record == null || record.lastDeath <= 0) return result;
        var state = CalcState(hunt, record.lastDeath, now);
        result.Ready = state.State is "open" or "over";
        result.Detail = state.State switch
        {
            "open" => $"已进闸 {Math.Round(state.Pct)}%",
            "cd" => "进闸 " + HuntWindowDisplay.Countdown(state.Eta),
            "over" => "窗口已过",
            _ => "时间未知"
        };
        return result;
    }
}
