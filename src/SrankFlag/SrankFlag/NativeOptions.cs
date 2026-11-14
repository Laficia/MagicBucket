using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace SrankFlag;

internal static class NativeOptionLayout
{
    internal static void Label(TextNode label,string text,float width)
    {
        label.String=text;
        label.FontSize=13;
        label.Width=MathF.Max(8,width-24);
        label.Height=24;
        label.RemoveTextFlags(TextFlags.AutoAdjustNodeSize);
        label.RemoveTextFlags(TextFlags.MultiLine);
        label.AddTextFlags(TextFlags.Ellipsis);
        while(label.FontSize>10&&label.GetTextDrawSize(false).X>label.Width)label.FontSize--;
    }
}

internal sealed class NativeCheckOption : CheckboxNode
{
    private bool synchronizing;
    private bool? appliedSelection;
    private Vector4? color;
    internal NativeCheckOption(Action changed)
    {
        DisableAutoResize=true;
        IsAutoResizeWidthEnabled=false;
        OnClick=_=>
        {
            if(synchronizing)return;
            changed();
        };
    }
    internal void Apply(string text,bool selected,float maxWidth,float? fixedWidth=null,Vector4? textColor=null)
    {
        float width=fixedWidth??MathF.Min(maxWidth,NativeButtonStyle.Measure(text)+42);
        width=MathF.Ceiling(MathF.Max(48,width));
        bool changed=String.ToString()!=text||MathF.Abs(Width-width)>.1f;
        if(String.ToString()!=text)String=text;
        if(MathF.Abs(Width-width)>.1f||Height!=24)Size=new(width,24);
        if(changed)NativeOptionLayout.Label(Label,text,Width);
        color=textColor;
        Repaint();
        // The native component changes its state during the pointer gesture. Reapplying
        // the previous snapshot between mouse-down and mouse-up cancels that gesture.
        if(appliedSelection==selected)return;
        appliedSelection=selected;
        synchronizing=true;
        try{IsChecked=selected;}finally{synchronizing=false;}
    }
    private void Repaint(){if(Label!=null)Label.TextColor=NativePalette.Text(color??NativeTheme.Text,NativeTheme.Text);}
    protected override void OnColorThemeChanged(){base.OnColorThemeChanged();Repaint();}
}

internal sealed class NativeRadioOption : RadioButtonNode
{
    private bool synchronizing;
    private bool? appliedSelection;
    internal NativeRadioOption(Action changed)=>Callback=()=>
    {
        if(synchronizing)return;
        changed();
    };
    internal void Apply(string text,bool selected,float maxWidth)
    {
        float width=MathF.Ceiling(MathF.Max(48,MathF.Min(maxWidth,NativeButtonStyle.Measure(text)+42)));
        bool changed=String.ToString()!=text||MathF.Abs(Width-width)>.1f;
        if(String.ToString()!=text)String=text;
        if(MathF.Abs(Width-width)>.1f||Height!=24)Size=new(width,24);
        if(changed)NativeOptionLayout.Label(LabelNode,text,Width);
        LabelNode.Position=new(24,0);
        LabelNode.TextColor=NativePalette.Text(NativeTheme.Text,NativeTheme.Text);
        if(appliedSelection!=selected)
        {
            appliedSelection=selected;
            synchronizing=true;
            // IsChecked also updates the native IsSelected bit. Always run the toolkit's
            // IsSelected setter here so its separate selected-image node is updated too.
            try{IsChecked=selected;IsSelected=selected;}
            finally{synchronizing=false;}
        }
    }
    protected override void OnColorThemeChanged()
    {base.OnColorThemeChanged();if(LabelNode!=null)LabelNode.TextColor=NativePalette.Text(NativeTheme.Text,NativeTheme.Text);}
}
