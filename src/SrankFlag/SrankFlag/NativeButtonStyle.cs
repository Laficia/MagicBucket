using System;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace SrankFlag;

// Layout only: textures, label nodes and hover/press timelines belong to the native component.
internal static class NativeButtonStyle
{
    internal static void Prepare(TextButtonNode button)
    {
        button.LabelNode.FontSize=13;
        button.LabelNode.TextColorID=50;
        button.LabelNode.RemoveTextFlags(TextFlags.MultiLine);
    }
    internal static void InitHover(TextButtonNode button) => button.ShowClickableCursor=true;
    internal static void SetLabel(TextButtonNode button,string? label){label??="";if(button.String.ToString()!=label)button.String=label;}
    internal static void SizePin(TextButtonNode button,float width,float height)
    {width=MathF.Ceiling(width);height=MathF.Ceiling(MathF.Max(26,height));if(button.Width!=width||button.Height!=height)button.Size=new(width,height);}
    internal static void SoftenTextInput(TextInputNode input) { } // Keep native input textures and focus border.
    internal static void Paint(TextButtonNode button,Vector4 idle,Vector4 hover) => button.LabelNode.TextColorID=50;
    internal static void HideBackground(TextButtonNode button) {button.BackgroundNode.IsVisible=false;}
    internal static float Measure(string? label)
    {
        float width=0,line=0;
        foreach(var rune in (label??"").EnumerateRunes())
        {
            if(rune.Value=='\n'){width=MathF.Max(width,line);line=0;}
            else if(rune.Value!='\r')line+=rune.Value<128?7.5f:14f;
        }
        return MathF.Max(width,line);
    }
    internal static float FitText(ThemedTextNode node,string? text,float maxW,float minW=40)
    {node.String=text??"";node.Width=Math.Clamp(Measure(text)+8,minW,maxW);return node.Width;}
    internal static float Apply(TextButtonNode button,string? label,float maxW,float baseH,bool selected)
    {
        label??="";maxW=MathF.Max(48,maxW);
        float width=Math.Clamp(Measure(label)+36,48,maxW);
        Fixed(button,label,width,baseH,Vector4.One,Vector4.One);
        return button.Width;
    }
    internal static void Fixed(TextButtonNode button,string? label,float width,float baseH,Vector4 bg,Vector4 fg)
    {
        label??="";width=MathF.Max(48,MathF.Max(width,button.LabelNode.GetTextDrawSize(label,false).X+36));
        SizePin(button,width,baseH);
        SetLabel(button,label);
        button.BackgroundNode.IsVisible=true;
        button.LabelNode.RemoveTextFlags(TextFlags.MultiLine);
    }
    internal static void ClearBadge(TextButtonNode b,float h) => Apply(b,"×",40,h,false);
    internal static void MiniAction(TextButtonNode b,string? label,float h,bool on) => Apply(b,label,MathF.Max(48,Measure(label)+32),h,on);
    internal static void GhostButton(TextButtonNode b,string? label,float h,bool hover) => Apply(b,label,MathF.Max(48,Measure(label)+32),h,false);
    internal static void HintTag(TextButtonNode b,string? label,float maxW,float h) => Apply(b,label,maxW,h,false);
    internal static void SortHeader(TextButtonNode b,string? label,float h,bool on) => Apply(b,label,MathF.Max(64,Measure(label)+32),h,on);
    internal static float Radio(TextButtonNode b,string? label,float maxW,float h,bool on) => Apply(b,label,maxW,h,on);
    internal static float PlainRadio(TextButtonNode b,string? label,float maxW,float h,bool on,bool dimOff=false) => Apply(b,label,maxW,h,on);
    internal static void CompactRadio(TextButtonNode b,string? label,float h,bool on) => Apply(b,label,MathF.Max(48,Measure(label)+32),h,on);
    internal static void RunButton(TextButtonNode b,string? label,float width,float h,bool enabled) => Fixed(b,label,width,h,Vector4.One,Vector4.One);
}
