using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using TextNode = SrankFlag.ThemedTextNode;

namespace SrankFlag;

internal sealed class ConditionWindow(ConditionActions actions, bool floating = false) : ResponsiveAddon
{
    private sealed class Row
    {
        internal string Key = "";
        internal ResNode Root = null!;
        internal NativeCheckOption Subscription = null!;
        internal TextNode Name = null!, Timer = null!, Empty = null!;
        internal readonly List<TextNode> Worlds = [];
    }
    private ScrollingNode<ResNode>? scroll;
    private ResNode? header;
    private TextInputNode? search;
    private TextButtonNode? all, none, view, floatingButton, lockButton;
    private TextNode? summary;
    private readonly List<Row> rows = [];
    private string layoutKey = "";
    private bool settingSearch;
    private Vector2? lockedPosition;
    internal bool SuppressClosed;
    protected override Vector2 MinimumSize => floating ? new(300, 170) : new(340, 220);

    protected unsafe override void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        header = new ResNode();
        header.AttachNode(this);
        scroll = new ScrollingNode<ResNode> { AutoHideScrollBar = true, ScrollSpeed = 32 };
        scroll.AttachNode(this);
        search = new TextInputNode { Size = new(180, 26), PlaceholderString = "搜索 S 怪", MaxCharacters = 80,
            OnInputReceived = text => { if (!settingSearch) actions.Search(text.ToString()); } };
        search.AttachNode(header);
        all = Button("全选", () => actions.SelectAll(true));
        none = Button("全不选", () => actions.SelectAll(false));
        view = Button("订阅", actions.ToggleView);
        if (floating) lockButton = Button("锁定位置", TogglePositionLock);
        else floatingButton = Button("悬浮窗", actions.ToggleFloating);
        summary = Text(header);
        base.OnSetup(addon, values);
    }
    private static TextNode Text(ResNode parent)
    {
        var node = new TextNode { FontSize = 13, TextColor = NativeTheme.Text, Size = new(100, 24) };
        node.AttachNode(parent);
        return node;
    }
    private TextButtonNode Button(string label, Action click)
    {
        var node = new TextButtonNode { Size = new(80, 26), String = label, OnClick = click };
        NativeButtonStyle.Prepare(node);
        NativeButtonStyle.InitHover(node);
        node.AttachNode(header);
        return node;
    }
    private unsafe void TogglePositionLock()
    {
        if (InternalAddon == null) return;
        lockedPosition = lockedPosition.HasValue ? null : new Vector2(InternalAddon->X, InternalAddon->Y);
    }
    protected unsafe override void OnUpdate(AtkUnitBase* addon)
    {
        base.OnUpdate(addon);
        if (lockedPosition is { } position && (addon->X != position.X || addon->Y != position.Y))
            SetWindowPosition(position);
    }
    private Row CreateRow()
    {
        var row = new Row { Root = new ResNode() };
        row.Root.AttachNode(scroll!.ContentNode);
        row.Subscription = new NativeCheckOption(() => actions.Toggle(row.Key));
        row.Subscription.AttachNode(row.Root);
        row.Name = Text(row.Root);
        row.Timer = Text(row.Root);
        row.Empty = Text(row.Root);
        return row;
    }
    internal void Apply(ConditionSnapshot snapshot)
    {
        if (scroll == null || header == null || search == null || summary == null || view == null || all == null || none == null) return;
        bool managing = !floating && !snapshot.OnlySubscribed;
        float width = BodyWidth - 12;
        header.Position = ContentStartPosition;
        search.IsVisible = all.IsVisible = none.IsVisible = managing;
        NativeButtonStyle.Apply(view, floating ? "订阅管理" : managing ? "返回提醒" : "订阅", 120, 26, false);
        if (floatingButton != null)
        {
            floatingButton.IsVisible = !managing;
            NativeButtonStyle.Apply(floatingButton, "悬浮窗", 100, 26, false);
        }
        summary.String = snapshot.Dc + " · " + snapshot.Subscribed + " 项订阅" + (managing ? " / 全部 " + snapshot.Total : "");
        summary.IsVisible = !floating || snapshot.Rows.Count == 0;
        if (floating) summary.String = "暂无订阅";
        float floatingWidth = floatingButton is { IsVisible: true } ? floatingButton.Width + 6 : 0;
        summary.Fit(MathF.Max(80, width - view.Width - floatingWidth - 8), 13);
        if (lockButton != null)
            NativeButtonStyle.Apply(lockButton, lockedPosition.HasValue ? "解锁位置" : "锁定位置", 120, 26, false);
        float headerHeight = Flow(width, 0, summary, view, floatingButton, lockButton);
        if (managing)
        {
            if (search.String.ToString() != snapshot.Search)
            {
                settingSearch = true;
                try { search.String = snapshot.Search; } finally { settingSearch = false; }
            }
            NativeButtonStyle.Apply(all, "全选", 90, 26, false);
            NativeButtonStyle.Apply(none, "全不选", 100, 26, false);
            float searchWidth = MathF.Max(100, width - all.Width - none.Width - 12);
            if (search.Width != searchWidth) search.Width = searchWidth;
            headerHeight = Flow(width, headerHeight, search, all, none);
        }
        header.Size = new(BodyWidth, headerHeight);
        string key = (managing ? "manage:" : "remind:") + string.Join("|", snapshot.Rows.Select(row => row.Key));
        bool resetScroll = key != layoutKey;
        layoutKey = key;
        while (rows.Count < snapshot.Rows.Count) rows.Add(CreateRow());
        float y = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool visible = i < snapshot.Rows.Count;
            row.Root.IsVisible = visible;
            if (!visible) continue;
            var data = snapshot.Rows[i];
            row.Key = data.Key;
            string detail = data.Method + "\n" + data.Rule + "\n" + string.Join("\n", data.Worlds.Select(world => world.Name + " · " + world.Detail));
            row.Root.Position = new(0, y);
            row.Name.IsVisible = row.Timer.IsVisible = !managing;
            row.Empty.IsVisible = false;
            foreach (var world in row.Worlds) world.IsVisible = false;
            float height;
            if (managing)
            {
                // Apply the native state before visibility: its timeline must not
                // resurrect controls belonging to the other presentation mode.
                string label = data.PinnedWorld.Length == 0 ? data.Name : data.PinnedWorld + " · " + data.Name;
                row.Subscription.Apply(label, data.Subscribed, width, width);
                row.Subscription.Position = Vector2.Zero;
                row.Subscription.TextTooltip = detail;
                height = 30;
            }
            else
            {
                const float timerWidth = 142;
                row.Name.String = data.Name;
                row.Name.Fit(MathF.Max(60, width - timerWidth - 8), 13);
                row.Name.Position = Vector2.Zero;
                row.Name.TextTooltip = detail;
                row.Timer.String = floating && data.Phase is ConditionPhase.Active or ConditionPhase.Waiting or ConditionPhase.Soon
                    ? HuntWindowDisplay.Countdown(data.Seconds) : data.Timer;
                row.Timer.TextColor = NativeTheme.ConditionColor(data.Phase);
                row.Timer.Fit(timerWidth, 12);
                row.Timer.Position = new(width - timerWidth, 0);
                row.Timer.TextTooltip = detail;
                float nextLine = MathF.Max(row.Name.Height, row.Timer.Height) + 5;
                var ready = data.Worlds.Where(world => world.Ready || world.Name == data.PinnedWorld).ToList();
                if (ready.Count == 0)
                {
                    row.Empty.IsVisible = true;
                    row.Empty.String = "未进闸";
                    row.Empty.TextColor = new(.55f, .58f, .62f, 1);
                    row.Empty.Fit(width, 11);
                    row.Empty.Position = new(0, nextLine);
                    row.Empty.TextTooltip = detail;
                    height = nextLine + row.Empty.Height + 8;
                }
                else
                {
                    while (row.Worlds.Count < ready.Count) row.Worlds.Add(Text(row.Root));
                    float x = 0, lineHeight = 0;
                    for (int j = 0; j < ready.Count; j++)
                    {
                        var label = row.Worlds[j];
                        label.String = ready[j].Name;
                        label.TextColor = ServerColors.ForWorld(ready[j].Name);
                        label.Fit(MathF.Min(width, NativeButtonStyle.Measure(ready[j].Name) + 8), 12);
                        label.TextTooltip = ready[j].Name + " · " + ready[j].Detail;
                        if (x > 0 && x + label.Width > width) { x = 0; nextLine += lineHeight + 4; lineHeight = 0; }
                        label.Position = new(x, nextLine);
                        label.IsVisible = true;
                        x += label.Width + 6;
                        lineHeight = MathF.Max(lineHeight, label.Height);
                    }
                    height = nextLine + lineHeight + 8;
                }
            }
            row.Subscription.IsVisible = managing;
            row.Root.Size = new(width, height);
            y += height;
        }
        if (snapshot.Rows.Count == 0)
        {
            summary.String = managing ? "没有匹配的 S 怪" : floating ? "暂无订阅" : "暂无提醒 · 点“订阅”选择 S 怪";
            summary.Fit(MathF.Max(80, width - view.Width - 8), 12);
        }
        Vector2 viewport = new(BodyWidth, MathF.Max(40, BodyHeight - headerHeight));
        float contentHeight = MathF.Max(y + 8, viewport.Y);
        bool resized = scroll.Size != viewport || MathF.Abs(scroll.ContentNode.Height - contentHeight) > .5f;
        scroll.Position = ContentStartPosition + new Vector2(0, headerHeight);
        if (scroll.Size != viewport) scroll.Size = viewport;
        if (MathF.Abs(scroll.ContentNode.Height - contentHeight) > .5f) scroll.ContentNode.Height = contentHeight;
        if (resized) scroll.RecalculateSizes();
        if (resetScroll) scroll.ScrollToTop();
    }
    protected unsafe override void OnHide(AtkUnitBase* addon) { base.OnHide(addon); if (!SuppressClosed) actions.Closed(); }
    protected unsafe override void OnFinalize(AtkUnitBase* addon)
    {
        base.OnFinalize(addon);
        rows.Clear(); scroll = null; header = null; search = null; summary = null; all = none = view = floatingButton = lockButton = null; layoutKey = "";
    }
}
