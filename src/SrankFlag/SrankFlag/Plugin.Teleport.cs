using System;
using System.Diagnostics;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace SrankFlag;

public sealed partial class Plugin
{
    private Func<bool>? preparationLocationGuard;
    private bool ownsArtisanCraft;
    private bool travelPreparationPending;

    private bool IsZoning() => condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51];
    private bool NativeTeleportReady(string world)
        => clientState.IsLoggedIn && objectTable.LocalPlayer != null && !Dead() && !IsZoning() &&
           WorldReady(world) && !condition[ConditionFlag.Casting] && !condition[ConditionFlag.InCombat] &&
           !condition[ConditionFlag.Crafting] && !condition[ConditionFlag.PreparingToCraft] && !condition[ConditionFlag.BoundByDuty];

    private bool AtTravelDestination(string world, string map)
        => clientState.IsLoggedIn && objectTable.LocalPlayer != null && !Dead() && !IsZoning() && WorldReady(world) && InMap(map);

    private void AbortTravel(string reason)
    {
        preparationLocationGuard = null;
        if (autoRunning || autoPaused) AutoStop();
        else StopAll(quiet: true);
        Print("[前往] " + reason + "；已停止后续制作、采集和触发动作。");
    }

    private bool EnsurePreparationLocation()
    {
        if (preparationLocationGuard == null || preparationLocationGuard()) return true;
        AbortTravel("目标服务器或地图未确认，取消触发准备");
        return false;
    }

    private void RunPrepAtDestination(string huntCn, string world, string map, Action then)
    {
        string expectedWorld = string.IsNullOrWhiteSpace(world) ? CurrentWorldName() : world;
        int generation = runGen;
        preparationLocationGuard = () => generation == runGen && AtTravelDestination(expectedWorld, map);
        if (!EnsurePreparationLocation()) return;
        travelPreparationPending = true;
        RunPrepThen(huntCn, () =>
        {
            if (!EnsurePreparationLocation()) return;
            preparationLocationGuard = null;
            travelPreparationPending = false;
            then();
        });
    }

    private AetheryteHit? ResolveNativeAetheryte(AetheryteHit destination)
    {
        var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        if (sheet == null) return null;
        if (destination.Id != 0)
        {
            var row = sheet.GetRow(destination.Id);
            return row.IsAetheryte && AetheryteUnlocked(row.RowId)
                ? destination with { Territory = row.Territory.RowId } : null;
        }
        var candidates = sheet.Where(row => row.IsAetheryte &&
            string.Equals(SafePlaceNameFrom(row), destination.Name, StringComparison.Ordinal) &&
            AetheryteUnlocked(row.RowId)).Take(2).ToArray();
        return candidates.Length == 1 ? destination with
        {
            Id = candidates[0].RowId,
            Territory = candidates[0].Territory.RowId
        } : null;
    }

    private unsafe bool TryNativeTeleport(AetheryteHit destination, out string reason)
    {
        reason = "";
        if (halted || !NativeTeleportReady(CurrentWorldName()))
        { reason = "当前角色状态无法传送"; return false; }
        var resolved = ResolveNativeAetheryte(destination);
        if (!resolved.HasValue)
        { reason = "目标大水晶未解锁或无法唯一识别：" + destination.Name; return false; }
        var target = resolved.Value;
        var telepo = Telepo.Instance();
        if (telepo == null)
        { reason = "游戏传送系统未就绪"; return false; }
        bool accepted = telepo->Teleport(target.Id, 0);
        log.Information($"[SrankFlag.Travel] 原生传送请求：{target.Name} · {target.Id} · 接受={accepted}");
        if (!accepted) reason = "游戏未接受传送：" + target.Name;
        return accepted;
    }

    private void TeleportConfirmed(AetheryteHit destination, string map, string world, Action arrived)
    {
        string expectedWorld = string.IsNullOrWhiteSpace(world) ? CurrentWorldName() : world;
        var resolved = ResolveNativeAetheryte(destination);
        if (!resolved.HasValue || resolved.Value.Territory == 0)
        { AbortTravel("找不到已解锁的目标大水晶：" + destination.Name); return; }
        var target = resolved.Value;
        WaitUntilOr(() => NativeTeleportReady(expectedWorld), () =>
        {
            bool alreadyThere = clientState.TerritoryType == target.Territory;
            if (!TryNativeTeleport(target, out string reason)) { AbortTravel(reason); return; }
            var gate = new TeleportArrivalGate(expectedWorld, target.Territory, alreadyThere);
            var elapsed = Stopwatch.StartNew();
            Sched(Poll, TimeSpan.FromMilliseconds(250));
            void Poll()
            {
                var state = gate.Observe(CurrentWorldName(), clientState.TerritoryType,
                    IsZoning(), NativeTeleportReady(expectedWorld), elapsed.Elapsed);
                if (state == TeleportArrivalState.Arrived)
                {
                    if (!AtTravelDestination(expectedWorld, map))
                    { AbortTravel("传送落地地图与目标不符：" + map); return; }
                    Print($"[前往] 已确认到达 {expectedWorld} · {map}（{target.Name}）");
                    arrived();
                }
                else if (state == TeleportArrivalState.TimedOut)
                    AbortTravel("90 秒内未确认传送落地：" + map);
                else Sched(Poll, TimeSpan.FromMilliseconds(250));
            }
        }, () => AbortTravel("角色状态未就绪，未开始传送：" + map), 60, 250);
    }
}
