using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using TextNode = SrankFlag.ThemedTextNode;

namespace SrankFlag;

internal sealed class ConditionWindow(ConditionActions actions) : ResponsiveAddon
{
    private sealed class ServerBadge : ResNode
    {
        private readonly ColorImageNode background;
        private readonly TextNode label;
        internal ServerBadge()
        {
            background=new ColorImageNode();background.AttachNode(this);
            label=new TextNode {FontSize=11,Position=new Vector2(5,1)};label.AttachNode(this);
        }
        internal void Apply(ConditionWorld world)
        {
            Vector4 color=ServerColors.ForWorld(world.Name);
            float width=NativeButtonStyle.Measure(world.Name)+12;
            Size=new Vector2(width,20);background.Size=Size;background.Color=color;
            label.NativeTextOverride=NativePalette.Luminance(color)>.18?new Vector4(.02f,.02f,.02f,1):Vector4.One;
            label.TextColor=NativePalette.Luminance(color)>.18?new Vector4(.02f,.02f,.02f,1):Vector4.One;
            label.String=world.Name;label.Fit(width-10,11);
            TextTooltip=world.Name+" · "+world.Detail;
        }
    }
    private sealed class Row
    {
        internal string Key="";
        internal TextButtonNode Name=null!;
        internal TextNode Timer=null!,Empty=null!;
        internal readonly List<ServerBadge> Worlds=[];
    }
    private ScrollingNode<ResNode>? scroll;
    private ResNode? content;
    private TextInputNode? search;
    private TextButtonNode? all,none,view;
    private TextNode? summary;
    private readonly List<Row> rows=[];
    private bool managing;
    internal bool SuppressClosed;
    protected override Vector2 MinimumSize=>new(340,220);

    protected unsafe override void OnSetup(AtkUnitBase* addon,Span<AtkValue> values)
    {
        scroll=new ScrollingNode<ResNode>{Position=ContentStartPosition,Size=new Vector2(BodyWidth,BodyHeight),AutoHideScrollBar=true,ScrollSpeed=30};
        scroll.AttachNode(this);content=scroll.ContentNode;
        search=new TextInputNode{Size=new Vector2(180,24),PlaceholderString="搜索 S 怪",MaxCharacters=80,OnInputReceived=text=>actions.Search(text.ToString())};search.AttachNode(content);
        all=Button("全选",()=>actions.SelectAll(true));none=Button("全不选",()=>actions.SelectAll(false));view=Button("订阅",actions.ToggleView);
        summary=Text();base.OnSetup(addon,values);
    }
    private TextNode Text(){var n=new TextNode{FontSize=11,Size=new Vector2(100,20)};n.AttachNode(content);return n;}
    private TextButtonNode Button(string label,Action click){var n=new TextButtonNode{Size=new Vector2(70,24),String=label,OnClick=click};NativeButtonStyle.Prepare(n);NativeButtonStyle.InitHover(n);n.AttachNode(content);return n;}
    private Row CreateRow(){var row=new Row();row.Name=Button("",()=>{if(managing)actions.Toggle(row.Key);});row.Timer=Text();row.Empty=Text();return row;}
    internal void Apply(ConditionSnapshot snapshot)
    {
        if(scroll==null||content==null||search==null||summary==null)return;
        managing=!snapshot.OnlySubscribed;
        float w=BodyWidth-12,y=0;
        search.IsVisible=all.IsVisible=none.IsVisible=managing;
        NativeButtonStyle.GhostButton(view,managing?"返回提醒":"订阅 ▾",22,false);
        view.Position=new Vector2(w-view.Width,0);
        summary.String=snapshot.Dc+" · "+snapshot.Subscribed+" 项订阅"+(managing?" / 全部 "+snapshot.Total:"");summary.Fit(w-view.Width-8,11);summary.Position=Vector2.Zero;
        y=28;
        if(managing)
        {
            NativeButtonStyle.Apply(all,"全选",65,24,false);NativeButtonStyle.Apply(none,"全不选",75,24,false);
            search.Width=MathF.Max(120,w-all.Width-none.Width-16);y=Flow(w,y,search,all,none);
        }
        while(rows.Count<snapshot.Rows.Count)rows.Add(CreateRow());
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i];bool visible=i<snapshot.Rows.Count;row.Name.IsVisible=row.Timer.IsVisible=visible;row.Empty.IsVisible=false;foreach(var badge in row.Worlds)badge.IsVisible=false;if(!visible)continue;
            var data=snapshot.Rows[i];row.Key=data.En;
            string name=(managing?(data.Subscribed?"✓ ":"□ "):"")+data.Name;
            NativeButtonStyle.GhostButton(row.Name,name,24,false);NativeButtonStyle.Paint(row.Name,NativeTheme.Text,NativeTheme.Text);row.Name.ShowClickableCursor=managing;
            string detail=data.Method+"\n"+data.Rule+"\n"+string.Join("\n",data.Worlds.Select(x=>x.Name+" · "+x.Detail));row.Name.TextTooltip=detail;
            row.Timer.String=data.Timer;row.Timer.TextColor=NativeTheme.ConditionColor(data.Phase);row.Timer.Fit(MathF.Max(100,w-row.Name.Width-8),12);row.Timer.TextTooltip=detail;
            y=Flow(w,y,row.Name,row.Timer);
            if(managing)continue;
            var worlds=data.Worlds.Where(x=>x.Ready).ToList();
            if(worlds.Count==0)
            {
                row.Empty.IsVisible=true;row.Empty.String="未进闸";row.Empty.TextColor=new Vector4(.55f,.58f,.62f,1);row.Empty.Fit(w,10);row.Empty.TextTooltip=detail;row.Empty.Position=new Vector2(4,y);y+=20;
            }
            else
            {
                while(row.Worlds.Count<worlds.Count){var badge=new ServerBadge();badge.AttachNode(content);row.Worlds.Add(badge);}
                float x=4;
                for(int j=0;j<worlds.Count;j++){var badge=row.Worlds[j];badge.IsVisible=true;badge.Apply(worlds[j]);if(x+badge.Width>w&&x>4){x=4;y+=23;}badge.Position=new Vector2(x,y);x+=badge.Width+4;}
                y+=23;
            }
            y+=5;
        }
        if(snapshot.Rows.Count==0){summary.String="暂无提醒 · 点“订阅”选择 S 怪";summary.Fit(w-view.Width-8,11);}
        scroll.Position=ContentStartPosition;scroll.Size=new Vector2(BodyWidth,BodyHeight);content.Height=MathF.Max(y+8,BodyHeight);scroll.RecalculateSizes();scroll.ScrollBarNode.UpdateScrollParams();
    }
    protected unsafe override void OnHide(AtkUnitBase* addon){base.OnHide(addon);if(!SuppressClosed)actions.Closed();}
    protected unsafe override void OnFinalize(AtkUnitBase* addon){base.OnFinalize(addon);rows.Clear();scroll=null;content=null;}
}
