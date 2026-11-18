using System;
using System.Linq;

namespace SrankFlag;

public sealed partial class Plugin
{
    private HuntVisibility? huntVisibility;
    private bool hiddenHuntsOpen;
    private DateTime lastHiddenHuntBuildUtc;
    private HuntVisibility Visibility => huntVisibility ??= new HuntVisibility(pluginDirectory, message => log.Warning("[魔法水桶] " + message));

    private void ToggleHiddenHunts()
    {
        CancelPendingWindowClose();
        hiddenHuntsOpen = !hiddenHuntsOpen;
        lastHiddenHuntBuildUtc = DateTime.MinValue;
        nativeHud?.ForceMainRefresh();
    }
    private void HideHunt(string en)
    {
        var definition = ConditionCatalog.Find(en);
        if (definition == null) return;
        try
        {
            Visibility.Hide(en);
            expandedHunts.Remove(en);
            autoHunts.Remove(definition.Cn);
            Print("已隐藏「" + definition.Cn + "」的全部服务器条目，可从“已隐藏”恢复。");
            nativeHud?.ForceMainRefresh();
        }
        catch (Exception ex) { log.Error(ex, "[魔法水桶] 保存隐藏列表失败"); Print("隐藏设置未保存：" + ex.Message); }
    }
    private HiddenHuntActions BuildHiddenHuntActions() => new()
    {
        Restore = en =>
        {
            try
            {
                Visibility.Restore(en);
                Print("已恢复「" + (ConditionCatalog.Find(en)?.Cn ?? en) + "」，主列表按当前筛选显示。");
                nativeHud?.ForceMainRefresh();
            }
            catch (Exception ex) { log.Error(ex, "[魔法水桶] 恢复隐藏项失败"); Print("恢复失败：" + ex.Message); }
        },
        RestoreAll = () =>
        {
            try { Visibility.RestoreAll(); nativeHud?.ForceMainRefresh(); Print("已恢复全部隐藏的 S 怪。"); }
            catch (Exception ex) { log.Error(ex, "[魔法水桶] 恢复全部隐藏项失败"); Print("恢复失败：" + ex.Message); }
        },
        Closed = () => QueueNativeWindowClose(() => hiddenHuntsOpen = false)
    };
    private HiddenHuntSnapshot BuildHiddenHuntSnapshot()
    {
        var snapshot = new HiddenHuntSnapshot();
        snapshot.Rows.AddRange(Visibility.Hidden.Select(en => new HiddenHuntEntry(en, ConditionCatalog.Find(en)?.Cn ?? en))
            .OrderBy(row => row.Name, StringComparer.Ordinal));
        return snapshot;
    }

    private void TickHiddenHuntUi()
    {
        if (nativeHud == null || (DateTime.UtcNow - lastHiddenHuntBuildUtc).TotalMilliseconds < 250) return;
        lastHiddenHuntBuildUtc = DateTime.UtcNow;
        nativeHud?.TickHiddenHunts(hiddenHuntsOpen, hiddenHuntsOpen ? BuildHiddenHuntSnapshot() : new HiddenHuntSnapshot());
    }
}
