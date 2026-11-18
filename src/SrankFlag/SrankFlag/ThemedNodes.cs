using System.Numerics;
using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

namespace SrankFlag;

internal sealed class ThemedTextNode : TextNode
{
    private Vector4? source;
    private bool useButtonPalette;
    private string fullText = "", fittedText = "";
    private float fittedWidth = -1;
    private uint fittedFont;
    private bool fittedWrap;
    internal Vector4? NativeTextOverride { get; set; }

    public new ReadOnlySeString String
    {
        get => base.String;
        set
        {
            string next = value.ToString();
            if (fullText == next) return;
            fullText = next;
            base.String = value;
        }
    }

    internal void Fit(float width, uint maxFont = 13, bool wrap = false)
    {
        width = MathF.Max(8, MathF.Floor(width));
        // Other layout helpers may have changed Width since the previous fit.
        if (Width != width) Width = width;
        if (fittedText == fullText && MathF.Abs(fittedWidth - width) < .5f && fittedFont == maxFont && fittedWrap == wrap) return;
        fittedText = fullText; fittedWidth = width; fittedFont = maxFont; fittedWrap = wrap;
        base.String = fullText;
        FontSize = maxFont;
        while (!wrap && FontSize > 10 && GetTextDrawSize(false).X > width) FontSize--;
        if (wrap) AddTextFlags(TextFlags.MultiLine); else RemoveTextFlags(TextFlags.MultiLine);
        TextTooltip = fullText;
        if (GetTextDrawSize(false).X <= width) { Height = MathF.Max(FontSize+7, GetTextDrawSize(false).Y+4); return; }
        var result = new StringBuilder();
        string line = "";
        int lines = 1;
        foreach (var rune in fullText.EnumerateRunes())
        {
            if (rune.Value == '\n') { result.Append(line).Append('\n'); line = ""; lines++; continue; }
            string candidate = line + rune;
            if (GetTextDrawSize(candidate, false).X > width && line.Length > 0)
            {
                if (!wrap)
                {
                    while (line.Length > 0 && GetTextDrawSize(line + "…", false).X > width)
                        line = line[..(line.Length - (line.Length >= 2 && char.IsSurrogatePair(line, line.Length - 2) ? 2 : 1))];
                    base.String = line + "…"; TextTooltip = fullText; Height = FontSize + 7; return;
                }
                result.Append(line).Append('\n'); line = ""; lines++;
            }
            line += rune;
        }
        result.Append(line);
        base.String = result.ToString();
        Height = MathF.Max(lines * (FontSize + 5), GetTextDrawSize(false).Y+4);
    }

    internal bool UseButtonPalette
    {
        get => useButtonPalette;
        set { useButtonPalette = value; Repaint(); }
    }

    // Keep the original semantic color so switching themes never transforms an
    // already-transformed value, and switching back restores its original hue.
    public new Vector4 TextColor
    {
        get => base.TextColor;
        set { source = value; Repaint(); }
    }

    private void Repaint()
    {
        if (source is { } color)
            base.TextColor = NativePalette.Text(color, NativeTextOverride ?? (useButtonPalette ? NativeTheme.ButtonText : NativeTheme.Text));
    }

    protected override void OnColorThemeChanged()
    {
        base.OnColorThemeChanged();
        Repaint();
    }
}

internal sealed class ThemedColorImageNode : ColorImageNode
{
    private Vector4? source;
    internal bool IsRowSurface { get; init; }
    public new Vector4 Color
    {
        get => base.Color;
        set { source = value; base.Color = NativePalette.Surface(value, NativeTheme.Text, IsRowSurface); }
    }

    protected override void OnColorThemeChanged()
    {
        base.OnColorThemeChanged();
        if (source is { } color) base.Color = NativePalette.Surface(color, NativeTheme.Text, IsRowSurface);
    }
}
