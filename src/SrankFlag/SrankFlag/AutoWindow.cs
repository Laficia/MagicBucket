using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using TextNode = SrankFlag.ThemedTextNode;
using ColorImageNode = SrankFlag.ThemedColorImageNode;

namespace SrankFlag;

internal sealed class AutoWindow : ResponsiveAddon
{
	internal const float W = 660f;

	internal const float DefaultH = 430f;

	private const float CW = 636f;

	private const float MinH = 300f;

	private const float MaxH = 820f;

	private const float BottomPad = 8f;

	private const float ViewportMaxH = 560f;

	private const float ScrollBarW = 12f;

	private const float MinWindowW = 380f;

	private static readonly Vector4 MutedText = new Vector4(0.52f, 0.56f, 0.62f, 1f);

	private static readonly Vector4 DividerColor = new Vector4(0.24f, 0.26f, 0.3f, 1f);

	private const int ChipCount = 16;

	private const int ChipPerRow = 8;

	private const float ChipW = 76f;

	private const float ChipPitch = 80f;

	private const float ChipRowH = 26f;

	private const int QueueLines = 6;

	private const int RowCount = 16;

	internal const int PageSize = 16;

	private const float HuntColPitch = 320f;

	private const float HuntBtnW = 212f;

	private const float HuntStateX = 216f;

	private const float HuntRowH = 27f;

	private static readonly float[] ToolW = new float[7] { 62f, 62f, 92f, 106f, 104f, 68f, 68f };

	private const float ToolGap = 8f;

	private const int AlwaysTools = 5;

	private readonly AutoWindowActions actions;

	private readonly string[] worldNames = new string[16];

	private readonly string[] rowKeys = new string[16];

	private readonly TextButtonNode?[] tools = new TextButtonNode[7];

	private bool setupDone;

	private float lastHeight = 430f;

	private float lastViewH;

	private readonly List<NodeBase> allNodes = new List<NodeBase>();

	private float lastW = 660f;

	private float lastScrollW;

	private ScrollingNode<ResNode>? scroll;

	private ResNode? content;

	private TextNode? hit1;

	private TextNode? hit2;

	private TextNode? worldCaption;

	private TextNode? queueTitle;

	private TextNode? pageLabel;

	private TextNode? hints;

	private TextNode? counts;

	private TextNode? status;

	private TextNode? skipped;

	private TextButtonNode? clearHit;

	private TextButtonNode? bPrev;

	private TextButtonNode? bNext;

	private TextButtonNode? bRun;

	private ColorImageNode? sepLine;

	private readonly TextButtonNode?[] chips = new TextButtonNode[16];

	private readonly TextNode?[] queueLines = new TextNode[6];

	private readonly TextButtonNode?[] huntButtons = new TextButtonNode[16];

	private readonly TextNode?[] huntStates = new TextNode[16];

	internal bool SuppressClosed;

	internal AutoWindow(AutoWindowActions actions)
	{
		this.actions = actions;
	}

	protected unsafe override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
	{
		SetWindowSize(new Vector2(660f, 430f));
		Vector2 contentStartPosition = ContentStartPosition;
		scroll = new ScrollingNode<ResNode>
		{
			Position = contentStartPosition,
			Size = new Vector2(636f, 430f - contentStartPosition.Y - 8f),
			AutoHideScrollBar = true,
			ScrollSpeed = 32
		};
		scroll.AttachNode(this);
		content = scroll.ContentNode;
		hit1 = T(contentStartPosition, 480f, 20f, 14u, "");
		hit2 = T(contentStartPosition, 480f, 16f, 11u, "");
		clearHit = B(contentStartPosition, 150f, 26f, "知道了，清掉提示", () =>
		{
			actions.ClearHit();
		});
		tools[0] = B(contentStartPosition, ToolW[0], 24f, "分组", () =>
		{
			actions.GroupStyle();
		});
		tools[1] = B(contentStartPosition, ToolW[1], 24f, "卡片", () =>
		{
			actions.CardStyle();
		});
		tools[2] = B(contentStartPosition, ToolW[2], 24f, "只看开闸", () =>
		{
			actions.ToggleOnlyOpen();
		});
		tools[3] = B(contentStartPosition, ToolW[3], 24f, "触发悬浮窗", () =>
		{
			actions.ToggleHud();
		});
		tools[4] = B(contentStartPosition, ToolW[4], 24f, "全部服务器", () =>
		{
			actions.ToggleAllWorlds();
		});
		tools[5] = B(contentStartPosition, ToolW[5], 24f, "全选服", () =>
		{
			actions.SelectAllWorlds();
		});
		tools[6] = B(contentStartPosition, ToolW[6], 24f, "清空服", () =>
		{
			actions.ClearWorlds();
		});
		sepLine = new ColorImageNode
		{
			Position = contentStartPosition,
			Size = new Vector2(636f, 1f),
			Color = DividerColor
		};
		allNodes.Add(sepLine);
		sepLine.AttachNode(content);
		worldCaption = T(contentStartPosition, 636f, 16f, 11u, "");
		worldCaption.TextColor = MutedText;
		for (int num = 0; num < 16; num++)
		{
			int idx = num;
			chips[num] = B(contentStartPosition, 76f, 24f, "", () =>
			{
				ClickWorld(idx);
			});
		}
		queueTitle = T(contentStartPosition, 636f, 18f, 12u, "");
		queueTitle.TextColor = MutedText;
		for (int num2 = 0; num2 < 6; num2++)
		{
			queueLines[num2] = T(contentStartPosition, 630f, 16f, 11u, "");
		}
		pageLabel = T(contentStartPosition, 446f, 20f, 12u, "");
		bPrev = B(contentStartPosition, 90f, 24f, "← 上一页", () =>
		{
			actions.PrevPage();
		});
		bNext = B(contentStartPosition, 90f, 24f, "下一页 →", () =>
		{
			actions.NextPage();
		});
		for (int num3 = 0; num3 < 16; num3++)
		{
			int idx2 = num3;
			huntButtons[num3] = B(contentStartPosition, 212f, 24f, "", () =>
			{
				ClickHunt(idx2);
			});
			huntStates[num3] = T(contentStartPosition, 100f, 18f, 10u, "");
			huntStates[num3].TextColor = MutedText;
		}
		hints = T(contentStartPosition, 636f, 16f, 10u, "");
		hints.TextColor = MutedText;
		bRun = B(contentStartPosition, 160f, 28f, "开始自动前往", () =>
		{
			actions.StartStop();
		});
		counts = T(contentStartPosition, 466f, 20f, 11u, "");
		status = T(contentStartPosition, 636f, 18f, 11u, "");
		skipped = T(contentStartPosition, 636f, 16f, 10u, "");
		setupDone = true;
			base.OnSetup(addon, atkValueSpan);
	}

	private TextNode T(Vector2 pos, float w, float h, uint fontSize, string value)
	{
		TextNode textNode = new TextNode
		{
			Position = pos,
			Size = new Vector2(w, h),
			FontSize = fontSize,
			String = value
		};
		allNodes.Add(textNode);
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
		allNodes.Add(textButtonNode);
		textButtonNode.AttachNode(content);
		return textButtonNode;
	}

	private void ClickWorld(int i)
	{
		string text = worldNames[i];
		if (text.Length > 0)
		{
			actions.ToggleWorld(text);
		}
	}

	private void ClickHunt(int i)
	{
		string text = rowKeys[i];
		if (text.Length > 0)
		{
			actions.ToggleHunt(text);
		}
	}

	internal void Apply(AutoWindowSnapshot s)
	{
		if (!setupDone)
		{
			return;
		}
		NativeButtonStyle.FitText(hit1, s.ShowHitBar ? s.Hit1 : "", 480f);
		NativeButtonStyle.FitText(hit2, s.ShowHitBar ? s.Hit2 : "", 480f);
		NativeButtonStyle.Apply(clearHit, "知道了，清掉提示", 190f, 26f, selected: false);
		string[] array = new string[7] { "分组", "卡片", "只看开闸", "触发悬浮窗", "全部服务器", "全选服", "清空服" };
		bool[] array2 = new bool[7] { s.StyleGroup, s.StyleCard, s.OnlyOpen, s.HudOpen, s.AllWorlds, false, false };
		for (int i = 0; i < tools.Length; i++)
		{
			if (i < 5)
			{
				NativeButtonStyle.PlainRadio(tools[i], array[i], 150f, 24f, array2[i], dimOff: true);
			}
			else
			{
				NativeButtonStyle.Apply(tools[i], array[i], 150f, 24f, selected: false);
			}
		}
		NativeButtonStyle.FitText(worldCaption, s.WorldCaption, 636f);
		bool showChips = s.Ready && !s.AllWorlds;
		for (int j = 0; j < 16; j++)
		{
			string text = ((j < s.Worlds.Count) ? s.Worlds[j] : "");
			worldNames[j] = text;
			bool flag = j < s.WorldOn.Count && s.WorldOn[j];
			NativeButtonStyle.Radio(chips[j], (flag ? "● " : "○ ") + text, 118f, 24f, flag);
			NativeButtonStyle.Paint(chips[j], ServerColors.ForWorld(text), ServerColors.ForWorld(text));
		}
		NativeButtonStyle.FitText(queueTitle, s.QueueTitle, 300f);
		for (int k = 0; k < 6; k++)
		{
			NativeButtonStyle.FitText(queueLines[k], (k < s.QueueLines.Count) ? s.QueueLines[k] : "", 636f);
		}
		NativeButtonStyle.FitText(pageLabel, $"选择要触发的 S · 共 {s.Hunts.Count} 只 · 第 {s.Page + 1}/{Math.Max(1, s.PageCount)} 页", 360f);
		int num = s.Page * 16;
		for (int l = 0; l < 16; l++)
		{
			AutoHuntRow autoHuntRow = ((num + l < s.Hunts.Count) ? s.Hunts[num + l] : null);
			rowKeys[l] = autoHuntRow?.Key ?? "";
			NativeButtonStyle.Apply(huntButtons[l], (autoHuntRow == null) ? "" : ((autoHuntRow.Checked ? "✓ " : "□ ") + autoHuntRow.Label), 200f, 24f, autoHuntRow?.Checked ?? false);
			NativeButtonStyle.FitText(huntStates[l], autoHuntRow?.Right ?? "", 140f);
		}
		NativeButtonStyle.Apply(bPrev, "← 上一页", 120f, 24f, selected: false);
		NativeButtonStyle.Apply(bNext, "下一页 →", 120f, 24f, selected: false);
		NativeButtonStyle.FitText(hints, s.Hints, 636f);
		bool flag2 = s.RunLabel.Length > 0 && s.RunLabel != "开始自动前往";
		if (!flag2)
		{
			foreach (AutoHuntRow hunt in s.Hunts)
			{
				if (hunt.Checked)
				{
					flag2 = true;
					break;
				}
			}
		}
		NativeButtonStyle.RunButton(bRun, s.RunLabel, 210f, 28f, flag2);
		NativeButtonStyle.FitText(counts, s.Counts, 320f);
		NativeButtonStyle.FitText(status, s.Status, 636f);
		NativeButtonStyle.FitText(skipped, s.Skipped, 636f);
		Reflow(s, showChips);
	}

	private void Reflow(AutoWindowSnapshot s, bool showChips)
    {
        if(content==null||scroll==null) return;
        float w=BodyWidth-12,y=0;
        hit1.IsVisible=hit2.IsVisible=clearHit.IsVisible=s.ShowHitBar;
        if(s.ShowHitBar) {hit1.Fit(w,14,true);hit2.Fit(w,11,true);y=Flow(w,y,hit1);y=Flow(w,y,hit2);y=Flow(w,y,clearHit);}
        for(int i=0;i<tools.Length;i++)tools[i].IsVisible=i<5||showChips;
        y=Flow(w,y,tools);
        sepLine.Position=new Vector2(0,y);sepLine.Width=w;y+=8;
        worldCaption.Fit(w,11,true);y=Flow(w,y,worldCaption);
        for(int i=0;i<chips.Length;i++)chips[i].IsVisible=showChips&&i<s.Worlds.Count;
        if(showChips)y=Flow(w,y,chips);
        queueTitle.Fit(w,12);y=Flow(w,y,queueTitle);
        for(int i=0;i<queueLines.Length;i++){queueLines[i].IsVisible=i<s.QueueLines.Count;if(queueLines[i].IsVisible){queueLines[i].Fit(w,11,true);y=Flow(w,y,queueLines[i]);}}
        pageLabel.Fit(w,12);y=Flow(w,y,pageLabel);bPrev.IsVisible=s.Page>0;bNext.IsVisible=s.Page+1<s.PageCount;y=Flow(w,y,bPrev,bNext);
        int columns=Math.Max(1,(int)(w/310)),index=0;float colW=(w-8*(columns-1))/columns,rowH=0,startY=y;
        for(int i=0;i<huntButtons.Length;i++)
        {
            var hunt=s.Page*16+i<s.Hunts.Count?s.Hunts[s.Page*16+i]:null;huntButtons[i].IsVisible=huntStates[i].IsVisible=hunt!=null;if(hunt==null)continue;
            int col=index%columns;if(col==0&&index>0){startY+=rowH+6;rowH=0;}
            NativeButtonStyle.Fixed(huntButtons[i],(hunt.Checked?"✓ ":"□ ")+hunt.Label,MathF.Max(120,colW-90),24,Vector4.One,NativeTheme.ButtonText);
            huntButtons[i].Position=new Vector2(col*(colW+8),startY);
            huntStates[i].Fit(84,10,true);huntStates[i].Position=new Vector2(col*(colW+8)+huntButtons[i].Width+6,startY+2);
            rowH=MathF.Max(rowH,MathF.Max(huntButtons[i].Height,huntStates[i].Height));index++;
        }
        y=startY+rowH+8;counts.Fit(w,11,true);y=Flow(w,y,bRun,counts);
        foreach(var text in new[]{hints,status,skipped}){text.IsVisible=!text.String.IsEmpty;if(text.IsVisible){text.Fit(w,11,true);y=Flow(w,y,text);}}
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
		setupDone = false;
	}
}
