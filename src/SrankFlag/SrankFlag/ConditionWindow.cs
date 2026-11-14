using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using TextNode=SrankFlag.ThemedTextNode;

namespace SrankFlag;

internal sealed class ConditionWindow(ConditionActions actions):ResponsiveAddon
{
    private sealed class Row
    {
        internal string Key="";
        internal NativeCheckOption Subscription=null!;
        internal TextNode Name=null!,Timer=null!,Empty=null!;
        internal readonly List<TextNode> Worlds=[];
    }
    private ScrollingNode<ResNode>? scroll;
    private ResNode? content;
    private TextInputNode? search;
    private TextButtonNode? all,none,view;
    private TextNode? summary;
    private readonly List<Row> rows=[];
    internal bool SuppressClosed;
    protected override Vector2 MinimumSize=>new(340,220);
    protected unsafe override void OnSetup(AtkUnitBase* addon,Span<AtkValue> values)
    {
        scroll=new ScrollingNode<ResNode>{Position=ContentStartPosition,Size=new(BodyWidth,BodyHeight),AutoHideScrollBar=true,ScrollSpeed=32};
        scroll.AttachNode(this);content=scroll.ContentNode;
        search=new TextInputNode{Size=new(180,26),PlaceholderString="搜索 S 怪",MaxCharacters=80,OnInputReceived=text=>actions.Search(text.ToString())};search.AttachNode(content);
        all=Button("全选",()=>actions.SelectAll(true));none=Button("全不选",()=>actions.SelectAll(false));view=Button("订阅",actions.ToggleView);
        summary=Text();base.OnSetup(addon,values);
    }
    private TextNode Text(){var node=new TextNode{FontSize=13,TextColor=NativeTheme.Text,Size=new(100,24)};node.AttachNode(content);return node;}
    private TextButtonNode Button(string label,Action click){var node=new TextButtonNode{Size=new(80,26),String=label,OnClick=click};NativeButtonStyle.Prepare(node);NativeButtonStyle.InitHover(node);node.AttachNode(content);return node;}
    private Row CreateRow()
    {
        var row=new Row();row.Subscription=new NativeCheckOption(()=>actions.Toggle(row.Key));row.Subscription.AttachNode(content);
        row.Name=Text();row.Timer=Text();row.Empty=Text();return row;
    }
    internal void Apply(ConditionSnapshot snapshot)
    {
        if(scroll==null||content==null||search==null||summary==null)return;
        bool managing=!snapshot.OnlySubscribed;
        float width=BodyWidth-12,y=0;
        search.IsVisible=all.IsVisible=none.IsVisible=managing;
        NativeButtonStyle.Apply(view,managing?"返回提醒":"订阅",120,26,false);
        summary.String=snapshot.Dc+" · "+snapshot.Subscribed+" 项订阅"+(managing?" / 全部 "+snapshot.Total:"");
        summary.Fit(MathF.Max(100,width-view.Width-8),13);
        y=Flow(width,y,summary,view);
        if(managing)
        {
            NativeButtonStyle.Apply(all,"全选",90,26,false);NativeButtonStyle.Apply(none,"全不选",100,26,false);
            search.Width=MathF.Max(120,width-all.Width-none.Width-12);y=Flow(width,y,search,all,none);
        }
        while(rows.Count<snapshot.Rows.Count)rows.Add(CreateRow());
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i];bool visible=i<snapshot.Rows.Count;
            row.Name.IsVisible=visible&&!managing;row.Subscription.IsVisible=visible&&managing;row.Timer.IsVisible=visible;row.Empty.IsVisible=false;
            foreach(var world in row.Worlds)world.IsVisible=false;
            if(!visible)continue;
            var data=snapshot.Rows[i];row.Key=data.En;
            string detail=data.Method+"\n"+data.Rule+"\n"+string.Join("\n",data.Worlds.Select(world=>world.Name+" · "+world.Detail));
            float nameWidth=width;
            row.Name.String=data.Name;row.Name.Fit(nameWidth,13);row.Name.TextTooltip=detail;
            row.Subscription.Apply(data.Name,data.Subscribed,nameWidth,nameWidth);row.Subscription.TextTooltip=detail;
            row.Timer.String=data.Timer;row.Timer.TextColor=NativeTheme.ConditionColor(data.Phase);row.Timer.Fit(width,12);row.Timer.TextTooltip=detail;
            y=Flow(width,y,row.Name,row.Subscription);
            y=Flow(width,y,row.Timer);
            if(managing)continue;
            var ready=data.Worlds.Where(world=>world.Ready).ToList();
            if(ready.Count==0)
            {
                row.Empty.IsVisible=true;row.Empty.String="未进闸";row.Empty.TextColor=new(.55f,.58f,.62f,1);row.Empty.Fit(width,11);row.Empty.TextTooltip=detail;
                y=Flow(width,y,row.Empty);
            }
            else
            {
                while(row.Worlds.Count<ready.Count)row.Worlds.Add(Text());
                for(int j=0;j<ready.Count;j++)
                {
                    var label=row.Worlds[j];label.IsVisible=true;label.String=ready[j].Name;label.TextColor=ServerColors.ForWorld(ready[j].Name);
                    label.Fit(MathF.Min(width,NativeButtonStyle.Measure(ready[j].Name)+8),12);label.TextTooltip=ready[j].Name+" · "+ready[j].Detail;
                }
                y=Flow(width,y,row.Worlds.ToArray());
            }
            y+=4;
        }
        if(snapshot.Rows.Count==0){summary.String="暂无提醒 · 点“订阅”选择 S 怪";summary.Fit(MathF.Max(100,width-view.Width-8),12);}
        scroll.Position=ContentStartPosition;scroll.Size=new(BodyWidth,BodyHeight);content.Height=MathF.Max(y+8,BodyHeight);scroll.RecalculateSizes();scroll.ScrollBarNode.UpdateScrollParams();
    }
    protected unsafe override void OnHide(AtkUnitBase* addon){base.OnHide(addon);if(!SuppressClosed)actions.Closed();}
    protected unsafe override void OnFinalize(AtkUnitBase* addon){base.OnFinalize(addon);rows.Clear();scroll=null;content=null;search=null;summary=null;}
}
