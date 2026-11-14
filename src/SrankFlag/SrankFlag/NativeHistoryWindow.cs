using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using TextNode = SrankFlag.ThemedTextNode;
using ColorImageNode = SrankFlag.ThemedColorImageNode;

namespace SrankFlag;

internal sealed class NativeHistoryWindow : ResponsiveAddon
{
	private sealed class RowNodes
	{
		internal ColorImageNode? Bg;

		internal TextNode? Time;

		internal TextNode? Monster;

		internal TextNode? Server;

		internal TextNode? Event;

		internal TextNode? Source;
	}

	internal const float W = 700f;

	internal const float H = 520f;

	private const float CW = 676f;

	private const float TopInset = 8f;

	private const float BottomPad = 8f;

	private const float HeaderH = 22f;

	private const float RowH = 24f;

	private const float ScrollBarTrackW = 8f;

	private const int RowPool = 24;

	private const float TimeX = 8f;

	private const float TimeW = 112f;

	private const float NameX = 126f;

	private const float NameW = 172f;

	private const float ServerX = 304f;

	private const float ServerW = 104f;

	private const float EventX = 414f;

	private const float EventW = 62f;

	private const float SourceX = 482f;

	private const float SourceW = 92f;

	private static readonly Vector4 MutedText = new Vector4(0.52f, 0.56f, 0.62f, 1f);

	private static readonly Vector4 ListText = new Vector4(0.86f, 0.89f, 0.94f, 1f);

	private static readonly Vector4 KillRed = new Vector4(0.9f, 0.34f, 0.36f, 1f);

	private static readonly Vector4 SpawnGreen = new Vector4(0.44f, 0.88f, 0.5f, 1f);

	private static readonly Vector4 SonarGold = new Vector4(1f, 0.8f, 0.3f, 1f);

	private static readonly Vector4 SiteGray = new Vector4(0.56f, 0.6f, 0.66f, 1f);

	private static readonly Vector4 StripeA = new Vector4(0.12f, 0.15f, 0.21f, 0.85f);

	private static readonly Vector4 StripeB = new Vector4(0.18f, 0.21f, 0.28f, 0.85f);

	private static readonly (HistoryFilter Filter, string Label)[] Filters = new (HistoryFilter, string)[5]
	{
		(HistoryFilter.All, "全部"),
		(HistoryFilter.KillOnly, "只看击杀"),
		(HistoryFilter.SpawnOnly, "只看出生"),
		(HistoryFilter.SonarOnly, "只看 Sonar"),
		(HistoryFilter.MainSiteOnly, "只看主站")
	};

	internal static Action<string>? Logger;

	private readonly HistoryActions actions;

	private readonly RowNodes?[] rows = new RowNodes[24];

	private readonly NativeRadioOption?[] filterButtons = new NativeRadioOption[5];

	private readonly TextNode?[] headers = new TextNode[5];

	private bool setupDone;

	private ResNode? fixedRoot;

	private ScrollingNode<ResNode>? scroll;

	private ResNode? content;

	private NodeBase? attachParent;

	private TextNode? title;

	private TextNode? status;

	private TextNode? mapHint;

	private TextButtonNode? bPrev;

	private TextButtonNode? bNext;

	private TextButtonNode? bClear;

	private TextButtonNode? bClose;

	private float lastViewH = -1f;

	private float lastScrollW = -1f;

	private bool clearArmed;

	private DateTime clearArmedAtUtc;

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

	internal NativeHistoryWindow(HistoryActions actions)
	{
		this.actions = actions;
	}

	protected unsafe override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
	{
		setupDone = false;
		try
		{
			SetWindowSize(new Vector2(700f, 520f));
			Vector2 contentStartPosition = ContentStartPosition;
			for (int i = 0; i < rows.Length; i++)
			{
				rows[i] = null;
			}
			for (int j = 0; j < filterButtons.Length; j++)
			{
				filterButtons[j] = null;
			}
			for (int k = 0; k < headers.Length; k++)
			{
				headers[k] = null;
			}
			fixedRoot = null;
			scroll = null;
			content = null;
			attachParent = null;
			title = (status = (mapHint = null));
			bPrev = (bNext = (bClear = (bClose = null)));
			lastViewH = (lastScrollW = -1f);
			fixedRoot = new ResNode
			{
				Position = contentStartPosition,
				Size = new Vector2(676f, 520f)
			};

			scroll = new ScrollingNode<ResNode>
			{
				Position = contentStartPosition,
				Size = new Vector2(676f, 520f - contentStartPosition.Y - 8f),
				AutoHideScrollBar = true,
				ScrollSpeed = 32
			};
			scroll.AttachNode(this);
			scroll.ScrollBarNode.IsAcceptingMouseWheelEvents = true;
			content = scroll.ContentNode;
			fixedRoot.AttachNode(content);
			attachParent = fixedRoot;
			title = T(contentStartPosition, 320f, 22f, 14u, "S 怪历史记录");
			for (int l = 0; l < Filters.Length; l++)
			{
				HistoryFilter f = Filters[l].Filter;
				filterButtons[l] = new NativeRadioOption(() =>
				{
					actions.SetFilter(f);
				});
				filterButtons[l].AttachNode(fixedRoot);
			}
			bPrev = B(contentStartPosition, 96f, 24f, "◀ 上一页", () =>
			{
				actions.PageBy(-1);
			});
			bNext = B(contentStartPosition, 96f, 24f, "下一页 ▶", () =>
			{
				actions.PageBy(1);
			});
			bClear = B(contentStartPosition, 130f, 26f, "清空历史", () =>
			{
				TryClear();
			});
			bClose = B(contentStartPosition, 80f, 26f, "关闭", () =>
			{
				actions.Closed();
			});
			string[] array = new string[5] { "时间", "怪名", "服务器", "事件", "来源" };
			_ = new float[5] { 8f, 126f, 304f, 414f, 482f };
			float[] array2 = new float[5] { 112f, 172f, 104f, 62f, 92f };
			for (int num = 0; num < array.Length; num++)
			{
				headers[num] = T(contentStartPosition, array2[num], 22f, 12u, array[num]);
				headers[num].TextColor = MutedText;
			}
			status = T(contentStartPosition, 652f, 18f, 11u, "");
			status.TextColor = MutedText;
			mapHint = T(contentStartPosition, 652f, 18f, 11u, "");
			mapHint.TextColor = MutedText;
			attachParent = null;
			for (int num2 = 0; num2 < 24; num2++)
			{
				rows[num2] = MakeRow(num2);
			}
			setupDone = true;
			base.OnSetup(addon, atkValueSpan);
		}
		catch (Exception ex)
		{
			setupDone = false;
			Log("[原生UI] 历史窗口节点初始化失败，本窗暂不刷新：" + ex.Message);
		}
	}

	private RowNodes MakeRow(int idx)
	{
		Vector2 contentStartPosition = ContentStartPosition;
		RowNodes rowNodes = new RowNodes();
		rowNodes.Bg = new ColorImageNode
		{
			IsRowSurface = true,
			Position = contentStartPosition,
			Size = new Vector2(664f, 22f),
			Color = ((idx % 2 == 0) ? StripeA : StripeB)
		};
		rowNodes.Time = new TextNode
		{
			Position = contentStartPosition,
			Size = new Vector2(112f, 20f),
			FontSize = 12u,
			FontType = FontType.Axis,
			TextColor = ListText
		};
		rowNodes.Monster = new TextNode
		{
			Position = contentStartPosition,
			Size = new Vector2(172f, 20f),
			FontSize = 12u,
			FontType = FontType.Axis,
			TextColor = ListText
		};
		rowNodes.Server = new TextNode
		{
			Position = contentStartPosition,
			Size = new Vector2(104f, 20f),
			FontSize = 12u,
			FontType = FontType.Axis,
			TextColor = ListText
		};
		rowNodes.Event = new TextNode
		{
			Position = contentStartPosition,
			Size = new Vector2(62f, 20f),
			FontSize = 12u,
			FontType = FontType.Axis,
			TextColor = ListText
		};
		rowNodes.Source = new TextNode
		{
			Position = contentStartPosition,
			Size = new Vector2(92f, 20f),
			FontSize = 12u,
			FontType = FontType.Axis,
			TextColor = ListText
		};
		rowNodes.Bg.AttachNode(content);
		rowNodes.Time.AttachNode(content);
		rowNodes.Monster.AttachNode(content);
		rowNodes.Server.AttachNode(content);
		rowNodes.Event.AttachNode(content);
		rowNodes.Source.AttachNode(content);
		Show(rowNodes, v: false);
		return rowNodes;
	}

	private TextNode T(Vector2 pos, float w, float h, uint fontSize, string value)
	{
		TextNode textNode = new TextNode();
		textNode.Position = pos;
		textNode.Size = new Vector2(w, h);
		textNode.FontSize = fontSize;
		textNode.String = value;
		textNode.AttachNode(attachParent ?? content);
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
		textButtonNode.AttachNode(attachParent ?? content);
		return textButtonNode;
	}

	private static void Show(RowNodes r, bool v)
	{
		if (r.Bg != null)
		{
			r.Bg.IsVisible = v;
		}
		if (r.Time != null)
		{
			r.Time.IsVisible = v;
		}
		if (r.Monster != null)
		{
			r.Monster.IsVisible = v;
		}
		if (r.Server != null)
		{
			r.Server.IsVisible = v;
		}
		if (r.Event != null)
		{
			r.Event.IsVisible = v;
		}
		if (r.Source != null)
		{
			r.Source.IsVisible = v;
		}
	}

	private void TryClear()
	{
		if (!clearArmed || (DateTime.UtcNow - clearArmedAtUtc).TotalSeconds > 5.0)
		{
			clearArmed = true;
			clearArmedAtUtc = DateTime.UtcNow;
		}
		else
		{
			clearArmed = false;
			actions.Clear();
		}
	}

	internal void Apply(HistorySnapshot s)
    {
        if(!setupDone||fixedRoot==null||content==null||scroll==null)return;
        float w=BodyWidth-12,y=0;fixedRoot.Position=Vector2.Zero;title.Fit(MathF.Max(100,w-250),14);
        if(clearArmed&&(DateTime.UtcNow-clearArmedAtUtc).TotalSeconds>5)clearArmed=false;
        NativeButtonStyle.Apply(bClear,clearArmed?"再点一次确认":"清空历史",150,26,clearArmed);NativeButtonStyle.Apply(bClose,"关闭",80,26,false);y=Flow(w,y,title,bClear,bClose);
        for(int i=0;i<filterButtons.Length;i++)filterButtons[i].Apply(Filters[i].Label,s.Filter==Filters[i].Filter,w);
        y=Flow(w,y,filterButtons);
        bPrev.IsVisible=s.Page>0;bNext.IsVisible=s.Page+1<s.PageCount;status.String=s.Status;status.Fit(w,11);y=Flow(w,y,bPrev,bNext,status);
        mapHint.String=s.Rows.Count>0?"最近："+s.Rows[0].MonsterName+" · "+s.Rows[0].MapName:"暂无历史记录";mapHint.Fit(w,11);y=Flow(w,y,mapHint);
        float[] widths=NativeWindowLayout.HistoryColumns(w);float x=0;
        for(int i=0;i<headers.Length;i++){headers[i].Position=new Vector2(x,y);headers[i].Fit(widths[i]-6,12);x+=widths[i];}y+=26;fixedRoot.Size=new Vector2(w,y);
        for(int i=0;i<rows.Length;i++)
        {
            var row=rows[i];if(row==null)continue;Show(row,i<s.Rows.Count);if(i>=s.Rows.Count)continue;
            var record=s.Rows[i];row.Bg.Position=new Vector2(0,y);row.Bg.Size=new Vector2(w,24);
            row.Time.String=FormatTime(record.EventTime);row.Monster.String=record.MonsterName;row.Server.String=record.ServerName;row.Server.TextColor=ServerColors.ForWorld(record.ServerName);
            row.Event.String=record.EventType==HistoryEventType.Kill?"击杀":"出生";row.Event.TextColor=record.EventType==HistoryEventType.Kill?KillRed:SpawnGreen;
            row.Source.String=record.Source switch {HistorySource.Sonar=>"Sonar",HistorySource.Bear=>"熊站",_=>"主站"};row.Source.TextColor=record.Source==HistorySource.Sonar?SonarGold:SiteGray;
            x=0;var texts=new[]{row.Time,row.Monster,row.Server,row.Event,row.Source};
            for(int j=0;j<texts.Length;j++){texts[j].Position=new Vector2(x+3,y+2);texts[j].Fit(widths[j]-6,12);x+=widths[j];}y+=26;
        }
        scroll.Position=ContentStartPosition;scroll.Size=new Vector2(BodyWidth,BodyHeight);content.Height=MathF.Max(y+8,BodyHeight);scroll.RecalculateSizes();scroll.ScrollBarNode.UpdateScrollParams();
    }

	private static string FormatTime(double unixSeconds)
	{
		try
		{
			if (unixSeconds <= 0.0)
			{
				return "--";
			}
			return DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds).ToLocalTime().ToString("MM-dd HH:mm:ss");
		}
		catch
		{
			return "--";
		}
	}

	private void Reflow(Vector2 p, float scrollW, float fixedH, float listContentH)
	{
		float num = 520f - p.Y - fixedH - 8f - 22f;
		if (num < 120f)
		{
			num = 120f;
		}
		fixedRoot.Size = new Vector2(676f, fixedH);
		scroll.Position = new Vector2(p.X, p.Y + fixedH);
		if (MathF.Abs(num - lastViewH) >= 0.5f || MathF.Abs(scrollW - lastScrollW) >= 0.5f)
		{
			lastViewH = num;
			lastScrollW = scrollW;
			scroll.Size = new Vector2(scrollW, num);
		}
		content.Height = MathF.Max(listContentH, num);
		Vector2 size = new Vector2(scrollW, num);
		scroll.ClippingContentNode.Size = size;
		scroll.ScrollingCollisionNode.Size = size;
		ScrollBarNode scrollBarNode = scroll.ScrollBarNode;
		scrollBarNode.Position = new Vector2(scrollW - 8f, 0f);
		scrollBarNode.Size = new Vector2(8f, num);
		scrollBarNode.IsAcceptingMouseWheelEvents = true;
		scrollBarNode.UpdateScrollParams();
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
		for (int j = 0; j < filterButtons.Length; j++)
		{
			filterButtons[j] = null;
		}
		for (int k = 0; k < headers.Length; k++)
		{
			headers[k] = null;
		}
		fixedRoot = null;
		scroll = null;
		content = null;
		attachParent = null;
		title = (status = (mapHint = null));
		bPrev = (bNext = (bClear = (bClose = null)));
		setupDone = false;
	}
}
