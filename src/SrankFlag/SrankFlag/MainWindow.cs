using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;
using TextNode = SrankFlag.ThemedTextNode;
using ColorImageNode = SrankFlag.ThemedColorImageNode;

namespace SrankFlag;

internal sealed class MainWindow : ResponsiveAddon
{
	private sealed class RowNodes
	{
		internal TextButtonNode Hit;


		internal TextNode World;

		internal TextNode Name;

		internal ProgressBarNode Bar;

		internal TextNode Tail;

		internal TextNode Badge;

		internal int FillW;
	}

	internal const float W = 600f;

	internal const float DefaultH = 800f;

	internal const float FixedWindowH = 800f;

	private const float CW = 576f;

	private const float IconW = 20f;

	private const float WorldW = 100f;

	private const float NameW = 154f;

	private const float BarW = 232f;

	private const float TailW = 32f;

	private const float RowTotalW = 576f;

	private const float IconX = 10f;

	private const float WorldX = 40f;

	private const float NameX = 146f;

	private const float BarX = 306f;

	private const float TailX = 544f;

	private const float RowH = 28f;

	private const float BarH = 12f;

	private const float BottomPad = 8f;

	private const float TopInset = 16f;

	private const float SearchW = 240f;

	private const int WorldMenuMax = 16;

	private const int PatchCount = 7;

	private const int MaxRows = 320;

	private const int RowPoolAtSetup = 64;

	private const float RowGap = 6f;

	private const float MaxListViewH = 600f;

	private const float MinListViewH = 160f;

	private const float DetailBtnH = 22f;

	private const float DetailIndent = 20f;

	private const float DetailGap = 14f;

	private const float ScrollBarTrackW = 8f;

	private const float HeaderH = 22f;

	private static readonly string[] HeaderNames = new string[3] { "服务器", "怪名", "概率" };

	private static readonly Vector4 MutedText = new Vector4(0.52f, 0.56f, 0.62f, 1f);

	private static readonly Vector4 TrackColor = new Vector4(0.1f, 0.12f, 0.16f, 0.85f);

	private static readonly Vector4 SelBg = new Vector4(0.78f, 0.66f, 0.36f, 0.2f);

	private static readonly Vector4 Cyan = new Vector4(0.3f, 0.82f, 1f, 1f);

	private static readonly Vector4 ClockWhite = new Vector4(0.9f, 0.98f, 1f, 1f);

	private static readonly Vector4 BarDeep = new Vector4(0.04f, 0.07f, 0.1f, 1f);

	private static readonly Vector4 BarFill = new Vector4(0.95f, 0.78f, 0.28f, 1f);

	private static readonly bool CanaryBar = false;

	private const float CanaryOutX = 544f;

	private const float CanaryOutW = 16f;

	private static readonly Vector4 CanaryRed = new Vector4(1f, 0f, 0f, 1f);

	private static readonly Vector4 ListText = new Vector4(0.86f, 0.89f, 0.94f, 1f);

	private static readonly Vector4 Gold = new Vector4(1f, 0.82f, 0.3f, 1f);

	private static readonly Vector4 CoordIdle = new Vector4(0.66f, 0.7f, 0.76f, 1f);

	private static readonly Vector4 CoordHover = new Vector4(1f, 1f, 1f, 1f);

	private static readonly Vector4 AetIdle = new Vector4(0.8f, 0.64f, 0.26f, 1f);

	private static readonly Vector4 AetHover = new Vector4(1f, 0.86f, 0.5f, 1f);

	private static readonly Vector4 ActIdle = new Vector4(0.55f, 0.92f, 0.55f, 1f);

	private static readonly Vector4 ActHover = new Vector4(0.8f, 1f, 0.8f, 1f);

	private static readonly Vector4 CondOk = new Vector4(0.45f, 0.95f, 0.5f, 1f);

	private static readonly Vector4 CondBad = new Vector4(0.74f, 0.3f, 0.3f, 1f);

	private static readonly Vector4 CondNeutral = new Vector4(0.8f, 0.7f, 0.42f, 1f);

	internal const string TrigBadgeText = "已触发";

	private static readonly Vector4 TrigRed = new Vector4(0.95f, 0.33f, 0.3f, 1f);

	private static readonly Vector4 ActBlocked = new Vector4(0.46f, 0.48f, 0.53f, 1f);

	private const float BadgeW = 40f;

	private static readonly Vector4[] PatchTint = new Vector4[7]
	{
		new Vector4(0.46f, 0.48f, 0.54f, 1f),
		new Vector4(0.22f, 0.42f, 0.8f, 1f),
		new Vector4(0.24f, 0.66f, 0.9f, 1f),
		new Vector4(0.82f, 0.2f, 0.22f, 1f),
		new Vector4(0.5f, 0.26f, 0.78f, 1f),
		new Vector4(0.76f, 0.8f, 0.68f, 1f),
		new Vector4(0.86f, 0.68f, 0.22f, 1f)
	};

	internal static Action<string>? Logger;

	private readonly MainActions actions;

	private readonly List<RowNodes> rows = new List<RowNodes>();

	private readonly List<string> rowKeys = new List<string>();

	private TextNode? detailLabel;

	private TextNode? detailCond;
	private TextNode? detailTime, detailRange;

	private float detailCondW;

	private float detailCondRelX;

	private float detailCondRelY;

	private readonly List<TextButtonNode> detailBtns = new List<TextButtonNode>();

	private readonly List<Action?> detailActions = new List<Action>();

	private readonly List<(float rx, float ry, float w)> detailLayout = new List<(float, float, float)>();

	private int detailLineCount;

	private float detailLabelW;

	private float detailLabelRelX;

	private float detailLabelRelY;

	private const int MaxDcButtons = 8;

	private readonly NativeRadioOption?[] dcButtons = new NativeRadioOption[8];

	private readonly List<string> dcNames = new List<string>();

	private bool dcBuilt;

	private readonly List<string> patchKeys = new List<string>();

	private readonly List<string> patchLabels = new List<string>();

	private ColorImageNode? selRowBg;

	private ColorImageNode? selFrameLeft;

	private bool detailBuilt;

	private bool detailOk;

	private bool barLogged;

	private ColorImageNode? canaryOut;

	private bool canaryLogged;

	private ResNode? fixedRoot;

	private NodeBase? attachParent;

	private ColorImageNode? trackNode;

	private TextButtonNode? hintTag;

	private readonly TextButtonNode?[] sortHeaders = new TextButtonNode[3];

	private SortBy sortBy;

	private bool sortAsc = true;
	private bool showOpenRows = true;
	private TextButtonNode? bConditions;
	private TextButtonNode? bHidden, bHideHunt, bWatch, bWatchHunt;
	private bool revealDetails;
	protected override Vector2 MinimumSize => new(420, 300);

	private bool sortActive;

	private long patchSoloTick;

	private string patchSoloKey = "";

	private readonly string[] patchKeysArr = new string[7];

	private readonly string[] worldNames = new string[16];

	private bool setupDone;

	private bool onSetupOk = true;

	private bool applyLogged;

	private string rowFailSig = "";

	private bool menuOpen;

	private bool searchInitialized;

	private bool suppressSearch;

	private bool syncedExpand;

	private string selectedKey = "";

	private string expandedKey = "";

	private long lastRightTick;

	private string lastRightKey = "";

	private float lastViewH;

	private float lastScrollW;

	private bool settingsHover;

	private ScrollingNode<ResNode>? scroll;

	private ResNode? content;

	private TextInputNode? searchInput;

	private TextButtonNode? bDc;

	private TextButtonNode? bRefresh;

	private TextButtonNode? bAuto;

	private TextButtonNode? bLegacy;

	private TextButtonNode? bMini;

	private TextButtonNode? bStop;

	private TextButtonNode? bClearSearch;

	private TextButtonNode? bSettings;

	private TextButtonNode? bHistory;

	private TextButtonNode? bWorldMenu;

	private NativeCheckOption? bOnlyOpen;

	private TextButtonNode? bWorldAll;

	private TextButtonNode? bWorldClear;

	private TextButtonNode? bWorldCurrent;

	private readonly NativeCheckOption?[] worldMenuItems = new NativeCheckOption[16];

	private readonly NativeCheckOption?[] patchButtons = new NativeCheckOption[7];

	private TextNode? listTitle;

	private TextNode? counts;

	private TextNode? status;

	private TextNode? bManualHint;

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
		try
		{
			Console.WriteLine("[SrankFlag.UI] " + m);
		}
		catch
		{
		}
	}

	private static void SetT(TextNode? n, string? text)
	{
		if (n == null)
		{
			return;
		}
		try
		{
			n.String = text ?? "";
		}
		catch (Exception ex)
		{
			Log("[原生UI] 文本写入失败：" + ex.GetType().Name);
		}
	}

	private static void SetT(TextButtonNode? b, string? text)
	{
		if (b == null)
		{
			return;
		}
		try
		{
			b.String = text ?? "";
		}
		catch (Exception ex)
		{
			Log("[原生UI] 按钮文本写入失败：" + ex.GetType().Name);
		}
	}

	private static void ShowNode(NodeBase? n, bool v)
	{
		if (n == null)
		{
			return;
		}
		try
		{
			n.IsVisible = v;
		}
		catch
		{
		}
	}

	private static void KeepOpaque(NodeBase? n)
	{
		if (n == null)
		{
			return;
		}
		try
		{
			n.Alpha = 1f;
		}
		catch
		{
		}
	}

	private void RowStep(string stage, Action a, string who)
	{
		try
		{
			a();
		}
		catch (Exception ex)
		{
			string text = stage + "|" + ex.GetType().Name;
			if (!(text == rowFailSig))
			{
				rowFailSig = text;
				Log($"[原生UI] 列表行[{stage}]失败（{who}）：{ex}");
			}
		}
	}

	private int PaintBar(ColorImageNode bg, ColorImageNode fg, float trackW, float barH, float prog)
	{
		int num = (ushort)Math.Clamp(trackW * prog, 0f, trackW);
		int num2 = (ushort)(trackW - (float)num);
		fg.Size = new Vector2(num, barH);
		bg.Size = new Vector2(num2, barH);
		if (!barLogged)
		{
			barLogged = true;
			Log($"[原生UI] 血条诊断 prog={prog:F3} fgW={num} bgW={num2} fg实宽={fg.Width:F0} bg实宽={bg.Width:F0} fgVis={fg.IsVisible} bgVis={bg.IsVisible} fgColor={fg.Color}");
		}
		return num;
	}

	internal MainWindow(MainActions actions)
	{
		this.actions = actions;
	}

	protected unsafe override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
	{
		SetWindowSize(new Vector2(600f, 800f));
		Vector2 contentStartPosition = ContentStartPosition;
		rows.Clear();
		rowKeys.Clear();
		detailBtns.Clear();
		detailActions.Clear();
		detailLayout.Clear();
		detailLineCount = 0;
		detailLabel = null;
		detailCond = null;
		detailTime = detailRange = null;
		detailBuilt = false;
		dcBuilt = false;
		selRowBg = (selFrameLeft = null);
		searchInitialized = false;
		canaryOut = null;
		canaryLogged = false;
		fixedRoot = null;
		trackNode = null;
		hintTag = null;
		attachParent = null;
		for (int i = 0; i < sortHeaders.Length; i++)
		{
			sortHeaders[i] = null;
		}
		try
		{
			fixedRoot = new ResNode
			{
				Position = contentStartPosition,
				Size = new Vector2(576f, 800f)
			};

			scroll = new ScrollingNode<ResNode>
			{
				Position = contentStartPosition,
				Size = new Vector2(576f, 800f - contentStartPosition.Y - 8f),
				AutoHideScrollBar = true,
				ScrollSpeed = 32
			};
			scroll.AttachNode(this);
			scroll.ScrollBarNode.IsAcceptingMouseWheelEvents = true;
			trackNode = new ColorImageNode
			{
				Size = new Vector2(8f, 1f),
				Color = TrackColor
			};
			trackNode.AttachNode(scroll, NodePosition.AsFirstChild);
			trackNode.IsVisible = false;
			content = scroll.ContentNode;
			fixedRoot.AttachNode(content);
			attachParent = fixedRoot;
			bConditions = B(contentStartPosition, 100, 26, "条件订阅", () => actions.OpenConditions());
			bHidden = B(contentStartPosition, 100, 26, "已隐藏", () => actions.OpenHidden());
			bWatch = B(contentStartPosition, 84, 26, "悬浮窗", () => actions.OpenWatch());
			bHideHunt = B(contentStartPosition, 150, 26, "隐藏该类 S 怪", () =>
			{
				string en = expandedKey.Split('\u0001')[0];
				if (en.Length == 0) return;
				actions.HideHunt(en);
				CollapseDetails();
			});
			bHideHunt.IsVisible = false;
			bHideHunt.TextTooltip = "隐藏这类 S 怪的所有服务器条目；可在“已隐藏”里恢复";
			bWatchHunt = B(contentStartPosition, 150, 26, "加入悬浮窗", () =>
			{
				var pair = expandedKey.Split('\u0001', 2);
				if (pair.Length == 2) actions.ToggleWatch(pair[0], pair[1]);
			});
			bWatchHunt.IsVisible = false;
			searchInput = new TextInputNode
			{
				Position = contentStartPosition,
				Size = new Vector2(240f, 26f),
				PlaceholderString = "搜索",
				MaxCharacters = 64,
				OnInputReceived = (ReadOnlySeString s) =>
				{
					if (!suppressSearch)
					{
						actions.Search(s.ToString());
					}
				}
			};
			searchInput.AttachNode(fixedRoot);
			NativeButtonStyle.SoftenTextInput(searchInput);
			bClearSearch = B(contentStartPosition, 34f, 26f, "×", () =>
			{
				suppressSearch = true;
				searchInput.String = "";
				suppressSearch = false;
				actions.Search("");
			});
			bDc = B(contentStartPosition, 100f, 26f, "大区", () =>
			{
				actions.NextDc();
			});
			bRefresh = B(contentStartPosition, 84f, 26f, "刷新", () =>
			{
				actions.Refresh();
			});
			bSettings = B(contentStartPosition, 60f, 26f, "设置", () =>
			{
				actions.OpenSettings();
			});
			try
			{
				bSettings.AddEvent(AtkEventType.MouseOver, () =>
				{
					settingsHover = true;
					NativeButtonStyle.GhostButton(bSettings, "设置", 24f, hover: true);
				});
				bSettings.AddEvent(AtkEventType.MouseOut, () =>
				{
					settingsHover = false;
					NativeButtonStyle.GhostButton(bSettings, "设置", 24f, hover: false);
				});
			}
			catch
			{
			}
			bHistory = B(contentStartPosition, 60f, 26f, "历史", () =>
			{
				actions.OpenHistory();
			});
			bAuto = B(contentStartPosition, 96f, 26f, "前往", () =>
			{
				actions.OpenAuto();
			});
			bLegacy = B(contentStartPosition, 84f, 26f, "完整", () =>
			{
				actions.Legacy();
			});
			bMini = B(contentStartPosition, 64f, 26f, "收起", () =>
			{
				actions.Mini();
			});
			bLegacy.IsVisible = false;
			bMini.IsVisible = false;
			bStop = B(contentStartPosition, 64f, 26f, "停止", () =>
			{
				actions.StopAll();
			});
			bWorldMenu = B(contentStartPosition, 220f, 26f, "服务器", () =>
			{
				menuOpen = !menuOpen;
			});
			bOnlyOpen = new NativeCheckOption(() =>
			{
				actions.ToggleOnlyOpen();
			});
			bOnlyOpen.AttachNode(fixedRoot);
			counts = T(contentStartPosition, 400f, 20f, 11u, "");
			bManualHint = T(contentStartPosition, 160f, 20f, 11u, "");
			bManualHint.TextColor = MutedText;
			bManualHint.IsVisible = false;
			bWorldAll = B(contentStartPosition, 62f, 22f, "全服", () =>
			{
				actions.AllWorlds();
				menuOpen = false;
			});
			bWorldClear = B(contentStartPosition, 62f, 22f, "清空", () =>
			{
				actions.ClearWorlds();
				menuOpen = false;
			});
			bWorldCurrent = B(contentStartPosition, 62f, 22f, "本服", () =>
			{
				actions.CurrentWorldOnly();
				menuOpen = false;
			});
			for (int num = 0; num < 16; num++)
			{
				int idx = num;
				worldMenuItems[num] = new NativeCheckOption(() =>
				{
					string text = worldNames[idx];
					if (text.Length > 0)
					{
						actions.ToggleWorld(text);
					}
				});
				worldMenuItems[num].AttachNode(fixedRoot);
			}
			for (int num2 = 0; num2 < 7; num2++)
			{
				int idx2 = num2;
				patchButtons[num2] = new NativeCheckOption(() =>
				{
					if(patchKeysArr[idx2].Length>0)actions.TogglePatch(patchKeysArr[idx2]);
				});
				patchButtons[num2].AttachNode(fixedRoot);
				WirePatchClick(patchButtons[num2], idx2);
				patchButtons[num2].IsVisible = false;
			}
			for (int num3 = 0; num3 < 8; num3++)
			{
				int idx3 = num3;
				dcButtons[num3] = new NativeRadioOption(() =>
				{
					if (idx3 < dcNames.Count)
					{
						actions.SelectDc(dcNames[idx3]);
					}
				});
				dcButtons[num3].AttachNode(fixedRoot);
				dcButtons[num3].IsVisible = false;
			}
			listTitle = T(contentStartPosition, 500f, 20f, 12u, "");
			hintTag = B(contentStartPosition, 86f, 18f, "右键详情", () =>
			{
				int index = selectedKey.Length > 0 ? rowKeys.IndexOf(selectedKey) : -1;
				ToggleDetails(index >= 0 ? index : 0);
			});
			for (int num4 = 0; num4 < 3; num4++)
			{
				SortBy by = (SortBy)num4;
				sortHeaders[num4] = B(contentStartPosition, 78f, 22f, "", () =>
				{
					SetSort(by);
				});
				NativeButtonStyle.SortHeader(sortHeaders[num4], HeaderNames[num4] + " ↕", 22f, on: false);
			}
			counts.TextColor = MutedText;
			attachParent = null;
			status = T(contentStartPosition, 576f, 20f, 11u, "");
			status.TextColor = MutedText;
			EnsureRows(64);
			if (CanaryBar)
			{
				canaryOut = new ColorImageNode
				{
					Position = contentStartPosition,
					Size = new Vector2(16f, 12f),
					Color = CanaryRed
				};
				canaryOut.AttachNode(content);
				canaryOut.IsVisible = false;
			}
			setupDone = true;
			base.OnSetup(addon, atkValueSpan);
		}
		catch (Exception ex)
		{
			onSetupOk = false;
			Log("[原生UI] OnSetup 构造异常，主窗停用：" + ex);
		}
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

	private void EnsureRows(int need)
	{
		Vector2 contentStartPosition = ContentStartPosition;
		while (rows.Count < need && rows.Count < 320)
		{
			int idx = rows.Count;
			TextButtonNode textButtonNode = B(contentStartPosition, 576f, 28f, "", () =>
			{
				Select(idx);
			});
			textButtonNode.BackgroundNode.IsVisible = false;
			WireRightClick(textButtonNode, idx);
			RowNodes rowNodes = new RowNodes
			{
				Hit = textButtonNode,
				World = new TextNode
				{
					Position = contentStartPosition,
					Size = new Vector2(100f, 22f),
					FontSize = 13u,
					FontType = FontType.Axis,
					TextColor = Gold
				},
				Name = new TextNode
				{
					Position = contentStartPosition,
					Size = new Vector2(154f, 22f),
					FontSize = 13u,
					FontType = FontType.Axis,
					TextColor = ListText
				},
				Bar = new ProgressBarNode { Position=contentStartPosition, Size=new Vector2(232,12) },
				Tail = new TextNode
				{
					Position = contentStartPosition,
					Size = new Vector2(32f, 18f),
					FontSize = 12u,
					FontType = FontType.Axis,
					AlignmentType = AlignmentType.Right,
					TextColor = ListText
				},
				Badge = new TextNode
				{
					Position = contentStartPosition,
					Size = new Vector2(40f, 20f),
					FontSize = 12u,
					FontType = FontType.Axis,
					AlignmentType = AlignmentType.Right,
					TextColor = TrigRed,
					String = "已触发"
				}
			};
			rowNodes.World.AttachNode(content);
			rowNodes.Name.AttachNode(content);
			rowNodes.Bar.AttachNode(content);
			rowNodes.Tail.AttachNode(content);
			rowNodes.Badge.AttachNode(content);
			TextButtonNode hit = rowNodes.Hit;
			TextNode world = rowNodes.World;
			TextNode name = rowNodes.Name;
			TextNode tail = rowNodes.Tail;
			bool flag = (rowNodes.Badge.IsVisible = false);
			bool flag3 = (tail.IsVisible = flag);
			bool flag7 = (rowNodes.Bar.IsVisible = flag3);
			bool flag9 = (name.IsVisible = flag7);
			bool flag11 = (world.IsVisible = flag9);
			hit.IsVisible = flag11;
			rows.Add(rowNodes);
			rowKeys.Add("");
		}
	}

	private unsafe void WirePatchClick(NativeCheckOption b, int idx)
	{
		AtkEventListener.Delegates.ReceiveEvent callback = (AtkEventListener* _, AtkEventType _, int _, AtkEvent* _, AtkEventData* data) =>
		{
			if (data != null)
			{
				byte buttonId = data->MouseData.ButtonId;
				if (buttonId == 1)
				{
					string text = patchKeysArr[idx] ?? "";
					if (text.Length != 0)
					{
						long tickCount = Environment.TickCount64;
						string text2 = text + "|" + buttonId;
						if (!(text2 == patchSoloKey) || tickCount - patchSoloTick >= 300)
						{
							patchSoloTick = tickCount;
							patchSoloKey = text2;
							Log($"[SrankFlag.UI] 版本点击: {text}, btn={buttonId}");
							if (buttonId == 1)
							{
								actions.SelectPatch(text);
							}
							else
							{
								actions.TogglePatch(text);
							}
						}
					}
				}
			}
		};
		// A direct MouseDown on the component owner creates a new blocking hit target
		// ahead of its native checkbox collision node, so left clicks never become
		// ButtonClick. Observe right clicks on the existing native collision node.
		b.CollisionNode.AddEvent(AtkEventType.MouseDown, callback);
	}

	private void SetSort(SortBy by)
	{
		if (sortActive && sortBy == by)
		{
			sortAsc = !sortAsc;
		}
		else
		{
			sortBy = by;
			sortAsc = true;
		}
		sortActive = true;
	}

	private static float PatchNum(string? key)
	{
		float result;
		return key switch
		{
			"ARR" => 2f,
			"HW" => 3f,
			"SB" => 4f,
			"ShB" => 5f,
			"EW" => 6f,
			"DT" => 7f,
			_ => float.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : 0f,
		};
	}

	private void SortHunts(List<MainHuntRow> hunts)
	{
		if (sortActive && hunts.Count >= 2)
		{
			SortBy by = sortBy;
			bool asc = sortAsc;
			hunts.Sort((MainHuntRow a, MainHuntRow b) =>
			{
				int num = by switch
				{
					SortBy.Server => string.CompareOrdinal(a.World, b.World),
					SortBy.Version => string.CompareOrdinal(a.Cn, b.Cn),
					_ => (showOpenRows ? a.PctValue.CompareTo(b.PctValue) : a.EtaSeconds.CompareTo(b.EtaSeconds)),
				};
				int result = ((num != 0) ? ((!asc) ? (-num) : num) : string.CompareOrdinal(a.Key, b.Key));
				return result;
			});
		}
	}

	private void RefreshHeaders()
	{
		for (int i = 0; i < 3; i++)
		{
			TextButtonNode textButtonNode = sortHeaders[i];
			if (textButtonNode != null)
			{
				SortBy sortBy = (SortBy)i;
				bool flag = sortActive && this.sortBy == sortBy;
				string text;
				if (flag)
				{
					text = (sortAsc ? "↑" : "↓");
				}
				else
				{
					text = "↕";
				}
				try
				{
					NativeButtonStyle.SortHeader(textButtonNode, (i == 2 && !showOpenRows ? "距进闸" : HeaderNames[i]) + " " + text, 22f, flag);
				}
				catch (Exception ex)
				{
					Log("[原生UI] 表头文本失败：" + ex.GetType().Name);
					textButtonNode.IsVisible = false;
				}
			}
		}
	}

	private unsafe void WireRightClick(TextButtonNode hit, int idx)
	{
		AtkEventListener.Delegates.ReceiveEvent callback = (AtkEventListener* _, AtkEventType _, int _, AtkEvent* _, AtkEventData* data) =>
		{
			if (data != null)
			{
				switch (data->MouseData.ButtonId)
				{
				case 1:
					RightClick(idx);
					break;
				case 0:
					Select(idx);
					break;
				}
			}
		};
		hit.AddEvent(AtkEventType.MouseDown, callback);
		hit.AddEvent(AtkEventType.MouseClick, callback);
	}

	private void RightClick(int idx)
	{
		if (idx < 0 || idx >= rowKeys.Count)
		{
			return;
		}
		string text = rowKeys[idx];
		if (text.Length != 0)
		{
			long tickCount = Environment.TickCount64;
			if (!(text == lastRightKey) || tickCount - lastRightTick >= 300)
			{
				lastRightTick = tickCount;
				lastRightKey = text;
				ToggleDetails(idx);
			}
		}
	}

	private void Select(int idx)
	{
		if (idx >= 0 && idx < rowKeys.Count)
		{
			string text = rowKeys[idx];
			if (text.Length > 0)
			{
				selectedKey = text;
			}
		}
	}

	private void ToggleDetails(int idx)
	{
		if (idx >= 0 && idx < rowKeys.Count)
		{
			string text = rowKeys[idx];
			if (text.Length != 0)
			{
				selectedKey = text;
				expandedKey = (string.Equals(expandedKey, text, StringComparison.Ordinal) ? "" : text);
				revealDetails = expandedKey.Length > 0;
				actions.Expand(expandedKey);
			}
		}
	}

	internal void ShowDetails(string key)
	{
		selectedKey = expandedKey = key;
		revealDetails = true;
		actions.Expand(key);
	}

	private void CollapseDetails()
	{
		if (expandedKey.Length != 0)
		{
			expandedKey = "";
			actions.Expand("");
		}
	}

	private void BuildDetail()
	{
		if (detailBuilt)
		{
			return;
		}
		detailBuilt = true;
		try
		{
			detailLabel = new TextNode
			{
				Position = ContentStartPosition,
				Size = new Vector2(120f, 22f),
				FontSize = 12u,
				FontType = FontType.Axis,
				AlignmentType = AlignmentType.Left,
				TextColor = MutedText,
				String = ""
			};
			detailLabel.AddTextFlags(TextFlags.MultiLine);
			detailLabel.AttachNode(content);
			detailCond = new TextNode
			{
				Position = ContentStartPosition,
				Size = new Vector2(120f, 22f),
				FontSize = 12u,
				FontType = FontType.Axis,
				AlignmentType = AlignmentType.Left,
				TextColor = CondNeutral,
				String = ""
			};
			detailCond.AddTextFlags(TextFlags.MultiLine);
			detailCond.AttachNode(content);
			detailTime = new TextNode { FontSize=12, TextColor=CondNeutral, AlignmentType=AlignmentType.Left };
			detailTime.AttachNode(content);
			detailRange = new TextNode { FontSize=11, TextColor=MutedText, AlignmentType=AlignmentType.Left };
			detailRange.AttachNode(content);
			detailOk = true;
		}
		catch (Exception ex)
		{
			detailOk = false;
			Log("[原生UI] 详情面板降级：" + ex.GetType().Name);
		}
	}

	private void EnsureDetailBtns(int need)
	{
		Vector2 contentStartPosition = ContentStartPosition;
		while (detailBtns.Count < need)
		{
			int idx = detailBtns.Count;
			detailActions.Add(null);
			detailLayout.Add((0f, 0f, 0f));
			TextButtonNode textButtonNode = B(contentStartPosition, 60f, 22f, "", () =>
			{
				detailActions[idx]?.Invoke();
			});
			detailBtns.Add(textButtonNode);
			textButtonNode.IsVisible = false;
		}
	}

	private void EnsureSelFrame()
	{
		if (selFrameLeft == null)
		{
			Vector4 color = new Vector4(0.94f, 0.72f, 0.28f, 0.95f);
			selRowBg = new ColorImageNode
			{
				Position = Vector2.Zero,
				Size = new Vector2(576f, 28f),
				Color = SelBg
			};
			selRowBg.AttachNode(content, NodePosition.AsFirstChild);
			selRowBg.IsVisible = false;
			selFrameLeft = new ColorImageNode
			{
				Position = Vector2.Zero,
				Size = new Vector2(4f, 28f),
				Color = color
			};
			selFrameLeft.AttachNode(content);
			selFrameLeft.IsVisible = false;
		}
	}

	private List<(float x, int line)> LayCoordLines(List<(string text, Vector4 idle, Vector4 hover, Action act)> coords, float startX, float rightEdge, float actionReserve)
	{
		List<(float, int)> list = new List<(float, int)>(coords.Count);
		if (coords.Count == 0)
		{
			return list;
		}
		int num = -1;
		int num2 = 0;
		while (num2++ <= coords.Count + 2)
		{
			list.Clear();
			float num3 = startX;
			int num4 = 0;
			float num5 = ((num == 0) ? (rightEdge - actionReserve) : rightEdge);
			for (int i = 0; i < coords.Count; i++)
			{
				float num6 = NativeButtonStyle.Measure(coords[i].text) + 16f;
				if (num3 + num6 > num5 && num3 > startX + 0.5f)
				{
					num4++;
					num3 = startX;
					num5 = ((num4 == num) ? (rightEdge - actionReserve) : rightEdge);
				}
				list.Add((num3, num4));
				num3 += num6 + 14f;
			}
			if (num < 0)
			{
				num = ((list.Count > 0) ? list[list.Count - 1].Item2 : 0);
				continue;
			}
			int num7 = 0;
			foreach (var item in list)
			{
				if (item.Item2 > num7)
				{
					num7 = item.Item2;
				}
			}
			if (num7 <= num)
			{
				break;
			}
			num = num7;
		}
		return new List<(float, int)>(list);
	}

	internal void Apply(MainSnapshot s)
	{
		if (!setupDone || !onSetupOk)
		{
			return;
		}
		try
		{
			ApplyInner(s);
		}
		catch (Exception ex)
		{
			try
			{
				Reflow(s, null);
			}
			catch
			{
			}
			if (!applyLogged)
			{
				applyLogged = true;
				Log("[原生UI] Apply 异常（窗口保留，详见 /xllog）：" + ex);
			}
			try
			{
				string text = "";
				string stackTrace = ex.StackTrace;
				if (!string.IsNullOrEmpty(stackTrace))
				{
					int num = stackTrace.IndexOf('\n');
					text = ((num > 0) ? stackTrace.Substring(0, num) : stackTrace).Trim();
				}
				string text2 = ex.GetType().Name + ((text.Length > 0) ? (" @ " + text) : "");
				if (text2.Length > 120)
				{
					text2 = text2.Substring(0, 120);
				}
				SetT(listTitle, text2);
				string text3 = ex.Message;
				if (text3.Length > 120)
				{
					text3 = text3.Substring(0, 120);
				}
				SetT(status, text3);
			}
			catch
			{
			}
		}
	}

	private void ApplyInner(MainSnapshot s)
	{
		showOpenRows = s.OnlyOpen;
		NativeButtonStyle.GhostButton(bConditions, "条件订阅", 26, false);
		if (!searchInitialized)
		{
			searchInitialized = true;
			suppressSearch = true;
			searchInput.String = s.Search;
			suppressSearch = false;
		}
		if (!syncedExpand)
		{
			syncedExpand = true;
			actions.Expand(expandedKey);
		}
		try
		{
			NativeButtonStyle.ClearBadge(bClearSearch, 24f);
			NativeButtonStyle.Apply(bDc, string.IsNullOrEmpty(s.DcNext) ? s.Dc : (s.Dc + "→" + s.DcNext), 130f, 26f, selected: false);
			NativeButtonStyle.Apply(bRefresh, s.Fetching ? "…" : "刷新", 80f, 26f, selected: false);
			NativeButtonStyle.Apply(bAuto, "前往", 96f, 26f, selected: false);
			NativeButtonStyle.Apply(bStop, "停止", 80f, 26f, selected: false);
			bOnlyOpen.Apply("开闸",s.OnlyOpen,110);
			NativeButtonStyle.Apply(bWorldMenu, (menuOpen ? "▾ " : "▸ ") + s.WorldMenuLabel, 230f, 26f, menuOpen);
			NativeButtonStyle.MiniAction(bWorldAll, "全服", 22f, on: false);
			NativeButtonStyle.MiniAction(bWorldClear, "清空", 22f, on: false);
			NativeButtonStyle.MiniAction(bWorldCurrent, "本服", 22f, on: false);
			NativeButtonStyle.FitText(counts, s.Counts.Replace(" ", ""), 220f);
			NativeButtonStyle.HintTag(hintTag, "右键详情", 100f, 18f);
			NativeButtonStyle.GhostButton(bSettings, "设置", 24f, settingsHover);
			if (bHistory != null)
			{
				NativeButtonStyle.GhostButton(bHistory, "历史", 24f, hover: false);
			}
			NativeButtonStyle.FitText(status, s.Status, 576f);
		}
		catch (Exception ex)
		{
			Log("[原生UI] 工具栏文本刷新失败（其余照常）：" + ex.GetType().Name);
		}
		if (s.Dcs.Count > 0 && !dcBuilt)
		{
			dcBuilt = true;
			dcNames.Clear();
			for (int i = 0; i < s.Dcs.Count && i < 8; i++)
			{
				dcNames.Add(s.Dcs[i]);
			}
		}
		for (int j = 0; j < 8; j++)
		{
			NativeRadioOption textButtonNode = dcButtons[j];
			if (textButtonNode != null && (textButtonNode.IsVisible = j < dcNames.Count))
			{
				string text = dcNames[j];
				bool flag2 = string.Equals(text, s.DcSelected, StringComparison.Ordinal);
				try
				{
					textButtonNode.Apply(text,flag2,120);
				}
				catch (Exception ex2)
				{
					Log("[原生UI] 大区按钮文本失败：" + ex2.GetType().Name);
					textButtonNode.IsVisible = false;
				}
			}
		}
		if (s.PatchKeys.Count > 0)
		{
			patchKeys.Clear();
			patchLabels.Clear();
			for (int k = 0; k < s.PatchKeys.Count && k < 7; k++)
			{
				patchKeys.Add(s.PatchKeys[k]);
				patchLabels.Add((k < s.PatchLabels.Count) ? s.PatchLabels[k] : s.PatchKeys[k]);
				patchKeysArr[k] = s.PatchKeys[k];
			}
			for (int l = 0; l < 7; l++)
			{
				NativeCheckOption textButtonNode2 = patchButtons[l];
				if (textButtonNode2 != null && (textButtonNode2.IsVisible = l < patchLabels.Count))
				{
					bool flag4 = l < s.PatchOn.Count && s.PatchOn[l];
					try
					{
						textButtonNode2.Apply(patchLabels[l],flag4,90);
					}
					catch (Exception ex3)
					{
						Log("[原生UI] 版本按钮文本失败：" + ex3.GetType().Name);
						textButtonNode2.IsVisible = false;
					}
				}
			}
		}
		for (int m = 0; m < 16; m++)
		{
			string text2 = ((m < s.Worlds.Count) ? s.Worlds[m] : "");
			worldNames[m] = text2;
			bool flag5 = m < s.WorldOn.Count && s.WorldOn[m];
			string text3;
			if (s.CurrentWorld.Length > 0 && string.Equals(text2, s.CurrentWorld, StringComparison.OrdinalIgnoreCase))
			{
				text3 = "◎";
			}
			else
			{
				text3 = (flag5 ? "●" : "○");
			}
			NativeCheckOption textButtonNode3 = worldMenuItems[m];
			try
			{
				textButtonNode3.Apply(text2,flag5,140,textColor:ServerColors.ForWorld(text2));
			}
			catch (Exception ex4)
			{
				Log("[原生UI] 服务器按钮文本失败：" + ex4.GetType().Name);
				textButtonNode3.IsVisible = false;
			}
		}
		EnsureSelFrame();
		SortHunts(s.Hunts);
		RefreshHeaders();
		int count = s.Hunts.Count;
		EnsureRows(count);
		try
		{
			NativeButtonStyle.FitText(listTitle, s.Ready ? (count == 0 ? (s.OnlyOpen ? "暂无已开闸 S 怪" : "暂无未开闸 S 怪") : $"{(s.OnlyOpen ? "已开闸" : "未开闸")} S 怪 {count}") : "数据未加载", 520f);
		}
		catch (Exception ex5)
		{
			Log("[原生UI] 列表标题文本失败：" + ex5.GetType().Name);
		}
		string selRowKey = null;
		for (int n = 0; n < rows.Count; n++)
		{
			RowNodes rn = rows[n];
			MainHuntRow r = ((n < count) ? s.Hunts[n] : null);
			rowKeys[n] = r?.Key ?? "";
			bool flag6 = r != null;
			ShowNode(rn.Hit, flag6);
			ShowNode(rn.World, flag6);
			ShowNode(rn.Name, flag6);
			ShowNode(rn.Bar, flag6);
			ShowNode(rn.Tail, flag6);
			ShowNode(rn.Badge, flag6 && r != null && r.Triggered);
			if (r != null)
			{
				if (string.Equals(r.Key, selectedKey, StringComparison.Ordinal))
				{
					selRowKey = r.Key;
				}
				RowStep("底纹", () =>
				{
					rn.Hit.BackgroundNode.IsVisible = false;
				}, r.En);
				RowStep("服务器", () =>
				{
					SetT(rn.World, TrimWorld(r.World));
					rn.World.TextColor = ServerColors.ForWorld(r.World);
				}, r.En);
				RowStep("怪名", () =>
				{
					SetT(rn.Name, r.RowLabel);
				}, r.En);
				RowStep("血条", () =>
				{
					float prog = ((r.State == "unknown") ? 0f : Math.Clamp((float)r.PctValue / 100f, 0f, 1f));
					rn.Bar.Progress = prog;
				}, r.En);
				RowStep("百分比", () =>
				{
					SetT(rn.Tail, r.State == "cd" ? r.Clock : r.Pct);
				}, r.En);
				RowStep("已触发红标", () =>
				{
					rn.Badge.TextColor = TrigRed;
				}, r.En);
			}
		}
		if (!detailBuilt)
		{
			BuildDetail();
		}
		MainHuntRow expanded = ((expandedKey.Length > 0) ? s.Hunts.Find((MainHuntRow x) => string.Equals(x.Key, expandedKey, StringComparison.Ordinal)) : null);
		bool flag7 = expanded is { Expanded: true } && detailOk;
		if (bHideHunt != null) bHideHunt.IsVisible = flag7;
		if (bWatchHunt != null)
		{
			bWatchHunt.IsVisible = flag7;
			if (expanded != null)
			{
				NativeButtonStyle.Apply(bWatchHunt, expanded.Watched ? "移出悬浮窗" : "加入悬浮窗", 160, 26, false);
				bWatchHunt.TextTooltip = expanded.World + " · " + expanded.Cn;
			}
		}
		if (bHidden != null) NativeButtonStyle.Apply(bHidden, s.HiddenCount > 0 ? $"已隐藏 {s.HiddenCount}" : "已隐藏", 100, 26, false);
		try
		{
			if (detailLabel != null)
			{
				detailLabel.IsVisible = flag7;
			}
			if (detailCond != null)
			{
				detailCond.IsVisible = false;
			}
			if (detailTime != null) detailTime.IsVisible = false;
			if (detailRange != null) detailRange.IsVisible = false;
			for (int num = 0; num < detailBtns.Count; num++)
			{
				detailBtns[num].IsVisible = false;
			}
			detailLineCount = 0;
			if (flag7 && expanded != null)
			{
				List<(string, Vector4, Vector4, Action)> list = new List<(string, Vector4, Vector4, Action)>();
				foreach (MainPointRow pt in expanded.Points)
				{
					if (!pt.Aetheryte && !pt.Special)
					{
						list.Add(($"({pt.X:0.0}, {pt.Y:0.0})", CoordIdle, CoordHover, () =>
						{
							actions.Flag(expanded.Map, pt.X, pt.Y);
						}));
					}
				}
				List<(string, Vector4, Vector4, Action)> list2 = new List<(string, Vector4, Vector4, Action)>();
				if (expanded.Points.Any((MainPointRow p) => p.Aetheryte))
				{
					list2.Add(("大水晶", AetIdle, AetHover, () =>
					{
						actions.FlagAetheryte(expanded.Map, expanded.Cn);
					}));
				}
				list2.Add(((string, Vector4, Vector4, Action))(expanded.Triggered ? ("已被抢触", ActBlocked, ActBlocked, () =>
				{
					actions.Primary(expanded.En, expanded.World);
				}) : (expanded.HasSpecial ? "执行触发" : "自动前往", ActIdle, ActHover, () =>
				{
					actions.Primary(expanded.En, expanded.World);
				})));
				HuntConditionHelper.HuntCondition condition = HuntConditionHelper.GetCondition(expanded.En);
				var definition = ConditionCatalog.Find(expanded.En);
				if (detailTime != null)
				{
					detailTime.IsVisible = true;
					detailTime.String = expanded.Condition;
					detailTime.TextColor = NativeTheme.ConditionColor(expanded.ConditionPhase);
				}
				if (detailRange != null)
				{
					detailRange.IsVisible = expanded.ConditionRange.Length > 0;
					detailRange.String = expanded.ConditionRange;
				}
				if (detailCond != null && condition == null && definition != null)
				{
					detailCond.IsVisible = true;
					detailCond.String = definition.Method;
					detailCond.TextColor = CondNeutral;
				}
				float num2 = 0f;
				if ((object)condition != null && detailCond != null)
				{
					detailCond.IsVisible = true;
					detailCond.String = condition.Text;
					TextNode? textNode = detailCond;
					bool? met = condition.Met;
					Vector4 textColor;
					if (met.HasValue)
					{
						textColor = ((met != true) ? CondBad : CondOk);
					}
					else
					{
						textColor = CondNeutral;
					}
					textNode.TextColor = textColor;
					num2 = NativeButtonStyle.Measure(condition.Text) + 6f;
					detailCondRelX = 20f;
					detailCondRelY = 0f;
					detailCondW = num2;
				}
				detailLabelW = NativeButtonStyle.Measure("目的地与操作") + 6f;
				detailLabelRelX = 20f + ((num2 > 0f) ? (num2 + 14f) : 0f);
				detailLabelRelY = 0f;
				float num3 = detailLabelRelX + detailLabelW + 14f;
				float num4 = 568f;
				float num5 = 0f;
				foreach (var item in list2)
				{
					num5 += NativeButtonStyle.Measure(item.Item1) + 16f + 14f;
				}
				if (num5 > 0f)
				{
					num5 -= 14f;
				}
				float actionReserve = ((num5 > 0f) ? (num5 + 14f) : 0f);
				List<(float, int)> list3 = LayCoordLines(list, num3, num4, actionReserve);
				int num6 = ((list3.Count > 0) ? list3.Max(((float x, int line) p) => p.line) : 0);
				int num7 = num6 + 1;
				float num8 = Math.Max(num4 - num5, num3);
				EnsureDetailBtns(list.Count + list2.Count);
				int num9 = 0;
				foreach (var item2 in list3)
				{
					(string, Vector4, Vector4, Action) tuple = list[num9];
					float num10 = NativeButtonStyle.Measure(tuple.Item1) + 32f;
					TextButtonNode textButtonNode4 = detailBtns[num9];
					textButtonNode4.IsVisible = true;
					NativeButtonStyle.SizePin(textButtonNode4, num10, 22f);
					NativeButtonStyle.Paint(textButtonNode4, tuple.Item2, tuple.Item3);
					NativeButtonStyle.SetLabel(textButtonNode4, tuple.Item1);
					detailActions[num9] = tuple.Item4;
					detailLayout[num9] = (item2.Item1, (float)item2.Item2 * 22f, num10);
					num9++;
				}
				foreach (var item3 in list2)
				{
					float num11 = NativeButtonStyle.Measure(item3.Item1) + 32f;
					TextButtonNode textButtonNode5 = detailBtns[num9];
					textButtonNode5.IsVisible = true;
					NativeButtonStyle.SizePin(textButtonNode5, num11, 22f);
					NativeButtonStyle.Paint(textButtonNode5, item3.Item2, item3.Item3);
					NativeButtonStyle.SetLabel(textButtonNode5, item3.Item1);
					detailActions[num9] = item3.Item4;
					detailLayout[num9] = (num8, (float)num6 * 22f, num11);
					num8 += num11 + 14f;
					num9++;
				}
				detailLineCount = num7;
			}
		}
		catch (Exception ex6)
		{
			Log("[原生UI] 详情内容更新失败：" + ex6);
		}
		Reflow(s, selRowKey);
	}

	private static string TrimWorld(string? w)
	{
		if (!string.IsNullOrEmpty(w))
		{
			if (w.Length > 5)
			{
				return w.Substring(0, 5);
			}
			return w;
		}
		return "";
	}

	private static Vector4 IconColor(string st)
	{
		return st switch
		{
			"open" => new Vector4(0.38f, 0.95f, 0.55f, 1f),
			"cd" => new Vector4(0.3f, 0.75f, 1f, 1f),
			"over" => new Vector4(0.52f, 0.56f, 0.62f, 1f),
			_ => new Vector4(0.42f, 0.45f, 0.5f, 1f),
		};
	}

	private static string StateCn(string st)
	{
		return st switch
		{
			"open" => "开闸",
			"cd" => "冷却",
			"over" => "过窗",
			_ => "无记录",
		};
	}

	private void Reflow(MainSnapshot s, string? selRowKey)
    {
        if (scroll == null || content == null || fixedRoot == null) return;
        float w = BodyWidth - 12, y = 0;
        float? detailTop = null;
        float detailBottom = 0;
        fixedRoot.Position = Vector2.Zero;
        float searchWidth=MathF.Floor(Math.Clamp(w*.28f,90,180));
        if(MathF.Abs(searchInput.Width-searchWidth)>.1f)searchInput.Width=searchWidth;
        bDc.IsVisible = false;
        y = Flow(w, y, searchInput, bClearSearch, bRefresh);
        y = Flow(w, y, bConditions, bHistory, bSettings, bHidden, bWatch);
        bManualHint.IsVisible = s.NeedsManualRefresh;
        if (s.NeedsManualRefresh) { bManualHint.String = s.Fetching ? "公开熊站 · 手动加载中…" : "公开熊站 · 点击“刷新”手动加载"; bManualHint.Fit(w, 11, true); y = Flow(w, y, bManualHint); }
        y = Flow(w, y, dcButtons);
        y = Flow(w, y, patchButtons);
        bWorldAll.IsVisible = bWorldClear.IsVisible = bWorldCurrent.IsVisible = menuOpen;
        y = Flow(w, y, bAuto, bStop);
        y = Flow(w, y, bOnlyOpen, bWorldMenu);
        if(menuOpen)y=Flow(w,y,bWorldAll,bWorldClear,bWorldCurrent);
        counts.IsVisible = true; counts.Fit(w, 11); y = Flow(w, y, counts);
        for (int i = 0; i < worldMenuItems.Length; i++) worldMenuItems[i].IsVisible = menuOpen && i < s.Worlds.Count;
        if (menuOpen) y = Flow(w, y, worldMenuItems.Cast<NodeBase?>().ToArray());
        listTitle.Fit(MathF.Max(120, w - 100), 12); y = Flow(w, y, listTitle, hintTag);
        var columns = NativeWindowLayout.MainColumns(w);
        float worldX = columns.WorldX, nameX = columns.NameX, barX = columns.ProgressX, barW = columns.ProgressWidth;
        sortHeaders[0].Position = new Vector2(worldX, y);
        sortHeaders[1].Position = new Vector2(nameX, y);
        sortHeaders[2].Position = new Vector2(barX, y);
        y += 30;
        fixedRoot.Size = new Vector2(w, y);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (i >= s.Hunts.Count)
            {
                foreach (var node in new NodeBase[] {row.Hit,row.World,row.Name,row.Bar,row.Tail,row.Badge}) node.IsVisible = false;
                continue;
            }
            var hunt = s.Hunts[i];
            bool open = hunt.State == "open";
            row.Hit.Size = new Vector2(w, 28); row.Hit.Position = new Vector2(0,y);
            row.World.Position = new Vector2(worldX,y+4); row.World.Fit(columns.WorldWidth,13);
            row.Name.Position = new Vector2(nameX,y+4); row.Name.Fit(columns.NameWidth - (hunt.Triggered ? 44 : 0),13);
            row.Badge.Position = new Vector2(barX-44,y+4); row.Badge.IsVisible = hunt.Triggered;
            row.Bar.IsVisible = open;
            if (open)
            {
                row.Bar.Size=new Vector2(barW,12);
                row.Bar.Progress=Math.Clamp((float)hunt.PctValue/100f,0,1);
                row.Bar.Position=new Vector2(barX,y+8);
            }
            row.Tail.Position = new Vector2(open ? barX+barW+6 : barX,y+4);
            row.Tail.AlignmentType = open ? AlignmentType.Right : AlignmentType.Left;
            row.Tail.Fit(open ? 36 : barW+42,12);
            row.Tail.IsVisible = true;
            if (selRowBg != null && selFrameLeft != null && hunt.Key == selRowKey)
            {
                selRowBg.Position = new Vector2(0,y); selRowBg.Size = new Vector2(w,28); selRowBg.IsVisible = true;
                selFrameLeft.Position = new Vector2(0,y); selFrameLeft.IsVisible = true;
            }
            y += 28;
            if (hunt.Key != expandedKey || !hunt.Expanded) continue;
            detailTop = y - 28;
            string tooltip = (ConditionCatalog.Find(hunt.En)?.Method ?? "") + "\n" + hunt.ConditionRange;
            foreach (var text in new[] {detailCond, detailTime, detailRange})
            {
                if (text == null || !text.IsVisible) continue;
                text.Fit(w-32,text==detailRange?11u:12u,true);
                text.TextTooltip=tooltip;
                text.Position=new Vector2(20,y); y+=text.Height+6;
            }
            if (detailLabel != null && detailLabel.IsVisible) { detailLabel.String="目的地与操作"; detailLabel.Fit(w-32,12,true); detailLabel.Position=new Vector2(20,y); y+=detailLabel.Height+6; }
            float dx=20, lineH=0;
            foreach(var button in detailBtns)
            {
                if(!button.IsVisible) continue;
                if(dx+button.Width>w && dx>20) {dx=20;y+=lineH+4;lineH=0;}
                button.Position=new Vector2(dx,y); dx+=button.Width+8; lineH=MathF.Max(lineH,button.Height);
            }
            y+=lineH+8;
            float actionsX = 20;
            foreach (var button in new[] { bWatchHunt, bHideHunt })
            {
                if (button == null || !button.IsVisible) continue;
                if (actionsX + button.Width > w && actionsX > 20) { actionsX = 20; y += 32; }
                button.Position = new Vector2(actionsX, y);
                actionsX += button.Width + 8;
            }
            y += 34;
            detailBottom = y;
        }
        status.Fit(w,11,true); status.Position=new Vector2(0,y); y+=status.Height+8;
        scroll.Position = ContentStartPosition;
        var viewport = new Vector2(MathF.Floor(BodyWidth), MathF.Floor(BodyHeight));
        float contentHeight = MathF.Ceiling(MathF.Max(y, viewport.Y));
        bool viewportChanged = scroll.Size != viewport;
        bool heightChanged = content.Height != contentHeight;
        if (viewportChanged) scroll.Size = viewport;
        if (heightChanged) content.Height = contentHeight;
        if (heightChanged) scroll.RecalculateSizes();
        if (revealDetails && detailTop is { } top)
        {
            float position = scroll.ScrollBarNode.ScrollPosition;
            if (top < position || detailBottom > position + BodyHeight)
                scroll.ScrollBarNode.ScrollPosition = Math.Clamp(Math.Min(top, detailBottom - BodyHeight + 8), 0, scroll.ScrollBarNode.ScrollMaxPosition);
            revealDetails = false;
        }
        if(trackNode!=null) trackNode.IsVisible=false;
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
