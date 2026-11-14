using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using TextNode = SrankFlag.ThemedTextNode;

namespace SrankFlag;

internal sealed class HudWindow : ResponsiveAddon
{
	private readonly HudActions actions;

	private bool setupDone;
	private ScrollingNode<ResNode>? scroll;
	private ResNode? content;

	internal static Action<string>? Logger;

	private TextNode? badge;

	private TextNode? progress;

	private TextNode? job;

	private TextNode? map;

	private TextNode? next;

	private TextNode? status;

	private TextNode? hint;

	private TextButtonNode? primary;

	private TextButtonNode? skip;

	private TextButtonNode? stop;

	private TextButtonNode? restart;

	internal bool SuppressClosed;

	internal HudWindow(HudActions actions)
	{
		this.actions = actions;
	}

	protected unsafe override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
	{
		setupDone = false;
		try
		{
			SetWindowSize(new Vector2(360f, 300f));
			Vector2 contentStartPosition = ContentStartPosition;
			scroll=new ScrollingNode<ResNode>{Position=contentStartPosition,Size=new Vector2(BodyWidth,BodyHeight),AutoHideScrollBar=true};
			scroll.AttachNode(this);content=scroll.ContentNode;
			badge = Text(contentStartPosition + new Vector2(0f, 0f), 340f, 20f, 14u, "● 空闲");
			progress = Text(contentStartPosition + new Vector2(150f, 0f), 190f, 20f, 12u, "0/0");
			job = Text(contentStartPosition + new Vector2(0f, 26f), 340f, 26f, 18u, "还没开始触发");
			map = Text(contentStartPosition + new Vector2(0f, 56f), 340f, 20f, 12u, "");
			next = Text(contentStartPosition + new Vector2(0f, 78f), 340f, 20f, 12u, "");
			status = Text(contentStartPosition + new Vector2(0f, 100f), 340f, 52f, 12u, "");
			hint = Text(contentStartPosition + new Vector2(0f, 158f), 340f, 18f, 11u, "关掉后：主窗「触发悬浮窗」按钮 或 /水桶 hud");
			primary = Button(contentStartPosition + new Vector2(0f, 182f), 166f, 28f, "继续队列", () =>
			{
				actions.Primary();
			});
			skip = Button(contentStartPosition + new Vector2(174f, 182f), 166f, 28f, "停止这只，去下一个", () =>
			{
				actions.Skip();
			});
			stop = Button(contentStartPosition + new Vector2(0f, 214f), 166f, 28f, "停止整队", () =>
			{
				actions.Stop();
			});
			restart = Button(contentStartPosition + new Vector2(174f, 214f), 166f, 28f, "重新开始", () =>
			{
				actions.Restart();
			});
			setupDone = true;
			base.OnSetup(addon, atkValueSpan);
		}
		catch (Exception ex)
		{
			setupDone = false;
			try
			{
				Logger?.Invoke("[原生UI] 触发悬浮窗节点初始化失败，本窗暂不刷新：" + ex.Message);
			}
			catch
			{
			}
		}
	}

	private TextNode Text(Vector2 pos, float w, float h, uint fontSize, string value)
	{
		TextNode textNode = new TextNode();
		textNode.Position = pos;
		textNode.Size = new Vector2(w, h);
		textNode.FontSize = fontSize;
		textNode.String = value;
		textNode.AttachNode(content);
		return textNode;
	}

	private TextButtonNode Button(Vector2 pos, float w, float h, string label, Action onClick)
	{
		TextButtonNode textButtonNode = new TextButtonNode
		{
			Position = pos,
			Size = new Vector2(w, h),
			String = label,
			OnClick = onClick
		};
		NativeButtonStyle.Prepare(textButtonNode);
		NativeButtonStyle.InitHover(textButtonNode);
		textButtonNode.AttachNode(content);
		return textButtonNode;
	}

	internal void Apply(HudSnapshot s)
    {
        if(!setupDone||scroll==null||content==null)return;
        badge.String=s.Badge;progress.String=s.Progress;job.String=s.Job;map.String=s.Map;next.String=s.Next;status.String=s.Status;
        float w=BodyWidth-12,y=0;badge.Fit(w*.60f,14);progress.Fit(w*.30f,12);y=Flow(w,y,badge,progress);
        foreach(var text in new[]{job,map,next,status,hint}){text.Fit(w,text==job?18u:12u,true);y=Flow(w,y,text);}
        NativeButtonStyle.Fixed(primary,s.Primary,w,28,Vector4.One,NativeTheme.ButtonText);
        NativeButtonStyle.Fixed(skip,"停止这只，去下一个",w,28,Vector4.One,NativeTheme.ButtonText);
        NativeButtonStyle.Fixed(stop,"停止整队",w,28,Vector4.One,NativeTheme.ButtonText);
        NativeButtonStyle.Fixed(restart,"重新开始",w,28,Vector4.One,NativeTheme.ButtonText);
        foreach(var button in new[]{primary,skip,stop,restart})y=Flow(w,y,button);
        scroll.Position=ContentStartPosition;scroll.Size=new Vector2(BodyWidth,BodyHeight);content.Height=MathF.Max(y+8,BodyHeight);scroll.RecalculateSizes();scroll.ScrollBarNode.UpdateScrollParams();
    }

	protected unsafe override void OnHide(AtkUnitBase* addon)
	{
		base.OnHide(addon);
		if (!SuppressClosed)
		{
			actions.Closed();
		}
	}

	protected unsafe override void OnFinalize(AtkUnitBase* addon)
	{
		base.OnFinalize(addon);
		badge = (progress = (job = (map = (next = (status = (hint = null))))));
		primary = (skip = (stop = (restart = null)));
		setupDone = false;
	}
}
