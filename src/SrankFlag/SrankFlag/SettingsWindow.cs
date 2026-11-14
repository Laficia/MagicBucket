using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using TextNode = SrankFlag.ThemedTextNode;

namespace SrankFlag;

internal sealed class SettingsWindow : ResponsiveAddon
{
	internal const float W = 420f;

	internal const float H = 430f;

	private const float CW = 396f;

	private static readonly Vector4 MutedText = new Vector4(0.52f, 0.56f, 0.62f, 1f);

	internal static readonly (string Key, string Label)[] Rows = new (string, string)[7]
	{
		("main", "显示主窗口"),
		("auto", "显示自动前往窗口"),
		("hud", "显示触发悬浮窗"),
		("conditions", "显示条件订阅悬浮窗"),
		("summon", "自动召唤随从"),
		("leave", "独身自动退本"),
		("return", "倒地自动确认返回")
	};

	internal static Action<string>? Logger;

	private readonly SettingsActions actions;

	private readonly TextButtonNode?[] rows = new TextButtonNode[Rows.Length];

	private TextNode? title;
	private ScrollingNode<ResNode>? scroll;
	private ResNode? content;

	private TextNode? hint;

	private TextNode? status;

	private TextButtonNode? bRefresh;

	private TextButtonNode? bClose;

	internal bool SuppressClosed;

	private static void Log(string m)
	{
		try
		{
			Logger?.Invoke("[SrankFlag.UI] " + m);
		}
		catch
		{
		}
	}

	internal SettingsWindow(SettingsActions actions)
	{
		this.actions = actions;
	}

	protected unsafe override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
	{
		SetWindowSize(new Vector2(420f, 430f));
		Vector2 contentStartPosition = ContentStartPosition;
		scroll = new ScrollingNode<ResNode> { Position=contentStartPosition, Size=new Vector2(BodyWidth,BodyHeight), AutoHideScrollBar=true };
		scroll.AttachNode(this); content=scroll.ContentNode;
		title = T(contentStartPosition, 396f, 22f, 14u, "魔法水桶 · 设置");
		title.TextColor = MutedText;
		for (int i = 0; i < Rows.Length; i++)
		{
			string key = Rows[i].Key;
			rows[i] = B(contentStartPosition, 396f, 28f, Rows[i].Label, () =>
			{
				actions.Toggle(key);
			});
		}
		status = T(contentStartPosition, 396f, 18f, 11u, "");
		status.TextColor = MutedText;
		bRefresh = B(contentStartPosition, 160f, 26f, "立即刷新数据", () =>
		{
			actions.Refresh();
		});
		bClose = B(contentStartPosition, 96f, 26f, "关闭", () =>
		{
			actions.Closed();
		});
		hint = T(contentStartPosition, 396f, 18f, 11u, "配色跟随游戏主题 · 点击行切换选项");
		hint.TextColor = MutedText;
		base.OnSetup(addon, atkValueSpan);
	}

	private TextNode T(Vector2 pos, float w, float h, uint fontSize, string value)
	{
		TextNode textNode = new TextNode();
		textNode.Position = pos;
		textNode.Size = new Vector2(w, h);
		textNode.FontSize = fontSize;
		textNode.String = value;
		textNode.AttachNode(content);
		return textNode;
	}

	private TextButtonNode B(Vector2 pos, float w, float h, string label, Action onClick)
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

	internal void Apply(SettingsSnapshot s)
    {
        if(content==null||scroll==null||title==null||status==null||hint==null||bRefresh==null||bClose==null)return;
        bool[] values=[s.ShowMain,s.ShowAuto,s.ShowHud,s.ShowConditions,s.AutoSummon,s.AutoLeave,s.ConfirmReturn];
        float w=BodyWidth-12,y=0;title.Fit(w,14);y=Flow(w,y,title);
        for(int i=0;i<rows.Length;i++){NativeButtonStyle.Fixed(rows[i],Rows[i].Label+(values[i]?" · 开":" · 关"),w,28,Vector4.One,NativeTheme.ButtonText);y=Flow(w,y,rows[i]);}
        status.String=s.Status;status.Fit(w,11,true);y=Flow(w,y,status);
        NativeButtonStyle.Apply(bRefresh,"立即刷新数据",170,26,false);NativeButtonStyle.Apply(bClose,"关闭",100,26,false);y=Flow(w,y,bRefresh,bClose);
        hint.Fit(w,11,true);y=Flow(w,y,hint);
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
		for (int i = 0; i < rows.Length; i++)
		{
			rows[i] = null;
		}
		title = (hint = (status = null));
		bRefresh = (bClose = null);
		scroll = null;
		content = null;
	}
}
