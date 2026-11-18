using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using TextNode = SrankFlag.ThemedTextNode;

namespace SrankFlag;

internal sealed class HiddenHuntActions
{
    internal Action<string> Restore = _ => { };
    internal Action RestoreAll = () => { };
    internal Action Closed = () => { };
}
internal sealed record HiddenHuntEntry(string En, string Name);
internal sealed class HiddenHuntSnapshot
{
    internal readonly List<HiddenHuntEntry> Rows = [];
}

internal sealed class HiddenHuntWindow(HiddenHuntActions actions) : ResponsiveAddon
{
    private sealed class Row
    {
        internal string Key = "";
        internal TextNode Name = null!;
        internal TextButtonNode Restore = null!;
    }
    private ScrollingNode<ResNode>? scroll;
    private TextNode? summary, empty;
    private TextButtonNode? restoreAll;
    private readonly List<Row> rows = [];
    internal bool SuppressClosed;
    protected override Vector2 MinimumSize => new(300, 200);

    protected unsafe override void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        scroll = new ScrollingNode<ResNode> { Size = new(BodyWidth, BodyHeight), AutoHideScrollBar = true, ScrollSpeed = 32 };
        scroll.AttachNode(this);
        summary = Text();
        empty = Text();
        empty.String = "暂无隐藏的 S 怪";
        restoreAll = Button("全部恢复", actions.RestoreAll);
        base.OnSetup(addon, values);
    }
    private TextNode Text()
    {
        var node = new TextNode { FontSize = 13, TextColor = NativeTheme.Text, Size = new(180, 24) };
        node.AttachNode(scroll!.ContentNode);
        return node;
    }
    private TextButtonNode Button(string label, Action click)
    {
        var node = new TextButtonNode { Size = new(90, 26), String = label, OnClick = click };
        NativeButtonStyle.Prepare(node);
        NativeButtonStyle.InitHover(node);
        node.AttachNode(scroll!.ContentNode);
        return node;
    }
    internal void Apply(HiddenHuntSnapshot snapshot)
    {
        if (scroll == null || summary == null || empty == null || restoreAll == null) return;
        float width = BodyWidth - 12;
        summary.String = $"已隐藏 {snapshot.Rows.Count} 种 S 怪";
        summary.Fit(MathF.Max(80, width - 108), 13);
        restoreAll.IsVisible = snapshot.Rows.Count > 0;
        float y = Flow(width, 0, summary, restoreAll);
        while (rows.Count < snapshot.Rows.Count)
        {
            var row = new Row { Name = Text() };
            row.Restore = Button("恢复", () => actions.Restore(row.Key));
            rows.Add(row);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool visible = i < snapshot.Rows.Count;
            row.Name.IsVisible = row.Restore.IsVisible = visible;
            if (!visible) continue;
            var data = snapshot.Rows[i];
            row.Key = data.En;
            row.Name.String = data.Name;
            row.Name.TextTooltip = data.Name + " · 恢复后按当前筛选显示";
            row.Name.Fit(MathF.Max(80, width - row.Restore.Width - 8), 13);
            y = Flow(width, y, row.Name, row.Restore);
        }
        empty.IsVisible = snapshot.Rows.Count == 0;
        if (empty.IsVisible) { empty.Fit(width, 13); y = Flow(width, y, empty); }
        scroll.Position = ContentStartPosition;
        scroll.Size = new(BodyWidth, BodyHeight);
        scroll.ContentNode.Height = MathF.Max(y + 8, BodyHeight);
        scroll.RecalculateSizes();
        scroll.ScrollBarNode.UpdateScrollParams();
    }
    protected unsafe override void OnHide(AtkUnitBase* addon)
    {
        base.OnHide(addon);
        if (!SuppressClosed) actions.Closed();
    }
    protected unsafe override void OnFinalize(AtkUnitBase* addon)
    {
        base.OnFinalize(addon);
        rows.Clear(); scroll = null; summary = empty = null; restoreAll = null;
    }
}
