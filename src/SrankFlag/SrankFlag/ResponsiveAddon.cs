using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using KamiToolKit;

namespace SrankFlag;

internal abstract class ResponsiveAddon : NativeAddon
{
    private TextureButtonNode? resizeGrip;
    private ViewportEventListener? dragListener;
    private Vector2 dragStart, startSize;
    private bool resizeEnded;
    protected virtual Vector2 MinimumSize => new(300, 220);
    protected float BodyWidth => MathF.Max(100, Size.X - 24);
    protected float BodyHeight => MathF.Max(40, Size.Y - ContentStartPosition.Y - 28);
    private static string SizeFile => Path.Combine(KamiToolKitLibrary.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "window_sizes.json");
    private static Dictionary<string, float[]>? savedSizes;

    internal void ResizeTo(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height)) return;
        Size = Vector2.Clamp(new Vector2(width, height), MinimumSize, new Vector2(2000, 1600));
        PositionGrip();
    }

    internal void RememberSize()
    {
        try
        {
            savedSizes ??= new();
            savedSizes[InternalName] = [Size.X, Size.Y];
            File.WriteAllText(SizeFile, JsonSerializer.Serialize(savedSizes));
        }
        catch { /* A read-only configuration directory must not break the window. */ }
    }

    protected unsafe override void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        base.OnSetup(addon, values);
        try
        {
            savedSizes ??= File.Exists(SizeFile) ? JsonSerializer.Deserialize<Dictionary<string, float[]>>(File.ReadAllText(SizeFile)) ?? new() : new();
            if (savedSizes.TryGetValue(InternalName, out var size) && size.Length == 2) ResizeTo(size[0], size[1]);
        }
        catch { savedSizes ??= new(); }
        resizeGrip = new TextureButtonNode
        {
            Size = new Vector2(18, 18),
            TexturePath = "ui/uld/ChatLog.tex",
            TextureCoordinates = new Vector2(32, 34),
            TextureSize = new Vector2(18, 18),
            TextTooltip = "拖动调整窗口宽高"
        };
        resizeGrip.ImageNode.ImageNodeFlags = ImageNodeFlags.FlipV;
        resizeGrip.AddEvent(AtkEventType.MouseOver, () => { if (resizeGrip != null) resizeGrip.TextureCoordinates = new Vector2(4, 34); });
        resizeGrip.AddEvent(AtkEventType.MouseOut, () => { if (resizeGrip != null) resizeGrip.TextureCoordinates = new Vector2(32, 34); });
        resizeGrip.AddEvent(AtkEventType.MouseDown, BeginResize);
        resizeGrip.AttachNode(this);
        PositionGrip();
    }

    private void PositionGrip()
    {
        if (resizeGrip != null) resizeGrip.Position = Size - new Vector2(23, 22);
    }

    private unsafe void BeginResize(AtkEventListener* listener, AtkEventType type, int param, AtkEvent* evt, AtkEventData* data)
    {
        if (data == null || data->MouseData.ButtonId != 0 || resizeGrip == null) return;
        EndResize();
        dragStart = new Vector2(data->MouseData.PosX, data->MouseData.PosY);
        startSize = Size;
        dragListener = new ViewportEventListener(DragResize);
        dragListener.AddEvent(AtkEventType.MouseMove, resizeGrip);
        dragListener.AddEvent(AtkEventType.MouseUp, resizeGrip);
        if (evt != null) evt->SetEventIsHandled();
    }

    private unsafe void DragResize(AtkEventListener* listener, AtkEventType type, int param, AtkEvent* evt, AtkEventData* data)
    {
        if (data == null || InternalAddon == null) { EndResize(); return; }
        if (resizeEnded) return;
        if (type == AtkEventType.MouseUp) { resizeEnded = true; RememberSize(); return; }
        if (type != AtkEventType.MouseMove) return;
        float scale = MathF.Max(.1f, InternalAddon->Scale);
        Vector2 delta = (new Vector2(data->MouseData.PosX, data->MouseData.PosY) - dragStart) / scale;
        ResizeTo(startSize.X + delta.X, startSize.Y + delta.Y);
        if (evt != null) evt->SetEventIsHandled();
    }

    private void EndResize()
    {
        dragListener?.Dispose();
        dragListener = null;
        resizeEnded = false;
    }

    protected unsafe override void OnUpdate(AtkUnitBase* addon) { base.OnUpdate(addon); if (resizeEnded) EndResize(); PositionGrip(); }
    protected unsafe override void OnHide(AtkUnitBase* addon) { EndResize(); RememberSize(); base.OnHide(addon); }
    protected unsafe override void OnFinalize(AtkUnitBase* addon) { EndResize(); resizeGrip = null; base.OnFinalize(addon); }

    protected static float Flow(float width, float y, params NodeBase?[] nodes)
    {
        float x = 0, rowHeight = 0;
        foreach (var node in nodes)
        {
            if (node == null || !node.IsVisible) continue;
            if (x > 0 && x + node.Width > width) { x = 0; y += rowHeight + 6; rowHeight = 0; }
            node.Position = new Vector2(x, y);
            x += node.Width + 6;
            rowHeight = MathF.Max(rowHeight, node.Height);
        }
        return y + rowHeight + 6;
    }
}
