using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Game.Command;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Game.NativeWrapper;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace SrankFlag;

public sealed partial class Plugin : IDalamudPlugin, IDisposable
{
	private sealed record AutoJob(string World, string HuntCn, string HuntEn, string Map);

	private sealed record NavHunt(string Cn, string Map, (float x, float y)[] Points);

	private sealed class SnapshotData
	{
		public string dc { get; set; } = "";

		public double snapshot { get; set; }

		public string[] worlds { get; set; } = Array.Empty<string>();

		public HuntData[] hunts { get; set; } = Array.Empty<HuntData>();
	}

	private sealed class HuntData
	{
		public string en { get; set; } = "";

		public string cn { get; set; } = "";

		public string map { get; set; } = "";

		public string patch { get; set; } = "";

		public double cd { get; set; }

		public double win { get; set; }

		public double[] mt { get; set; } = new double[0];

		public bool counted { get; set; }

		public bool prob { get; set; }

		public string kind { get; set; } = "";

		public string[] tips { get; set; } = Array.Empty<string>();

		public double lastDeath { get; set; }

		public WorldDeath[] worlds { get; set; } = Array.Empty<WorldDeath>();
	}

	private sealed class WorldDeath
	{
		public string world { get; set; } = "";

		public double lastDeath { get; set; }
		public bool isMaint { get; set; }
	}

	private sealed class PointSet
	{
		public int[] ids { get; set; } = Array.Empty<int>();

		public float[][] pts { get; set; } = Array.Empty<float[]>();

		public int[] bm { get; set; } = Array.Empty<int>();

		public Dictionary<string, PointFlags> w { get; set; } = new Dictionary<string, PointFlags>();
	}

	private sealed class PointFlags
	{
		public int[] r { get; set; } = Array.Empty<int>();

		public int[] y { get; set; } = Array.Empty<int>();

		public int[] a { get; set; } = Array.Empty<int>();
	}

	private sealed class DataFile
	{
		public SnapshotData? data { get; set; }

		public List<SnapshotData> dcs { get; set; } = new List<SnapshotData>();

		public Dictionary<string, PointSet> points { get; set; } = new Dictionary<string, PointSet>();
	}

	private sealed record Pt(int Id, float X, float Y, char S);

	private sealed class DcFilter
	{
		public List<string> worlds { get; set; } = new List<string>();

		public bool worldsSet { get; set; }

		public List<string> patches { get; set; } = new List<string>();

		public bool onlyOpen { get; set; } = true;

		public string search { get; set; } = "";
	}

	private sealed class UiPrefs
	{
		public int windowFilterVersion { get; set; }
		public string dc { get; set; } = "";

		public Dictionary<string, DcFilter> byDc { get; set; } = new Dictionary<string, DcFilter>(StringComparer.Ordinal);
	}

	private readonly record struct FlagResult(bool Ok, string Message, uint TerritoryId = 0u, uint MapId = 0u);

	private readonly record struct GotoResult(bool Ok, string Message);

	private enum XuanxueAdvance
	{
		NextSlot,
		NextGroup
	}

	private enum PrepKind
	{
		Pet,
		Cook,
		Blu,
		Garden,
		Warrior
	}

	private readonly record struct HuntPrep(PrepKind Kind, string Arg);

	private readonly record struct AetheryteHit(string Name, float X, float Y, double Dist, uint Id = 0, uint Territory = 0);

	private readonly record struct DiscardResult(bool Ok, string Message);

	private readonly record struct WeeEaResult(bool Ok, string Message, List<string> Names);

	private sealed class AutoSettings
	{
		public bool autoSummonMinion { get; set; }

		public bool autoLeaveDuty { get; set; }

		public bool autoConfirmReturn { get; set; } = true;
	}

	private sealed class LiveMap
	{
		public int[] ids { get; set; } = Array.Empty<int>();

		public float[][] pts { get; set; } = Array.Empty<float[]>();

		public int[] bmob { get; set; } = Array.Empty<int>();
	}

	private sealed class SonarKillFile
	{
		public Dictionary<string, double> kills { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

		public List<SonarKillRecord> recent { get; set; } = new List<SonarKillRecord>();
	}

	private sealed class SonarKillRecord
	{
		public double at { get; set; }

		public double kill { get; set; }

		public double old { get; set; }

		public string dc { get; set; } = "";

		public string world { get; set; } = "";

		public string cn { get; set; } = "";

		public string en { get; set; } = "";

		public string map { get; set; } = "";

		public double oldPct { get; set; }

		public double newPct { get; set; }
	}

	private sealed record SonarHit(string Head, string World, double X, double Y, double SeenAt, bool IsKill, bool IsReset = false);

	private enum MainTriggerMode
	{
		Enabled,
		Disabled
	}

	private sealed class TriggeredFile
	{
		public int v { get; set; } = 8;

		public string mainMode { get; set; } = "on";

		public Dictionary<string, double> items { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

		public Dictionary<string, double> floors { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

		public Dictionary<string, double> killed { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

		public Dictionary<string, double> seen { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

		public Dictionary<string, string> src { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
	}

	private bool autoRunOpen;

	private bool autoHudOpen;

	private bool autoPaused;

	private string autoPauseReason = "";

	private int autoStyle = 1;

	private bool autoOnlyOpen = true;

	private bool autoAllWorlds = true;

	private readonly HashSet<string> autoWorlds = new HashSet<string>(StringComparer.Ordinal);

	private readonly HashSet<string> autoHunts = new HashSet<string>(StringComparer.Ordinal);

	private readonly HashSet<string> autoGroupsClosed = new HashSet<string>(StringComparer.Ordinal);

	private int autoNativePage;

	private static readonly string[] AutoMapNames = new string[11]
	{
		"克扎玛乌卡湿地", "亚克特尔树海", "遗产之地", "活着的记忆", "厄尔庇斯", "加雷马", "迷津", "安穆·艾兰", "雷克兰德", "珂露西亚岛",
		"黑风海"
	};

	private static readonly HashSet<string> AutoMaps = new HashSet<string>(AutoMapNames, StringComparer.Ordinal);

	private readonly List<AutoJob> autoQueue = new List<AutoJob>();

	private int autoIdx;

	private bool autoRunning;

	private int autoGen;

	private int autoToken;

	private DateTime autoJobStart;

	private string autoStatus = "未开始：勾选服务器和 S 后点「开始自动前往」";

	private string autoCurrent = "";

	private string autoHit = "";

	private float autoDist = -1f;

	private int autoTotal;

	private DateTime autoGatherDoneAt;

	private DateTime autoWaitBeganAt;

	private DateTime autoRouteDoneAt;

	private bool autoHasRoute;

	private DateTime autoDiscardDoneAt;

	private DateTime autoLastLogAt;

	private DateTime autoRouteStartedAt;

	private bool autoRetriedRoute;

	private List<Pt> autoJobGreens = new List<Pt>();

	private bool autoPrepFailed;

	private DateTime autoTravelAt;

	private int autoTravelRetry;

	private const int AutoTravelRetries = 2;

	private const double AutoCrossFailSeconds = 90.0;

	private const double AutoTeleFailSeconds = 90.0;

	private const int TriggerGraceSeconds = 5;

	private AutoJob? autoJob;

	private DateTime autoHitHandledAt;

	private long autoTriggerCheckAt;

	private readonly List<(string world, string cn)> autoHits = new List<(string, string)>();

	private readonly List<(string cn, string kind, string detail)> autoSkipped = new List<(string, string, string)>();

	private static readonly HashSet<string> QuickActionHunts = new HashSet<string>(StringComparer.Ordinal) { "多智兽", "阿姆斯特朗", "天气预报机器人" };

	private const int AutoBombTries = 4;

	private const string CommandName = "/srankflag";

	private const string CommandAlias = "/水桶";

	private const int Port = 18099;

	private static readonly Dictionary<uint, (uint ItemId, int Amount)> TriggerDiscardItems = new Dictionary<uint, (uint, int)>
	{
		{
			813u,
			(27850u, 1)
		},
		{
			961u,
			(36256u, 5)
		},
		{
			1189u,
			(7767u, 50)
		}
	};

	private static readonly HashSet<string> NoPointHunts = new HashSet<string>(StringComparer.Ordinal)
	{
		"俄菲翁尼厄斯", "顾尼图", "先驱勇士 阿提卡斯", "戾虫", "内尤佐缇", "得到宽恕的炫学", "山谢亚", "沉思之物", "伊休妲", "颇胝迦",
		"盐和光"
	};

	private static readonly HashSet<string> PointHunts = new HashSet<string>(StringComparer.Ordinal)
	{
		"咕尔呱洛斯", "南迪", "夺心魔", "布隆特斯", "凯撒贝希摩斯", "伽马", "兀鲁忽乃朝鲁", "多智兽", "阿格拉俄珀", "布弗鲁",
		"狭缝", "阿姆斯特朗", "厌忌之人 奇里格", "伊努索奇", "天气预报机器人"
	};

	private static readonly Dictionary<string, string> HuntAetheryte = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["山谢亚"] = "谢申|青磷",
		["得到宽恕的炫学"] = "图姆拉"
	};

	private static readonly Dictionary<string, uint> DiscardMaps = new Dictionary<string, uint>(StringComparer.Ordinal)
	{
		["雷克兰德"] = 813u,
		["厄尔庇斯"] = 961u,
		["亚克特尔树海"] = 1189u
	};

	private static readonly Dictionary<uint, uint> MinionMap = new Dictionary<uint, uint>
	{
		{ 1188u, 180u },
		{ 960u, 423u },
		{ 816u, 303u },
		{ 956u, 434u },
		{ 614u, 215u },
		{ 397u, 148u }
	};

	private static readonly NavHunt[] NavHunts = new NavHunt[6]
	{
		new NavHunt("阿格拉俄珀", "伊尔美格", new (float, float)[17]
		{
			(27.15f, 18.97f),
			(31.52f, 13.71f),
			(19.72f, 34.94f),
			(7.53f, 22.91f),
			(9.75f, 30.75f),
			(22.93f, 28.69f),
			(8.09f, 27.1f),
			(24.91f, 37.23f),
			(11.16f, 15.91f),
			(24.54f, 32.94f),
			(13.74f, 34.11f),
			(20.09f, 8.62f),
			(25.59f, 7.06f),
			(19.31f, 27.32f),
			(34.1f, 7.28f),
			(10.56f, 20.28f),
			(29.35f, 5.41f)
		}),
		new NavHunt("伊努索奇", "克扎玛乌卡湿地", new (float, float)[11]
		{
			(9.5f, 7.87f),
			(20.21f, 28.37f),
			(37f, 20.01f),
			(5.32f, 28.86f),
			(23.97f, 36.71f),
			(16.3f, 17.27f),
			(6.44f, 12.05f),
			(15.83f, 23.67f),
			(36.71f, 17.51f),
			(34.42f, 36.52f),
			(30f, 15.75f)
		}),
		new NavHunt("阿姆斯特朗", "加雷马", new (float, float)[10]
		{
			(27.59f, 33.96f),
			(29.06f, 20.89f),
			(15.79f, 19.65f),
			(11.78f, 17.27f),
			(12.13f, 12.84f),
			(33.01f, 21.85f),
			(23.43f, 25.76f),
			(9.79f, 11.67f),
			(32.45f, 32.55f),
			(26.92f, 8.08f)
		}),
		new NavHunt("天气预报机器人", "活着的记忆", new (float, float)[10]
		{
			(12.47f, 37.84f),
			(5.17f, 12.88f),
			(12.95f, 27.82f),
			(18.91f, 20.21f),
			(4.26f, 28.96f),
			(26.94f, 31.32f),
			(37.19f, 18.7f),
			(38.99f, 30.05f),
			(32.91f, 20.94f),
			(4.31f, 33.28f)
		}),
		new NavHunt("狭缝", "天外天垓", new (float, float)[10]
		{
			(14.92f, 36.14f),
			(16.29f, 26.09f),
			(19.31f, 9.82f),
			(17.61f, 30.31f),
			(21.19f, 34.15f),
			(8.23f, 20.42f),
			(13.28f, 10.44f),
			(11.93f, 22.02f),
			(27.97f, 12.43f),
			(15.45f, 32.6f)
		}),
		new NavHunt("厌忌之人 奇里格", "奥阔帕恰山", new (float, float)[10]
		{
			(19.04f, 14.3f),
			(28.82f, 9.46f),
			(11.5f, 9.12f),
			(21.93f, 20.75f),
			(7.51f, 25.61f),
			(28.14f, 22.88f),
			(15.73f, 24.29f),
			(34.06f, 19.21f),
			(21.92f, 16.86f),
			(20.31f, 23.7f)
		})
	};

	private static readonly Dictionary<string, double[]> MaintPairOverride = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
	{
		["Minhocao"] = new double[2] { 34.2, 4.8 },
		["Zona Seeker"] = new double[2] { 34.2, 3.6 }
	};

	private readonly IGameGui gameGui;

	private readonly ICommandManager commandManager;

	private readonly IDataManager dataManager;

	private readonly IFramework framework;

	private readonly IPluginLog log;

	private readonly IChatGui chatGui;

	private readonly IClientState clientState;

	private readonly IObjectTable objectTable;

	private readonly ICondition condition;

	private readonly ITargetManager targets;

	private readonly IGameInventory gameInventory;

	private readonly IDalamudPluginInterface pi;

	private readonly IUiBuilder uiBuilder;

	private NativeHudRuntime? nativeHud;

	private SrankHistory? history;

	private bool historyWindowOpen;

	private HistoryFilter historyFilter;

	private int historyPage;

	private bool legacyMainOpen;

	private bool settingsOpen;

	private string mainExpandedKey = "";

	private readonly CancellationTokenSource cts = new CancellationTokenSource();

	private TcpListener? listener;

	private bool autoSummonMinion;

	private bool autoLeaveDuty;

	private bool autoConfirmReturn = true;

	private DateTime lastReturnClick;

	private bool returnClickLogged;

	private bool mainWindowOpen = true;

	private bool settingsWindowOpen;

	private bool teleportSnapshotPending;

	private bool teleportSnapshotMain;

	private bool teleportSnapshotHud;

	private bool teleportSnapshotAuto;

	private bool teleportSnapshotSettings;

	private bool teleportSnapshotHistory;
    private bool teleportSnapshotConditions;

	private DateTime teleportSnapshotAtUtc = DateTime.MinValue;

	private DateTime restoreAfterTeleportUtc = DateTime.MinValue;

	private bool wasBetweenAreas;

	private bool stableWindowMain;

	private bool stableWindowHud;

	private bool stableWindowAuto;

	private bool stableWindowSettings;

	private bool stableWindowHistory;
    private bool stableWindowConditions;

	private bool stableWindowValid;

	private int runGen;

	private bool halted;

	private float gounituHpMark;

	private readonly List<uint> unlockedMinions = new List<uint>();

	private DateTime lastSummonCheck = DateTime.MinValue;

	private DateTime lastLeaveTry = DateTime.MinValue;

	private readonly string pluginDirectory;

	private string dataError = "";

	private string currentDc = "";

	private DataFile? srankData;

	private readonly HashSet<string> expandedHunts = new HashSet<string>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> huntWorld = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly HashSet<string> worldFilter = new HashSet<string>(StringComparer.Ordinal);

	private readonly HashSet<string> tilesExpanded = new HashSet<string>(StringComparer.Ordinal);

	private readonly HashSet<string> tipsOpen = new HashSet<string>(StringComparer.Ordinal);

	private bool lightMode;

	private bool themeLoaded;

	private bool miniMode;

	private Vector2? miniPos;

	private bool miniDragging;

	private ISharedImmediateTexture? miniIcon;

	private bool miniIconTried;

	private bool searchInHeader;

	private List<string> lastListed = new List<string>();

	private bool onlyOpen = true;

	private string searchText = "";

	private readonly byte[] searchBuf = new byte[64];

	private readonly HashSet<string> patchSel = new HashSet<string>(StringComparer.Ordinal);

	private UiPrefs prefs = new UiPrefs();

	private bool prefsLoaded;

	private string lastFilterSig = "";

	private DateTime filterDirtyAt = DateTime.MinValue;

	private string drawErrorLogged = "";

	private const string FontGlyphs = "·×—…⌃⌄▶▸▾●⟳\u3000、。「」不丢个中为之乌了亚产人从令件伊休传佐作俄修先克入全兰关具兽内再写冷凯出击分切到制刷前副加务动助努劳勇包匹占卡即却厄厌原发取口只召可右号叹同名后启员命唤器回因图在地坐垓塔士夏外多够大天太失奇奈奥妲姆始子字存学安家宽寻小尔尚尤就尼展山岛岸川左已布帕常庇开异弃弗张当录影往得德忆忌快态思怪恕息恰戏成或戾所手扎打找把报拆拉拖持按换据排接控提插搜支收数整文斯新旗无旧时明显晓晶智暂暗曦最月有服朗未本机杀条来析林查柴标树格检森次正步每气水沉没法泪津活海清游湖湿满炫点焰照熊物特状独狭玛玩珀珂理用的监目相看着知示穆空窗立站端等管系索红线组细终经绘统绪维绿缇缓编缝缺网置美翁背胝能自艾茶荒菲萨落藏虫行被西要解触记试询该请读调谢豆败起足跨路跳踩身载辉过近还这连迦迷退送逆途道遗部配里重野量金钟银锁键闭间阔阳阿附除随隐雷需露页顾预颇风飞马驱鲁黑（），：？一与且东云剩区央实微推日星晴暴灵现碧秒算纳续翻致薄衣计诺进采阴集雨雪雾上下专业串主乃亮伐会伽体余你倒做兀光共准初利剧功化南去反叠向含呱和咕围备复夺套孔完宠属巡巨师希并建弱弹形心志忽恶情意战执技折指摩撒改放效斗更朝朱束构楚概段洛流测深烹爆版牛犎狂狩猎率生申画留白盐直确磷科程符第筛精级结继职色芙英范蓝蝶装认讨贝走超软返迪选逐造都铺闪闸陆限隆雀雅青面题骑魔鸟默但绑设跑我停也候别国它帝幻搭是混穿判好定归零差话任工延止消何儿己断离给轮块模住几稳刻咏唱头神否掉园艺《》『』【】・三仍伤使冰受害对射尝平待挑攻斧猛突约蛭衅诱距边钓锥！．；～位域基潜规固补交代单吃合品方菜表世侧凑列勾双变响圈境少层底式强悬感户择旦期栏样框桶永浮潮片界盯种稍紧聊至苍莲让错队顶龙";

	private IFontAtlas? fontAtlas;

	private IFontHandle? fontHandleS;

	private IFontHandle? fontHandleM;

	private IFontHandle? fontHandleL;

	private ILockedImFont? fontLockS;

	private ILockedImFont? fontLockM;

	private ILockedImFont? fontLockL;

	private ImFontPtr fontPtrS;

	private ImFontPtr fontPtrM;

	private ImFontPtr fontPtrL;

	private bool fontReady;

	private bool fontInitTried;

	private bool fontProbeLogged;

	private bool winResizing;

	private Vector2 winResizeBase;

	private Vector2 winResizeStart;

	private Vector2? winPendingSize;

	private const float TeleportCostYalms = 170f;

	private const float EarlyTriggerYalms = 30f;

	private const float StrictArriveYalms = 5f;

	private static readonly HashSet<string> StrictArriveHunts = new HashSet<string>(StringComparer.Ordinal) { "多智兽", "阿姆斯特朗" };

	private const string BluBlizzard = "/青魔法技能 狂风暴雪";

	private const string BluSelfDestruct = "/青魔法技能 自爆";

	private const string Swiftcast = "/ac 即刻咏唱";

	private const string SheathWeapon = "/bm off";

	private const string WeaknessName = "意志薄弱";

	private static readonly Pt GounituPt = new Pt(1, 29.8f, 3.9f, 'g');

	private Action<bool>? gounituDone;

	private DateTime gounituFinishedAt;

	private bool gounituFlowOk;

	private DateTime gounituStartedAt;

	private int aetheryteBombLimit;

	private static readonly (float X, float Y)[][] XuanxueGrassGroups = new (float, float)[6][]
	{
		new (float, float)[2]
		{
			(11.93f, 13.02f),
			(12.25f, 13.37f)
		},
		new (float, float)[2]
		{
			(12.39f, 13.32f),
			(12.44f, 13.47f)
		},
		new (float, float)[2]
		{
			(12.78f, 15.89f),
			(12.81f, 15.54f)
		},
		new (float, float)[2]
		{
			(13.16f, 16.04f),
			(13.27f, 16.03f)
		},
		new (float, float)[2]
		{
			(15.24f, 14.02f),
			(15.43f, 13.76f)
		},
		new (float, float)[2]
		{
			(15.46f, 13.66f),
			(15.51f, 13.85f)
		}
	};

	private const string XuanxueMap = "珂露西亚岛";

	private const string XuanxueGrass = "草场";

	private const string XuanxueGrassAlt = "图姆拉村";

	private const string XuanxueItem = "矮人棉";

	private const int XuanxueStrikes = 300;

	private const int XuanxueWindowRetry = 6;

	private const int XuanxueReopenGapMs = 1500;

	private const float XuanxueGrassRange = 30f;

	private const float XuanxueArriveYalms = 8f;

	private const int XuanxueGatherGapMs = 3000;

	private const int XuanxueStallLimit = 4;

	private const uint JobCulinarian = 15u;

	private const uint JobBlueMage = 36u;

	private const uint JobBotanist = 17u;

	private const uint JobWarrior = 21u;

	private static readonly Dictionary<string, HuntPrep> HuntPreps = new Dictionary<string, HuntPrep>(StringComparer.Ordinal)
	{
		["阿格拉俄珀"] = new HuntPrep(PrepKind.Pet, "朱孔雀"),
		["布弗鲁"] = new HuntPrep(PrepKind.Pet, "小巨魔"),
		["狭缝"] = new HuntPrep(PrepKind.Pet, "小异亚"),
		["伊努索奇"] = new HuntPrep(PrepKind.Pet, "蓝闪蝶"),
		["先驱勇士 阿提卡斯"] = new HuntPrep(PrepKind.Cook, "犎牛牛排"),
		["多智兽"] = new HuntPrep(PrepKind.Blu, "自爆"),
		["阿姆斯特朗"] = new HuntPrep(PrepKind.Blu, "自爆"),
		["天气预报机器人"] = new HuntPrep(PrepKind.Blu, "狂风暴雪"),
		["牛头黑神"] = new HuntPrep(PrepKind.Blu, "自爆"),
		["得到宽恕的炫学"] = new HuntPrep(PrepKind.Garden, "园艺工"),
		["顾尼图"] = new HuntPrep(PrepKind.Warrior, "战士")
	};

	private const string DefaultMountCmd = "/gaction 随机坐骑";

	private const string FallbackMountCmd = "/mount 专属陆行鸟";

	private const float HeadH = 62f;

	private const double CdHours = 84.0;

	private const double WinHours = 48.0;

	private static bool lightTheme;

	private static Vector4 V1Bg = new Vector4(0.043f, 0.051f, 0.051f, 0.99f);

	private static Vector4 V1Bg2 = new Vector4(0.059f, 0.071f, 0.067f, 1f);

	private static Vector4 V1Card = new Vector4(0.075f, 0.086f, 0.082f, 1f);

	private static Vector4 V1Input = new Vector4(0.055f, 0.063f, 0.059f, 1f);

	private static Vector4 V1Border = new Vector4(0.118f, 0.137f, 0.129f, 1f);

	private static Vector4 V1BorderHi = new Vector4(0.173f, 0.196f, 0.18f, 1f);

	private static Vector4 V1BorderSoft = new Vector4(0.173f, 0.196f, 0.18f, 0.55f);

	private static Vector4 V1Accent = new Vector4(0.839f, 0.949f, 0.294f, 1f);

	private static Vector4 V1AccentInk = new Vector4(0.839f, 0.949f, 0.294f, 1f);

	private static Vector4 V1Text = new Vector4(0.949f, 0.961f, 0.937f, 1f);

	private static Vector4 V1Dim = new Vector4(0.455f, 0.502f, 0.475f, 1f);

	private static Vector4 V1Faint = new Vector4(0.455f, 0.502f, 0.475f, 0.45f);

	private static Vector4 V1FaintSoft = new Vector4(0.455f, 0.502f, 0.475f, 0.75f);

	private static Vector4 V1Lime = new Vector4(0.839f, 0.949f, 0.294f, 1f);

	private static Vector4 V1Mint = new Vector4(0.231f, 0.89f, 0.541f, 1f);

	private static Vector4 V1Pink = new Vector4(1f, 0.302f, 0.553f, 1f);

	private static Vector4 V1Panel = new Vector4(0.059f, 0.071f, 0.067f, 1f);

	private static Vector4 V1PanelBorder = new Vector4(0.118f, 0.137f, 0.129f, 1f);

	private static Vector4 V1Hover = new Vector4(1f, 1f, 1f, 0.045f);

	private static Vector4 V1HoverMid = new Vector4(1f, 1f, 1f, 0.07f);

	private static Vector4 V1HoverStrong = new Vector4(1f, 1f, 1f, 0.13f);

	private static Vector4 V1Sel = new Vector4(1f, 1f, 1f, 0.03f);

	private static Vector4 V1Scroll = new Vector4(1f, 1f, 1f, 0.08f);

	private static Vector4 V1ScrollHi = new Vector4(1f, 1f, 1f, 0.14f);

	private static Vector4 V1FrameHi = new Vector4(0.098f, 0.11f, 0.106f, 1f);

	private static Vector4 V1BtnHi = new Vector4(0.11f, 0.125f, 0.117f, 1f);

	private static Vector4 V1BtnAct = new Vector4(0.075f, 0.086f, 0.082f, 1f);

	private static Vector4 V1PopBg = new Vector4(0.043f, 0.051f, 0.051f, 0.98f);

	private static readonly Vector4 Transparent = new Vector4(0f, 0f, 0f, 0f);

	private static Vector4 V1Track = new Vector4(0.086f, 0.098f, 0.094f, 1f);

	private static readonly Vector4 V1OnAccent = new Vector4(0.043f, 0.051f, 0.051f, 1f);

	private static readonly Vector4[] WorldPalette = new Vector4[12]
	{
		new Vector4(0.96f, 0.655f, 0.26f, 1f),
		new Vector4(0.08f, 0.76f, 0.22f, 1f),
		new Vector4(0.69f, 0.76f, 0.08f, 1f),
		new Vector4(0.44f, 0.72f, 1f, 1f),
		new Vector4(1f, 0.41f, 0.5f, 1f),
		new Vector4(0.76f, 0.55f, 1f, 1f),
		new Vector4(0.15f, 0.8f, 0.78f, 1f),
		new Vector4(1f, 0.58f, 0.2f, 1f),
		new Vector4(0.88f, 0.78f, 0.25f, 1f),
		new Vector4(0.36f, 0.62f, 1f, 1f),
		new Vector4(1f, 0.45f, 0.72f, 1f),
		new Vector4(0.55f, 0.85f, 0.28f, 1f)
	};

	private const string BearApi = "https://tracker.beartoolkit.com/api/getHuntInfo";

	private const string LiveApi = "http://119.29.119.179/api/live?dc=";

	private const string SelfHostedDc = "豆豆柴";

	private static readonly TimeSpan BearInterval = TimeSpan.FromMinutes(15L);

	private static readonly TimeSpan BearForceFloor = TimeSpan.FromSeconds(10L);

	private const int BearFallbackCooldownMin = 60;

	private const int BearFallbackMaxPairs = 60;

	private static readonly Lazy<HttpClient> BearHttp = new Lazy<HttpClient>(() =>
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromSeconds(20L);
		httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
		httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://tracker.beartoolkit.com/srank");
		return httpClient;
	});
	private readonly Lazy<BearBulkClient> bearBulk = new(() => new());

	private DateTime lastBearFetch = DateTime.MinValue;

	private DateTime lastBearFallback = DateTime.MinValue;

	private DateTime lastBearOk = DateTime.MinValue;

	private bool bearFetching;
	private CancellationTokenSource? bearRequest;
	private int bearRequestGeneration;
	private bool bearNotify;

	private string bearStatus = "尚未拉取";

	private const string RestartApi = "http://119.29.119.179/api/restart";

	private double restartAt;

	private DateTime lastRestartFetch = DateTime.MinValue;

	private const string PointsSnapshotApi = "http://119.29.119.179/api/tracker-points/snapshot";

	private static readonly TimeSpan PointsInterval = TimeSpan.FromSeconds(60L);

	private readonly Dictionary<string, LiveMap> liveMaps = new Dictionary<string, LiveMap>(StringComparer.Ordinal);

	private readonly Dictionary<string, Dictionary<int, char>> liveStates = new Dictionary<string, Dictionary<int, char>>(StringComparer.Ordinal);

	private DateTime lastPointsFetch = DateTime.MinValue;

	private bool pointsFetching;

	private double pointsUpdatedAt;

	private string pointsStatus = "排点未同步";

	private bool pointsProbeLogged;

	private static readonly Regex SonarKillRx = new Regex("^\\s*(?:(?:\\(|\\[|（)\\s*Sonar\\s*(?:\\)|\\]|）)\\s*|Sonar\\s*:\\s*)?Rank\\s+S\\s*:\\s*(?<head>[^<>()]+?)\\s*\\(\\s*(?<x>\\d{1,2}(?:\\.\\d+)?)\\s*,\\s*(?<y>\\d{1,2}(?:\\.\\d+)?)\\s*\\)\\s*<\\s*(?<world>[^<>]+?)\\s*>\\s*was\\s+just\\s+killed\\s*\\.?\\s*\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex SonarSpawnRx = new Regex("^\\s*(?:(?:\\(|\\[|（)\\s*Sonar\\s*(?:\\)|\\]|）)\\s*|Sonar\\s*:\\s*)?Rank\\s+S\\s*:\\s*(?<head>[^<>()]+?)\\s*\\(\\s*(?<x>\\d{1,2}(?:\\.\\d+)?)\\s*,\\s*(?<y>\\d{1,2}(?:\\.\\d+)?)\\s*\\)\\s*<\\s*(?<world>[^<>]+?)\\s*>\\s*\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex SonarResetRx = new Regex("^\\s*(?:(?:\\(|\\[|（)\\s*Sonar\\s*(?:\\)|\\]|）)\\s*|Sonar\\s*:\\s*)?Rank\\s+S\\s*:\\s*(?<head>[^<>()]+?)\\s*\\(\\s*(?<x>\\d{1,2}(?:\\.\\d+)?)\\s*,\\s*(?<y>\\d{1,2}(?:\\.\\d+)?)\\s*\\)\\s*<\\s*(?<world>[^<>]+?)\\s*>\\s*(?:was\\s+just\\s+)?(?:reset|despawned|evaded|disengaged|leashed|脱战|重置)\\s*[.!。]?\\s*\\z", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private const string SonarCacheFile = "sonar_kills.json";

	private const double SonarDedupeSeconds = 30.0;

	private readonly Dictionary<string, double> sonarKills = new Dictionary<string, double>(StringComparer.Ordinal);

	private readonly Dictionary<string, double> sonarSeen = new Dictionary<string, double>(StringComparer.Ordinal);

	private readonly Dictionary<string, double> sonarSpawnSeen = new Dictionary<string, double>(StringComparer.Ordinal);

	private readonly Dictionary<string, double> sonarResetSeen = new Dictionary<string, double>(StringComparer.Ordinal);

	private readonly List<SonarKillRecord> sonarRecent = new List<SonarKillRecord>();

	private bool sonarKillsLoaded;

	private bool sonarProbeOk;

	private double sonarProbeAt;

	private int sonarUnmatched;

	private string sonarUnmatchedSample = "";

	private const string TriggeredCacheFile = "srank_triggered.json";

	private const string RelayApi = "http://119.29.119.179/srank/bot_relay.json";

	private MainTriggerMode mainTriggerMode;

	private const double RelayMaxAgeSeconds = 72000.0;

	private const double RelayKillTolerance = 30.0;

	private static readonly TimeSpan RelayInterval = TimeSpan.FromMinutes(3L);

	private static readonly TimeSpan RelayStaleAfter = TimeSpan.FromMinutes(20L);

	private static readonly TimeSpan TriggeredTimeout = TimeSpan.FromMinutes(10L);

	private static readonly TimeSpan TriggeredSweepInterval = TimeSpan.FromSeconds(30L);

	private DateTime lastTriggeredSweepUtc = DateTime.MinValue;

	private const string SourceRelay = "relay";

	private const string SourceSonar = "sonar";

	private Dictionary<string, Dictionary<string, double[]>> relayLive = new Dictionary<string, Dictionary<string, double[]>>(StringComparer.Ordinal);

	private double relayGenerated;

	private DateTime lastRelayFetch = DateTime.MinValue;

	private DateTime lastRelayOk = DateTime.MinValue;

	private bool relayFetching;

	private string relayStatus = "看板未同步";

	private readonly Dictionary<string, double> triggeredMonsters = new Dictionary<string, double>(StringComparer.Ordinal);

	private readonly Dictionary<string, double> triggeredFloor = new Dictionary<string, double>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> triggeredSource = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly Dictionary<string, double> triggeredSeen = new Dictionary<string, double>(StringComparer.Ordinal);

	private readonly Dictionary<string, double> killedWatermark = new Dictionary<string, double>(StringComparer.Ordinal);

	private bool triggeredLoaded;

	private string AutoSettingsFile => Path.Combine(pluginDirectory, "auto_settings.json");

	private string ThemeFile => Path.Combine(pluginDirectory, "theme.txt");

	private string BearCachePath => Path.Combine(pluginDirectory, "srank_live.json");

	private string SonarCachePath => Path.Combine(pluginDirectory, "sonar_kills.json");

	private string TriggeredCachePath => Path.Combine(pluginDirectory, "srank_triggered.json");

	private bool RelayStale
	{
		get
		{
			if (!(lastRelayOk == DateTime.MinValue))
			{
				return DateTime.UtcNow - lastRelayOk > RelayStaleAfter;
			}
			return true;
		}
	}

	private static bool AutoMapOk(HuntData x)
	{
		return AutoMaps.Contains(x.map);
	}

	private (string State, double Eta, double Pct) CalcState(HuntData h, double lastDeath, long now)
	{
		if (lastDeath <= 0.0)
		{
			return (State: "unknown", Eta: 0.0, Pct: 0.0);
		}
		double lastDeath2 = Math.Max(lastDeath, restartAt);
		int num;
		double num2;
		if (restartAt > 0.0||h.worlds.Any(w=>w.isMaint&&w.lastDeath==lastDeath))
		{
			num = ((restartAt >= lastDeath||h.worlds.Any(w=>w.isMaint&&w.lastDeath==lastDeath)) ? 1 : 0);
			if (num != 0 && h.mt.Length >= 1)
			{
				num2 = h.mt[0];
				goto IL_0070;
			}
		}
		else
		{
			num = 0;
		}
		num2 = h.cd;
		goto IL_0070;
		IL_0070:
		double cdH = num2;
		double winH = ((num != 0 && h.mt.Length >= 2) ? h.mt[1] : h.win);
		return Calc(lastDeath2, now, cdH, winH);
	}

	private void OnChatMessage(IHandleableChatMessage msg)
	{
		try
		{
			string textValue = msg.Message.TextValue;
			if (string.IsNullOrEmpty(textValue) || textValue.IndexOf("感受到了", StringComparison.Ordinal) < 0 || (textValue.IndexOf("气息", StringComparison.Ordinal) < 0 && textValue.IndexOf("恶名", StringComparison.Ordinal) < 0))
			{
				return;
			}
			autoHit = textValue.Trim();
			if (!autoRunning)
			{
				return;
			}
			int gen = autoGen;
			Sched(() =>
			{
				if (gen == autoGen && autoRunning)
				{
					AutoHandleHit();
				}
			}, TimeSpan.Zero);
		}
		catch
		{
		}
	}

	private void AutoHandleHit()
	{
		if (autoHit.Length == 0 || (DateTime.UtcNow - autoHitHandledAt).TotalSeconds < 3.0)
		{
			return;
		}
		autoHitHandledAt = DateTime.UtcNow;
		AutoJob autoJob = this.autoJob;
		string text = (((object)autoJob == null) ? autoCurrent : (autoJob.World + " · " + autoJob.HuntCn));
		if ((object)autoJob != null && !autoHits.Contains((autoJob.World, autoJob.HuntCn)))
		{
			autoHits.Add((autoJob.World, autoJob.HuntCn));
		}
		Print("✅ [自动前往] 检测到「" + autoHit + "」");
		Print("✅ " + text + " 触发成功！");
		if ((object)autoJob != null)
		{
			int num = 0;
			for (int num2 = autoQueue.Count - 1; num2 >= autoIdx; num2--)
			{
				if (!(autoQueue[num2].HuntCn != autoJob.HuntCn))
				{
					autoQueue.RemoveAt(num2);
					num++;
				}
			}
			if (num > 0)
			{
				Print($"[自动前往] 剩下 {num} 个「{autoJob.HuntCn}」的其它服任务跳过（已经触发到了）");
			}
		}
		autoHit = "";
		AutoPause("✅ " + text + " 触发成功");
	}

	private void AutoStart()
	{
		bool flag = autoRunning;
		autoRunning = false;
		autoToken++;
		autoPaused = false;
		autoPauseReason = "";
		CaptureWindowStateBeforeTeleport();
		if (flag)
		{
			try
			{
				StopAll(quiet: true);
			}
			catch
			{
			}
		}
		SnapshotData snapshotData = srankData?.data;
		if (snapshotData == null)
		{
			Print("[自动前往] 数据还没加载好，稍后再试");
			return;
		}
		List<HuntData> list = snapshotData.hunts.Where((HuntData huntData) => autoHunts.Contains(huntData.cn) && AutoMapOk(huntData)).ToList();
		List<string> list2 = snapshotData.worlds.Where((string w) => w.Length > 0).ToList();
		List<string> list3 = (autoAllWorlds ? list2 : list2.Where((string w) => autoWorlds.Contains(w)).ToList());
		if (list.Count == 0 || list3.Count == 0)
		{
			Print("[自动前往] 请至少勾选一个服务器和一只 S");
			return;
		}
		Print("[自动前往] 启用前请确认已开启以下插件：");
		Print("  · vnavmesh-cn（寻路 / flyflag / movetarget）— " + (VnavReady() ? "已检测到 ✓" : "未检测到 ✗ 请先启用"));
		Print("  · DailyRoutines · 跨界传送模块（跨服依赖 /pdr worldtravel）");
		Print("  · GatherBuddyReborn（采集类 S 需要：采集清单里配好矮人棉、数量 999）");
		autoQueue.Clear();
		autoSkipped.Clear();
		long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		List<HuntData> list4 = new List<HuntData>();
		Dictionary<string, HashSet<string>> dictionary = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		foreach (HuntData h in list)
		{
			ConditionForecast.Window? window = (ConditionForecast.HasSpec(h.en) ? ConditionForecast.NextWindow(h.en, now) : ((ConditionForecast.Window?)null));
			if (window.HasValue)
			{
				ConditionForecast.Window valueOrDefault = window.GetValueOrDefault();
				if (!(valueOrDefault.Start <= (double)now) || !((double)now < valueOrDefault.End))
				{
					autoSkipped.Add((h.cn, "条件窗口没到", ConditionForecast.LabelOf(h.en) + "｜" + ConditionForecast.Describe(h.en, now)));
					continue;
				}
			}
			if (autoOnlyOpen)
			{
				HashSet<string> hashSet = new HashSet<string>(list3.Where((string w) => IsOpenAt(h, w, now)), StringComparer.Ordinal);
				if (hashSet.Count == 0)
				{
					autoSkipped.Add((h.cn, "没进闸", ""));
					continue;
				}
				dictionary[h.cn] = hashSet;
			}
			list4.Add(h);
		}
		string curWorld = CurrentWorldName();
		List<string> list5 = list3.OrderBy((string w) => (!string.Equals(w, curWorld, StringComparison.OrdinalIgnoreCase)) ? 1 : 0).ToList();
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.Ordinal);
		foreach (string item in list5)
		{
			foreach (HuntData item2 in list4)
			{
				if (!autoOnlyOpen || dictionary[item2.cn].Contains(item))
				{
					if (IsTriggered(item2.en, item))
					{
						hashSet2.Add(item2.cn);
					}
					else
					{
						autoQueue.Add(new AutoJob(item, item2.cn, item2.en, item2.map));
					}
				}
			}
		}
		foreach (string item3 in hashSet2)
		{
			autoSkipped.Add((item3, "已被触发", "聊天栏/主站已报这只 S，跳过"));
		}
		if (autoQueue.Count == 0)
		{
			Print("[自动前往] 勾选的 S 一个都去不了：");
			PrintSkipReport(detail: true);
			autoStatus = "没有可去的 S（原因见聊天框）";
			return;
		}
		Print($"[自动前往] 出发：{autoQueue.Count} 个任务" + ((autoSkipped.Count > 0) ? $"，另有 {autoSkipped.Count} 只跳过（收工汇总）" : ""));
		List<(string, int)> list6 = (from w in list5
			select (w: w, n: Enumerable.Count(autoQueue, (AutoJob j) => j.World == w)) into x
			where x.n > 0
			select x).ToList();
		if (list6.Count > 0)
		{
			IEnumerable<string> values = list6.Take(6).Select(((string w, int n) x) => $"{x.w}({x.n})");
			Print("[自动前往] 顺序：" + string.Join(" → ", values) + ((list6.Count > 6) ? $" → …共 {list6.Count} 个服" : ""));
		}
		autoIdx = 0;
		autoTotal = autoQueue.Count;
		autoRunning = true;
		autoGen++;
		autoHit = "";
		autoJob = null;
		autoHits.Clear();
		autoHitHandledAt = default;
		autoStatus = "准备出发…";
		AutoStep();
	}

	private void PrintSkipReport(bool detail)
	{
		if (autoSkipped.Count == 0)
		{
			return;
		}
		List<IGrouping<string, (string, string, string)>> list = (from s in autoSkipped
			group s by s.kind).ToList();
		if (!detail)
		{
			IEnumerable<string> values = list.Select((IGrouping<string, (string cn, string kind, string detail)> g) => $"{g.Key} {g.Select(((string cn, string kind, string detail) x) => x.cn).Distinct().Count()}");
			Print($"[自动前往] 没跑成 {DistinctSkipped()} 只：{string.Join(" · ", values)}");
			return;
		}
		Print($"[自动前往] 本次没跑成的 {DistinctSkipped()} 只：");
		foreach (IGrouping<string, (string, string, string)> item in list)
		{
			IEnumerable<string> values2 = item.GroupBy(((string cn, string kind, string detail) s) => s.cn).Select((IGrouping<string, (string cn, string kind, string detail)> n) =>
			{
				string text = n.Select(((string cn, string kind, string detail) x) => x.detail).FirstOrDefault((string x) => x.Length > 0);
				return (text == null || text.Length <= 0) ? n.Key : (n.Key + "（" + text + "）");
			});
			Print("  · " + item.Key + "：" + string.Join("、", values2));
		}
	}

	private int DistinctSkipped()
	{
		return autoSkipped.Select(((string cn, string kind, string detail) s) => s.cn).Distinct().Count();
	}

	private bool IsOpenAt(HuntData h, string world, long now)
	{
		WorldDeath[] worlds = h.worlds;
		foreach (WorldDeath worldDeath in worlds)
		{
			if (string.Equals(worldDeath.world, world, StringComparison.Ordinal) && !(worldDeath.lastDeath <= 0.0))
			{
				return CalcState(h, worldDeath.lastDeath, now).State == "open";
			}
		}
		return false;
	}

	private List<string> OpenWorldsOf(HuntData h, long now)
	{
		List<string> list = new List<string>();
		WorldDeath[] worlds = h.worlds;
		foreach (WorldDeath worldDeath in worlds)
		{
			if (!(worldDeath.lastDeath <= 0.0) && CalcState(h, worldDeath.lastDeath, now).State == "open")
			{
				list.Add(worldDeath.world);
			}
		}
		return list;
	}

	private static string WorldsHint(List<string> ws)
	{
		if (ws.Count == 0)
		{
			return "";
		}
		if (ws.Count <= 2)
		{
			return string.Join("、", ws);
		}
		return $"{ws[0]} +{ws.Count - 1}";
	}

	private void AutoStep()
	{
		if (!autoRunning)
		{
			return;
		}
		if (autoIdx >= autoQueue.Count)
		{
			AutoFinish();
			return;
		}
		while (autoIdx < autoQueue.Count)
		{
			AutoJob autoJob = autoQueue[autoIdx];
			if (!IsTriggered(autoJob.HuntEn, autoJob.World))
			{
				break;
			}
			autoIdx++;
			autoSkipped.Add((autoJob.HuntCn, "已被触发", "出发前发现已被他人触发（" + autoJob.World + "）"));
			Print($"[自动前往] {autoJob.World} · {autoJob.HuntCn} 已触发，跳过（不出门）");
		}
		if (autoIdx >= autoQueue.Count)
		{
			AutoFinish();
			return;
		}
		AutoJob job = autoQueue[autoIdx++];
		this.autoJob = job;
		autoHit = "";
		autoCurrent = job.World + " · " + job.HuntCn;
		autoStatus = $"前往 {autoCurrent}（{autoIdx}/{autoTotal}）";
		autoDist = -1f;
		Print($"[自动前往] ({autoIdx}/{autoTotal}) {job.World} · {job.HuntCn}（{job.Map}）");
		autoToken++;
		gounituDone = null;
		gounituFinishedAt = default;
		gounituFlowOk = false;
		gounituStartedAt = default;
		autoWaitBeganAt = default;
		aetheryteBombLimit = 0;
		autoGatherDoneAt = default;
		autoRouteDoneAt = default;
		autoRouteStartedAt = default;
		autoRetriedRoute = false;
		autoPrepFailed = false;
		autoJobGreens = new List<Pt>();
		autoDiscardDoneAt = default;
		autoLastLogAt = default;
		autoTravelAt = default;
		autoTravelRetry = 0;
		try
		{
			StopAll(quiet: true);
		}
		catch
		{
		}
		try
		{
			GatherBuddySetAuto(on: false);
		}
		catch
		{
		}
		bool flag = PointHunts.Contains(job.HuntCn) && !NoPointHunts.Contains(job.HuntCn);
		List<Pt> greens = new List<Pt>();
		if (job.HuntCn == "顾尼图")
		{
			greens.Add(GounituPt);
		}
		else if (flag)
		{
			List<Pt> list = LiveGreens(job.HuntEn, job.World);
			List<Pt> source = HuntPoints(srankData?.points.GetValueOrDefault(job.HuntEn), job.World);
			List<Pt> list2 = source.Where((Pt q) => q.S == 'g').ToList();
			greens = ((list != null && list.Count > 0) ? list : list2);
			if (greens.Count == 0)
			{
				greens = source.Where((Pt q) => q.S != 'b').Take(1).ToList();
			}
		}
		autoJobGreens = greens;
		string text = AutoTriggerHint(job.HuntCn, job.Map);
		if (text.Length > 0)
		{
			Print("[自动前往] " + job.HuntCn + " 的触发方式：" + text);
		}
		Plugin plugin = this;
		string text2 = $"[自动前往] {job.HuntCn} 取到 {greens.Count} 个触发点";
		string text3;
		if (greens.Count != 0)
		{
			text3 = ((greens.Count > 1) ? "（会一个一个接着试，直到刷出来或全部试完）" : "");
		}
		else
		{
			text3 = "（没有点位，只传大水晶，不会做点位动作）";
		}
		plugin.Print(text2 + text3);
		if (job.HuntCn == "牛头黑神")
		{
			aetheryteBombLimit = 4;
		}
		autoHasRoute = greens.Count > 0;
		if (AutoInCombat())
		{
			Resume();
			Print("[自动前往] 还在战斗状态，先等脱战再出发（最多 20 秒）");
			WaitUntilOr(() => !AutoInCombat(), () =>
			{
				AutoDepart(job, greens);
			}, () =>
			{
				AutoDepart(job, greens);
			}, 40);
		}
		else
		{
			AutoDepart(job, greens);
		}
	}

	private void AutoDepart(AutoJob job, List<Pt> greens)
	{
		try
		{
			if (greens.Count > 0)
			{
				Pt pt = greens[0];
				GotoResult gotoResult = Goto(job.Map, pt.X, pt.Y, job.World, HuntLineOverride(job.HuntEn, job.World), job.HuntCn, nav: true, greens);
				if (!gotoResult.Ok)
				{
					AbortTravel(gotoResult.Message);
					return;
				}
			}
			else
			{
				GotoResult gotoResult2 = GotoAetheryte(job.Map, job.World, job.HuntCn);
				if (!gotoResult2.Ok)
				{
					AbortTravel(gotoResult2.Message);
					return;
				}
			}
		}
		catch (Exception ex)
		{
			AbortTravel("出发出错：" + ex.Message);
			return;
		}
		autoJobStart = DateTime.UtcNow;
		autoTravelAt = autoJobStart;
		autoTravelRetry = 0;
		autoLastLogAt = autoJobStart;
		AutoMonitor(job, greens);
	}

	private bool AutoInCombat()
	{
		try
		{
			return condition[ConditionFlag.InCombat];
		}
		catch
		{
			return false;
		}
	}

	private void AutoMonitor(AutoJob job, List<Pt> greens)
	{
		int token = autoToken;
		Sched(() =>
		{
			if (token == autoToken && autoRunning && autoHit.Length <= 0 && !AutoPrepFailedStep(job))
			{
				AutoMaybeRetryRoute(job, greens);
				DateTime utcNow = DateTime.UtcNow;
				double totalSeconds = (utcNow - autoJobStart).TotalSeconds;
				bool flag;
				bool flag2;
				try
				{
					flag = WorldReady(job.World);
					flag2 = InMap(job.Map);
				}
				catch
				{
					flag = true;
					flag2 = false;
				}
				try
				{
					if (!travelPreparationPending && flag && flag2 && !IsZoning() && greens.Count > 0 && TryResolveZone(job.Map, out uint _, out uint mapId, out string _))
					{
						float num = YalmsTo(mapId, greens[0].X, greens[0].Y);
						autoDist = num;
						if (num <= 40f)
						{
							AutoBeginWait(job, token);
							return;
						}
					}
					else if (!travelPreparationPending && !IsZoning() && greens.Count == 0 && flag && flag2)
					{
						autoDist = 0f;
						AutoBeginWait(job, token);
						return;
					}
				}
				catch
				{
				}
				if (!flag || !flag2)
				{
					string text = ((!flag) ? "跨服" : "传送");
					double num2 = ((!flag) ? 90.0 : 90.0);
					double totalSeconds2 = (utcNow - autoTravelAt).TotalSeconds;
					if (totalSeconds2 >= num2)
					{
						Print($"[自动前往] {job.World} · {job.HuntCn} {text} {num2:F0} 秒没成功，跳过这只" + ((text == "跨服") ? "（请确认 DailyRoutines 的「跨界传送指令」模块已启用）" : ""));
						autoSkipped.Add((job.HuntCn, text + "失败", $"{text} {num2:F0} 秒没成功（{job.World}）"));
						AutoStep();
						return;
					}
					if (totalSeconds2 >= 20.0 * (double)(autoTravelRetry + 1) && autoTravelRetry < 2)
					{
						autoTravelRetry++;
						autoTravelAt = utcNow;
						Print($"[自动前往] {text}没动静（已经 {totalSeconds2:F0} 秒），重发一次（第 {autoTravelRetry} 次）");
						if (!flag)
						{
							SendWorldTravel(job.World);
						}
						else
						{
							AutoRetryTeleport(job, greens);
						}
					}
					autoStatus = ((!flag) ? $"{job.World} · {job.HuntCn} 跨服中（→ {job.World}，{totalSeconds2:F0}s）" : $"{job.World} · {job.HuntCn} 传送中（{job.Map}，{totalSeconds2:F0}s）");
				}
				else
				{
					autoStatus = ((autoDist > 0f) ? $"{job.World} · {job.HuntCn} 飞行中（还差 {autoDist:F0} 亚尔）" : (job.World + " · " + job.HuntCn + " 在图上，定位中…"));
				}
				double num3 = ((!flag) ? 90.0 : 300.0);
				if (totalSeconds >= num3)
				{
					Print($"[自动前往] {job.World} · {job.HuntCn} {num3:F0} 秒没到点，跳过");
					autoSkipped.Add((job.HuntCn, "没飞到点", $"{num3:F0} 秒没到（{job.World}）"));
					AutoStep();
				}
				else
				{
					if ((utcNow - autoLastLogAt).TotalSeconds >= 30.0)
					{
						autoLastLogAt = utcNow;
						Print($"[自动前往] {autoStatus}（{autoIdx}/{autoTotal}）");
					}
					AutoMonitor(job, greens);
				}
			}
		}, TimeSpan.FromSeconds(3L));
	}

	private bool AutoPrepFailedStep(AutoJob job)
	{
		if (!autoPrepFailed)
		{
			return false;
		}
		autoPrepFailed = false;
		Print($"[自动前往] {job.World} · {job.HuntCn} 切换职业失败，判定触发失败，去下一只");
		autoSkipped.Add((job.HuntCn, "换装失败", "切职业没成功（" + job.World + "）"));
		AutoStep();
		return true;
	}

	private void AutoMaybeRetryRoute(AutoJob job, List<Pt> greens)
	{
		if (travelPreparationPending || autoRetriedRoute || autoRouteStartedAt != default(DateTime) || !QuickActionHunts.Contains(job.HuntCn) || greens.Count == 0 || autoJobStart == default(DateTime) || (DateTime.UtcNow - autoJobStart).TotalSeconds < 30.0)
		{
			return;
		}
		try
		{
			if (!AtTravelDestination(job.World, job.Map))
			{
				return;
			}
		}
		catch
		{
			return;
		}
		autoRetriedRoute = true;
		Print($"[自动前往] {job.World} · {job.HuntCn} 出发 30 秒还没开始巡点/自爆，" + "重新排一次点位动作（多半是换装或上坐骑那一步被卡住）");
		Resume();
		StartGreenRoute(job.Map, greens, job.HuntCn);
	}

	private void AutoRetryTeleport(AutoJob job, List<Pt> greens)
	{
		try
		{
			float x = ((greens.Count > 0) ? greens[0].X : 0f);
			float y = ((greens.Count > 0) ? greens[0].Y : 0f);
			AetheryteHit? aetheryteHit = NearestAetheryte(job.Map, x, y, job.HuntCn);
			if (aetheryteHit.HasValue)
			{
				TryNativeTeleport(aetheryteHit.GetValueOrDefault(), out _);
			}
		}
		catch
		{
		}
	}

	private void AutoBeginWait(AutoJob job, int token)
	{
		if (token != autoToken)
		{
			return;
		}
		autoWaitBeganAt = DateTime.UtcNow;
		bool flag = DiscardMaps.ContainsKey(job.Map);
		if (flag)
		{
			Print("[自动前往] " + job.HuntCn + " 的触发要「丢道具」，落地 3 秒后自动丢");
		}
		autoStatus = (flag ? (job.World + " · " + job.HuntCn + " 已到图，丢弃中…") : (job.World + " · " + job.HuntCn + " 已到点，等触发…"));
		if (flag)
		{
			Sched(() =>
			{
				AutoDiscard(job, token);
			}, TimeSpan.FromSeconds(3L));
		}
		AutoWaitTrigger(job, AutoWaitSeconds(job.HuntCn), token);
	}

	private bool AutoGounituTick(AutoJob job, int left)
	{
		if (gounituFinishedAt != default(DateTime))
		{
			double totalSeconds = (DateTime.UtcNow - gounituFinishedAt).TotalSeconds;
			if (totalSeconds < 20.0)
			{
				autoStatus = $"{job.World} · {job.HuntCn} 流程已走完，等刷新提示（{20 - (int)totalSeconds}s）";
				return false;
			}
			Print($"[自动前往] {job.World} · {job.HuntCn} 触发流程走完，{totalSeconds:F0} 秒没出刷新提示，下一只");
			autoSkipped.Add((job.HuntCn, "没触发出来", (gounituFlowOk ? "判到受击但没出怪" : "流程超时") + "（" + job.World + "）"));
			AutoStep();
			return true;
		}
		if (gounituStartedAt == default(DateTime) && autoWaitBeganAt != default(DateTime) && (DateTime.UtcNow - autoWaitBeganAt).TotalSeconds >= 60.0)
		{
			Print($"[自动前往] {job.World} · {job.HuntCn} 已到点 60 秒，触发流程还没起来" + "（多半卡在过图 / 下坐骑 / 没走到固定点），下一只");
			autoSkipped.Add((job.HuntCn, "没触发出来", "到点 60 秒流程未启动（" + job.World + "）"));
			AutoStep();
			return true;
		}
		autoStatus = ((gounituStartedAt == default(DateTime)) ? (job.World + " · " + job.HuntCn + " 已到点，等落地开始触发…") : $"{job.World} · {job.HuntCn} 触发流程进行中…（兜底 {left}s）");
		return false;
	}

	private int AutoWaitSeconds(string huntCn)
	{
		int num;
		switch (huntCn)
		{
		case "顾尼图":
			num = 300;
			break;
		case "多智兽":
		case "阿姆斯特朗":
		case "牛头黑神":
			num = 480;
			break;
		case "先驱勇士 阿提卡斯":
			num = 200;
			break;
		case "天气预报机器人":
			num = 90;
			break;
		case "得到宽恕的炫学":
			num = 1900;
			break;
		default:
			num = 100;
			break;
		}
		int num2 = num;
		if (huntCn == "多智兽" || huntCn == "阿姆斯特朗")
		{
			num2 += Math.Max(0, autoJobGreens.Count - 1) * 90;
		}
		return num2;
	}

	private static string AutoTriggerHint(string huntCn, string map)
	{
		switch (huntCn)
		{
		case "顾尼图":
			return "飞到固定点 (29.8, 3.9) → 诱钓 3 只深海水蛭 → 挑衅冰海天使 → 等巨大化/最大化/口锥";
		case "多智兽":
		case "阿姆斯特朗":
			return "到绿点下坐骑 → 即刻咏唱 + 自爆（阿姆斯特朗要先穿帝国套）";
		case "牛头黑神":
			return $"传大水晶后自爆，最多 {4} 次";
		case "天气预报机器人":
			return "到绿点下坐骑 → 放狂风暴雪";
		case "先驱勇士 阿提卡斯":
			return "落地切烹调师 → Artisan 做 HQ 犎牛牛排";
		case "得到宽恕的炫学":
			return "落地切园艺工 → 调用 GatherBuddy Reborn 自动采矮人棉（最多 300 次 / 触发即停；清单需配矮人棉×999）";
		default:
			return DiscardMaps.ContainsKey(map) ? "到图自动丢一组触发道具" : "";
		}
	}

	private static string AutoTriggerShort(string huntCn, string map)
	{
		switch (huntCn)
		{
		case "顾尼图":
			return "自动诱钓挑衅";
		case "多智兽":
		case "阿姆斯特朗":
			return "自动自爆";
		case "牛头黑神":
			return $"自爆×{4}";
		case "天气预报机器人":
			return "自动狂风暴雪";
		case "先驱勇士 阿提卡斯":
			return "自动做 HQ 牛排";
		case "得到宽恕的炫学":
			return "GBR 自动采矮人棉";
		default:
			return DiscardMaps.ContainsKey(map) ? "自动丢道具" : "";
		}
	}

	private void AutoDiscard(AutoJob job, int token, int tries = 0)
	{
		if (token != autoToken || !autoRunning || autoHit.Length > 0)
		{
			return;
		}
		if (!InMap(job.Map))
		{
			if (tries < 2)
			{
				Sched(() =>
				{
					AutoDiscard(job, token, tries + 1);
				}, TimeSpan.FromSeconds(4L));
			}
		}
		else if (DiscardTrigger().Ok)
		{
			autoDiscardDoneAt = DateTime.UtcNow;
		}
		else if (tries < 2)
		{
			Sched(() =>
			{
				AutoDiscard(job, token, tries + 1);
			}, TimeSpan.FromSeconds(5L));
		}
	}

	private bool AutoActionsDone(out double since, out string what)
	{
		since = 0.0;
		what = "";
		bool num = autoDiscardDoneAt != default(DateTime) && autoDiscardDoneAt >= autoJobStart;
		bool flag = autoGatherDoneAt != default(DateTime) && autoGatherDoneAt >= autoJobStart;
		DateTime dateTime = default;
		if (num)
		{
			dateTime = autoDiscardDoneAt;
			what = "丢弃完成";
		}
		if (flag && (dateTime == default(DateTime) || autoGatherDoneAt > dateTime))
		{
			dateTime = autoGatherDoneAt;
			what = "采集完成";
		}
		if (dateTime == default(DateTime))
		{
			if (autoRouteDoneAt == default(DateTime) || autoRouteDoneAt < autoJobStart)
			{
				return false;
			}
			dateTime = autoRouteDoneAt;
			what = (autoHasRoute ? "绿点走完" : "触发动作做完");
		}
		since = (DateTime.UtcNow - dateTime).TotalSeconds;
		return true;
	}

	private void AutoWaitTrigger(AutoJob job, int left, int token)
	{
		Sched(() =>
		{
			if (token == autoToken && autoRunning && autoHit.Length <= 0 && !AutoPrepFailedStep(job))
			{
				AutoMaybeRetryRoute(job, autoJobGreens);
				int value = AutoWaitSeconds(job.HuntCn);
				if (!(job.HuntCn == "顾尼图") || !AutoGounituTick(job, left))
				{
					if (job.HuntCn != "顾尼图" && AutoActionsDone(out double since, out string what))
					{
						if (since >= 5.0)
						{
							Print($"[自动前往] {job.World} · {job.HuntCn} {what}后 {5} 秒没出刷新提示，判定没触发，下一只");
							autoSkipped.Add((job.HuntCn, "没触发出来", $"{what}后 {5} 秒没出提示（{job.World}）"));
							AutoStep();
							return;
						}
						autoStatus = $"{job.World} · {job.HuntCn} {what}，等提示（{5 - (int)since}s）";
					}
					else
					{
						autoStatus = $"{job.World} · {job.HuntCn} 已到点，触发动作进行中…（{left}s）";
					}
					if (left <= 0)
					{
						Print($"[自动前往] {job.World} · {job.HuntCn} 已到点，等了 {value} 秒没出刷新提示，下一只");
						autoSkipped.Add((job.HuntCn, "没触发出来", $"到点等了 {value} 秒（{job.World}）"));
						AutoStep();
					}
					else
					{
						if (left == 60 && DiscardMaps.ContainsKey(job.Map))
						{
							Sched(() =>
							{
								AutoDiscard(job, token);
							}, TimeSpan.Zero);
						}
						AutoWaitTrigger(job, left - 1, token);
					}
				}
			}
		}, TimeSpan.FromSeconds(1L));
	}

	private void AutoPause(string reason)
	{
		if (!autoPaused)
		{
			autoRunning = false;
			autoPaused = true;
			autoGen++;
			autoToken++;
			try
			{
				StopAll(quiet: true);
			}
			catch
			{
			}
		}
		autoPauseReason = reason;
		autoStatus = "⏸ " + reason + " · 队列已暂停";
		Print($"[自动前往] {reason}（队列已暂停，还剩 {Math.Max(0, autoQueue.Count - autoIdx)} 个任务；" + "点悬浮窗「继续队列」接着跑）");
	}

	private void AutoResume()
	{
		if (!autoRunning && autoQueue.Count > autoIdx)
		{
			autoPaused = false;
			autoPauseReason = "";
			autoRunning = true;
			autoGen++;
			autoToken++;
			autoHit = "";
			autoJob = null;
			autoStatus = "继续队列…";
			Print($"[自动前往] 继续队列（还剩 {Math.Max(0, autoQueue.Count - autoIdx)} 个任务）");
			AutoStep();
		}
	}

	private void TickTriggeredGuard()
	{
		if (!autoRunning)
		{
			return;
		}
		AutoJob autoJob = this.autoJob;
		if ((object)autoJob == null)
		{
			return;
		}
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		if (num <= autoTriggerCheckAt)
		{
			return;
		}
		autoTriggerCheckAt = num;
		if (IsTriggered(autoJob.HuntEn, autoJob.World))
		{
			autoTriggerCheckAt = 0L;
			autoGen++;
			autoToken++;
			try
			{
				StopAll(quiet: true);
			}
			catch
			{
			}
			autoSkipped.Add((autoJob.HuntCn, "已被触发", "途中监听到该 S 已被他人触发（" + autoJob.World + "）"));
			Print($"[自动前往] {autoJob.World} · {autoJob.HuntCn} 已触发，跳过（取消寻路，去下一个）");
			autoHit = "";
			this.autoJob = null;
			autoStatus = "跳过 " + autoJob.HuntCn + "（已被触发），去下一个…";
			AutoStep();
		}
	}

	private void AutoSkipCurrent()
	{
		AutoJob autoJob = this.autoJob;
		if ((object)autoJob == null)
		{
			if (autoPaused)
			{
				AutoResume();
			}
			return;
		}
		autoGen++;
		autoToken++;
		try
		{
			StopAll(quiet: true);
		}
		catch
		{
		}
		autoSkipped.Add((autoJob.HuntCn, "手动跳过", "在悬浮窗点了「停止这只，去下一个」（" + autoJob.World + "）"));
		Print($"[自动前往] 停止 {autoJob.World} · {autoJob.HuntCn} 的触发，去下一个 S");
		autoHit = "";
		autoPaused = false;
		autoPauseReason = "";
		autoRunning = true;
		this.autoJob = null;
		autoStatus = "已跳过 " + autoJob.HuntCn + "，去下一个…";
		AutoStep();
	}

	private void AutoStop()
	{
		autoRunning = false;
		autoPaused = false;
		autoPauseReason = "";
		autoGen++;
		autoToken++;
		autoJob = null;
		autoHit = "";
		try
		{
			StopAll(quiet: true);
		}
		catch
		{
		}
		autoStatus = "已停止";
		Print("[自动前往] 已停止");
		PrintHitReport();
		PrintSkipReport(detail: false);
	}

	private void PrintHitReport()
	{
		if (autoHits.Count != 0)
		{
			Print($"[自动前往] ✅ 已触发 {autoHits.Count} 只：" + string.Join("、", autoHits.Select(((string world, string cn) h) => h.world + " · " + h.cn)));
		}
	}

	private void AutoFinish()
	{
		autoRunning = false;
		autoPaused = false;
		autoPauseReason = "";
		autoGen++;
		autoToken++;
		autoJob = null;
		int num = DistinctSkipped();
		ref string reference = ref autoStatus;
		string text;
		if (autoHits.Count > 0)
		{
			text = $"✅ 触发 {autoHits.Count} 只" + ((num > 0) ? $"，{num} 只没跑成" : "，全部跑完");
		}
		else
		{
			text = ((num > 0) ? $"全部走完（{autoTotal} 个任务，{num} 只没跑成）" : $"全部走完（{autoTotal} 个），没等到刷新提示");
		}
		reference = text;
		Print($"[自动前往] 全部走完：共 {autoTotal} 个任务");
		PrintHitReport();
		if (autoSkipped.Count > 0)
		{
			Print("");
			PrintSkipReport(detail: true);
			Print("[自动前往] 上面这些可以稍后重跑；条件窗口类的等窗口到了再来");
		}
	}

	private HudActions BuildHudActions()
	{
		return new HudActions
		{
			Primary = () =>
			{
				int num = autoQueue.Count - autoIdx;
				if (autoRunning)
				{
					AutoPause("手动暂停");
				}
				else if (num > 0)
				{
					AutoResume();
				}
				else
				{
					AutoStart();
				}
			},
			Skip = () =>
			{
				AutoSkipCurrent();
			},
			Stop = () =>
			{
				AutoStop();
			},
			Restart = () =>
			{
				AutoStart();
			},
			Closed = () =>
			{
				autoHudOpen = false;
			}
		};
	}

	private HudSnapshot BuildHudSnapshot()
	{
		HudSnapshot hudSnapshot = new HudSnapshot();
		string text;
		if (autoPaused)
		{
			text = "已暂停";
		}
		else
		{
			text = (autoRunning ? "触发中" : "空闲");
		}
		hudSnapshot.Badge = "● " + text;
		hudSnapshot.Progress = $"第 {Math.Min(autoIdx, autoTotal)}/{autoTotal} · 已触发 {autoHits.Count} 只";
		AutoJob autoJob = this.autoJob;
		if ((object)autoJob != null)
		{
			hudSnapshot.Job = autoJob.World + " · " + autoJob.HuntCn;
			string text2 = AutoTriggerShort(autoJob.HuntCn, autoJob.Map);
			hudSnapshot.Map = "「" + autoJob.Map + "」" + ((text2.Length > 0) ? (" · " + text2) : "");
			if (autoIdx < autoQueue.Count)
			{
				AutoJob autoJob2 = autoQueue[autoIdx];
				hudSnapshot.Next = $"下一个：{autoJob2.World} · {autoJob2.HuntCn}（还剩 {autoQueue.Count - autoIdx} 个）";
			}
		}
		else
		{
			hudSnapshot.Job = ((autoCurrent.Length > 0) ? autoCurrent : "还没开始触发");
			hudSnapshot.Map = "勾选服务器和 S 后点「开始自动前往」";
		}
		string text3 = ((autoPaused && autoPauseReason.Length > 0) ? (autoPauseReason + "\n队列已暂停，点「继续队列」接着跑") : autoStatus);
		if (text3.Length > 30 && !text3.Contains('\n'))
		{
			text3 = text3.Substring(0, 30) + "\n" + text3.Substring(30);
		}
		hudSnapshot.Status = text3;
		int num = autoQueue.Count - autoIdx;
		ref string primary = ref hudSnapshot.Primary;
		string text4;
		if (autoRunning)
		{
			text4 = "暂停";
		}
		else
		{
			text4 = ((num > 0) ? "继续队列" : "重新开始");
		}
		primary = text4;
		return hudSnapshot;
	}

	private AutoWindowActions BuildAutoWindowActions()
	{
		return new AutoWindowActions
		{
			GroupStyle = () =>
			{
				autoStyle = 1;
			},
			CardStyle = () =>
			{
				autoStyle = 2;
			},
			ToggleOnlyOpen = () =>
			{
				autoOnlyOpen = !autoOnlyOpen;
			},
			ToggleAllWorlds = () =>
			{
				autoAllWorlds = !autoAllWorlds;
			},
			ToggleHud = () =>
			{
				autoHudOpen = !autoHudOpen;
			},
			SelectAllWorlds = () =>
			{
				SnapshotData snapshotData = srankData?.data;
				if (snapshotData != null)
				{
					string[] worlds = snapshotData.worlds;
					foreach (string text in worlds)
					{
						if (text.Length > 0)
						{
							autoWorlds.Add(text);
						}
					}
				}
			},
			ClearWorlds = () =>
			{
				autoWorlds.Clear();
			},
			ToggleWorld = (string w) =>
			{
				if (!autoWorlds.Remove(w))
				{
					autoWorlds.Add(w);
				}
			},
			ToggleHunt = (string k) =>
			{
				if (!autoHunts.Remove(k))
				{
					autoHunts.Add(k);
				}
			},
			ClearHit = () =>
			{
				autoHit = "";
				autoHits.Clear();
			},
			PrevPage = () =>
			{
				if (autoNativePage > 0)
				{
					autoNativePage--;
				}
			},
			NextPage = () =>
			{
				autoNativePage++;
			},
			StartStop = () =>
			{
				bool flag = !autoRunning && !autoPaused && autoQueue.Count > autoIdx;
				if (autoPaused | flag)
				{
					AutoResume();
				}
				else if (autoRunning)
				{
					AutoStop();
				}
				else
				{
					AutoStart();
				}
			},
			Closed = () =>
			{
				autoRunOpen = false;
			}
		};
	}

	private AutoWindowSnapshot BuildAutoWindowSnapshot()
	{
		AutoWindowSnapshot autoWindowSnapshot = new AutoWindowSnapshot();
		SnapshotData snapshotData = srankData?.data;
		autoWindowSnapshot.Ready = snapshotData != null;
		autoWindowSnapshot.StyleGroup = autoStyle == 1;
		autoWindowSnapshot.StyleCard = autoStyle == 2;
		autoWindowSnapshot.OnlyOpen = autoOnlyOpen;
		autoWindowSnapshot.AllWorlds = autoAllWorlds;
		autoWindowSnapshot.HudOpen = autoHudOpen;
		if (autoHit.Length > 0 || autoHits.Count > 0)
		{
			autoWindowSnapshot.ShowHitBar = true;
			autoWindowSnapshot.Hit1 = ((autoHit.Length > 0) ? "✅ 触发成功 · 队列已暂停" : $"✅ 已触发 {autoHits.Count} 只 · 队列已暂停（悬浮窗里继续）");
			autoWindowSnapshot.Hit2 = ((autoHit.Length > 0) ? ("「" + autoHit + "」") : string.Join("、", from h in autoHits.Skip(Math.Max(0, autoHits.Count - 3))
				select h.cn));
		}
		List<string> list = ((snapshotData != null) ? snapshotData.worlds.Where((string w) => w.Length > 0).ToList() : new List<string>());
		autoWindowSnapshot.WorldCaption = (autoAllWorlds ? $"共 {list.Count} 服 · 每只 S 只去进闸的服（只看开闸：{(autoOnlyOpen ? "开" : "关")}）" : $"服务器（已选 {autoWorlds.Count}/{list.Count}）· 点服名勾选");
		foreach (string item in list.Take(16))
		{
			autoWindowSnapshot.Worlds.Add(item);
			autoWindowSnapshot.WorldOn.Add(autoWorlds.Contains(item));
		}
		int count = autoQueue.Count;
		int value = Math.Max(0, count - autoIdx);
		autoWindowSnapshot.QueueTitle = ((count == 0) ? "当前排队 · 还没生成队列（选好服务器和 S 怪后点底部开始）" : $"当前排队 · 共 {count} 个 / 剩 {value} 个");
		for (int num = Math.Max(0, Math.Min(autoIdx - 2, count - 6)); num < count; num++)
		{
			if (autoWindowSnapshot.QueueLines.Count >= 6)
			{
				break;
			}
			AutoJob autoJob = autoQueue[num];
			bool num2 = autoRunning && num == autoIdx - 1;
			bool flag = num < autoIdx - 1;
			string value2;
			if (num2)
			{
				value2 = "▶";
			}
			else
			{
				value2 = (flag ? "✓" : "·");
			}
			autoWindowSnapshot.QueueLines.Add($"{value2} {num + 1,2}. {autoJob.World} · {autoJob.HuntCn}（{autoJob.Map}）");
		}
		if (snapshotData != null)
		{
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			HuntData[] hunts = snapshotData.hunts;
			Dictionary<string, (string state, double eta, double pct)> states = new Dictionary<string, (string, double, double)>(StringComparer.Ordinal);
			HuntData[] array = hunts;
			foreach (HuntData huntData in array)
			{
				states[huntData.cn] = BestStateOf(huntData, now);
			}
			IEnumerable<HuntData> source = hunts.Where((HuntData x) => AutoShown(x, states));
			foreach (HuntData item2 in ((autoStyle == 1) ? source.OrderBy((HuntData x) => x.patch).ThenBy((HuntData x) => x.cn, StringComparer.Ordinal) : source.OrderBy((HuntData x) => x.cn, StringComparer.Ordinal)).ToList())
			{
				(string, double, double) tuple = (states.TryGetValue(item2.cn, out (string, double, double) value3) ? value3 : ("unknown", 0.0, 0.0));
				List<string> list2 = ((autoAllWorlds && tuple.Item1 == "open") ? OpenWorldsOf(item2, now) : null);
				string right;
				if (list2 != null && list2.Count > 0)
				{
					right = WorldsHint(list2);
				}
				else if (tuple.Item1 == "open")
				{
					right = $"开闸 {tuple.Item3:0}%";
				}
				else
				{
					right = ((tuple.Item1 == "cd") ? ("冷却 " + TimeText(tuple.Item2)) : "已过窗");
				}
				autoWindowSnapshot.Hunts.Add(new AutoHuntRow
				{
					Key = item2.cn,
					Label = item2.cn,
					Right = right,
					Checked = autoHunts.Contains(item2.cn)
				});
			}
		}
		autoWindowSnapshot.PageCount = Math.Max(1, (autoWindowSnapshot.Hunts.Count + 16 - 1) / 16);
		if (autoNativePage < 0)
		{
			autoNativePage = 0;
		}
		if (autoNativePage >= autoWindowSnapshot.PageCount)
		{
			autoNativePage = autoWindowSnapshot.PageCount - 1;
		}
		autoWindowSnapshot.Page = autoNativePage;
		autoWindowSnapshot.Hints = (autoRunning ? "队列运行中 · 暂停 / 继续 / 停止在「触发悬浮窗」里操作" : ("环境检测：vnavmesh " + (VnavReady() ? "✓" : "✗") + " | DailyRoutines 跨界传送 | GatherBuddyReborn"));
		bool flag2 = !autoRunning && !autoPaused && count > autoIdx;
		ref string runLabel = ref autoWindowSnapshot.RunLabel;
		string text;
		if (autoPaused | flag2)
		{
			text = "继续队列";
		}
		else
		{
			text = (autoRunning ? "停止" : "开始自动前往");
		}
		runLabel = text;
		if (autoAllWorlds && autoOnlyOpen)
		{
			autoWindowSnapshot.Counts = $"全部 {list.Count} 服 · 已选 {autoHunts.Count} 只（先跑本服）";
		}
		else if (autoAllWorlds)
		{
			autoWindowSnapshot.Counts = $"全部 {list.Count} 服 × {autoHunts.Count} 只 = {list.Count * autoHunts.Count} 个任务";
		}
		else if (autoOnlyOpen)
		{
			autoWindowSnapshot.Counts = $"{autoWorlds.Count} 服 · 已选 {autoHunts.Count} 只（只跑开闸的）";
		}
		else
		{
			autoWindowSnapshot.Counts = $"{autoWorlds.Count} 服 × {autoHunts.Count} 只 = {autoWorlds.Count * autoHunts.Count} 个任务";
		}
		autoWindowSnapshot.Status = ((autoStatus.Length > 64) ? autoStatus.Substring(0, 64) : autoStatus);
		if (autoSkipped.Count > 0)
		{
			autoWindowSnapshot.Skipped = $"没跑成 {DistinctSkipped()} 只：" + string.Join(" / ", from x in autoSkipped
				group x by x.kind into g
				select $"{g.Key} {g.Select(((string cn, string kind, string detail) x) => x.cn).Distinct().Count()}");
		}
		return autoWindowSnapshot;
	}

	private void DrawAutoHudInner()
	{
	}

	private void DrawAutoWindowInner()
	{
	}

	private void PushAutoStyle()
	{
		ImGui.PushStyleColor(ImGuiCol.WindowBg, V1Bg);
		ImGui.PushStyleColor(ImGuiCol.Text, V1Text);
		ImGui.PushStyleColor(ImGuiCol.TextDisabled, V1Dim);
		ImGui.PushStyleColor(ImGuiCol.FrameBg, V1Input);
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, V1Card);
		ImGui.PushStyleColor(ImGuiCol.FrameBgActive, V1BtnAct);
		ImGui.PushStyleColor(ImGuiCol.Button, V1Card);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, V1BtnHi);
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, V1BtnAct);
		ImGui.PushStyleColor(ImGuiCol.Header, V1Sel);
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, V1Hover);
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, V1HoverMid);
		ImGui.PushStyleColor(ImGuiCol.PopupBg, V1PopBg);
		ImGui.PushStyleColor(ImGuiCol.Separator, V1Border);
		ImGui.PushStyleColor(ImGuiCol.Border, V1BorderHi);
		ImGui.PushStyleColor(ImGuiCol.ChildBg, Transparent);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Transparent);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, V1Scroll);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, V1ScrollHi);
		ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, V1ScrollHi);
		ImGui.PushStyleColor(ImGuiCol.TitleBg, V1Bg2);
		ImGui.PushStyleColor(ImGuiCol.TitleBgActive, V1Bg2);
		ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, V1Bg2);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 7f);
		ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9f, 5f));
		ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 6f);
		ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 3f);
	}

	private static void PopAutoStyle()
	{
		ImGui.PopStyleColor(23);
		ImGui.PopStyleVar(9);
	}

	private void AutoChips(List<string> worlds)
	{
		string a = CurrentWorldName();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		float num = ImGui.GetWindowPos().X + ImGui.GetContentRegionMax().X;
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 7f);
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9f, 4f));
		foreach (string world in worlds)
		{
			bool flag = autoWorlds.Contains(world);
			Vector4 vector = WorldColor(world);
			float num2 = TW(world, 12.5f) + 30f;
			if (ImGui.GetItemRectMax().X + 6f + num2 <= num)
			{
				ImGui.SameLine(0f, 6f);
			}
			ImGui.PushStyleColor(ImGuiCol.Button, flag ? new Vector4(vector.X, vector.Y, vector.Z, 0.16f) : V1Card);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(vector.X, vector.Y, vector.Z, 0.26f));
			ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0f, 0f, 0f));
			ImGui.PushStyleColor(ImGuiCol.Border, flag ? new Vector4(vector.X, vector.Y, vector.Z, 0.8f) : V1Border);
			bool flag2 = ImGui.Button("##w" + world, new Vector2(num2, 24f));
			ImGui.PopStyleColor(4);
			Vector2 itemRectMin = ImGui.GetItemRectMin();
			Vector2 itemRectMax = ImGui.GetItemRectMax();
			bool flag3 = string.Equals(a, world, StringComparison.OrdinalIgnoreCase);
			windowDrawList.AddCircle(new Vector2(itemRectMin.X + 11f, (itemRectMin.Y + itemRectMax.Y) / 2f), 4.2f, ImGui.GetColorU32(new Vector4(vector.X, vector.Y, vector.Z, flag ? 1f : 0.45f)), 12);
			if (flag3)
			{
				windowDrawList.AddCircle(new Vector2(itemRectMin.X + 11f, (itemRectMin.Y + itemRectMax.Y) / 2f), 7.4f, ImGui.GetColorU32(new Vector4(vector.X, vector.Y, vector.Z, 0.85f)), 16, 1.4f);
			}
			windowDrawList.AddText(FontFor(12.5f), 12.5f, new Vector2(itemRectMin.X + 21f, itemRectMin.Y + 4f), ImGui.GetColorU32(flag ? vector : V1Dim), world);
			if (flag2)
			{
				if (flag)
				{
					autoWorlds.Remove(world);
				}
				else
				{
					autoWorlds.Add(world);
				}
			}
			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip(world + (flag3 ? "（你在这个服）" : "") + (flag ? "\n点击取消" : "\n点击勾选"));
			}
		}
		ImGui.PopStyleVar(2);
	}

	private void AutoBodyGrouped(float h, Dictionary<string, (string state, double eta, double pct)> states)
	{
		long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		HuntData[] source = srankData?.data?.hunts ?? Array.Empty<HuntData>();
		(string, string)[] array = new (string, string)[6]
		{
			("DT", "7.x 金曦之遗辉"),
			("EW", "6.x 晓月之终途"),
			("ShB", "5.x 暗影之逆焰"),
			("SB", "4.x 红莲之狂潮"),
			("HW", "3.x 苍天之龙骑士"),
			("ARR", "2.x 重生之境")
		};
		ImGui.BeginChild("##body2", new Vector2(0f, h));
		bool flag = false;
		(string, string)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string) tuple = array2[i];
			string key = tuple.Item1;
			string item = tuple.Item2;
			List<HuntData> list = source.Where((HuntData x) => x.patch == key && AutoShown(x, states)).OrderBy((HuntData x) => x.cn, StringComparer.Ordinal).ToList();
			if (list.Count == 0)
			{
				continue;
			}
			flag = true;
			bool flag2 = autoGroupsClosed.Contains(key);
			int value = Enumerable.Count(list, (HuntData x) => autoHunts.Contains(x.cn));
			if (ImGui.CollapsingHeader($"{item}\u3000{value}/{list.Count}##grp{key}", (!flag2) ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
			{
				if (flag2)
				{
					autoGroupsClosed.Remove(key);
				}
				if (ImGui.SmallButton("全选本组##g" + key))
				{
					foreach (HuntData item2 in list)
					{
						autoHunts.Add(item2.cn);
					}
				}
				ImGui.SameLine(0f, 4f);
				if (ImGui.SmallButton("清空本组##gc" + key))
				{
					foreach (HuntData item3 in list)
					{
						autoHunts.Remove(item3.cn);
					}
				}
				foreach (HuntData item4 in list)
				{
					(string, double, double) tuple2 = (states.TryGetValue(item4.cn, out (string, double, double) value2) ? value2 : ("unknown", 0.0, 0.0));
					List<string> list2 = ((autoAllWorlds && tuple2.Item1 == "open") ? OpenWorldsOf(item4, now) : null);
					string right;
					if (list2 != null && list2.Count > 0)
					{
						right = WorldsHint(list2);
					}
					else if (tuple2.Item1 == "open")
					{
						right = $"开闸 {tuple2.Item3:0}%";
					}
					else
					{
						right = ((tuple2.Item1 == "cd") ? ("冷却 " + TimeText(tuple2.Item2)) : "已过窗");
					}
					string text = ((list2 != null && list2.Count > 1) ? ("进闸的服：" + string.Join("、", list2)) : "");
					bool flag3 = autoHunts.Contains(item4.cn);
					string text2 = AutoTriggerShort(item4.cn, item4.map);
					string sub = ((text2.Length > 0) ? (item4.map + " · " + text2) : item4.map);
					string text3 = AutoTriggerHint(item4.cn, item4.map);
					if (text3.Length > 0)
					{
						text = ((text.Length > 0) ? (text3 + "\n" + text) : text3);
					}
					if (AutoCheckRow("g" + item4.en, item4.cn, sub, right, flag3, StateColor(tuple2.Item1), ImGui.GetContentRegionAvail().X, 28f, text))
					{
						if (flag3)
						{
							autoHunts.Remove(item4.cn);
						}
						else
						{
							autoHunts.Add(item4.cn);
						}
					}
				}
			}
			else if (!flag2)
			{
				autoGroupsClosed.Add(key);
			}
		}
		if (!flag)
		{
			AutoEmptyHint();
		}
		ImGui.EndChild();
	}

	private void AutoBodyCards(float h, Dictionary<string, (string state, double eta, double pct)> states)
	{
		long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		List<HuntData> list = (from huntData2 in srankData?.data?.hunts ?? Array.Empty<HuntData>()
			where AutoShown(huntData2, states)
			orderby huntData2.patch
			select huntData2).ThenBy((HuntData huntData2) => huntData2.cn, StringComparer.Ordinal).ToList();
		ImGui.BeginChild("##cards", new Vector2(0f, h));
		float x = ImGui.GetContentRegionAvail().X;
		int num = Math.Max(1, (int)Math.Floor((x + 6f) / 158f));
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			HuntData huntData = list[num2];
			if (num2 % num != 0)
			{
				ImGui.SameLine(0f, 6f);
			}
			bool flag = autoHunts.Contains(huntData.cn);
			(string, double, double) tuple = (states.TryGetValue(huntData.cn, out (string, double, double) value) ? value : ("unknown", 0.0, 0.0));
			Vector4 vector = StateColor(tuple.Item1);
			List<string> list2 = ((autoAllWorlds && tuple.Item1 == "open") ? OpenWorldsOf(huntData, now) : null);
			ImGui.PushID("c" + huntData.en);
			Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
			if (ImGui.InvisibleButton("##card", new Vector2(152f, 38f)))
			{
				if (flag)
				{
					autoHunts.Remove(huntData.cn);
				}
				else
				{
					autoHunts.Add(huntData.cn);
				}
			}
			bool flag2 = ImGui.IsItemHovered();
			Vector2 pMin = cursorScreenPos;
			Vector2 pMax = cursorScreenPos + new Vector2(152f, 38f);
			Vector4 col;
			if (flag)
			{
				col = new Vector4(vector.X, vector.Y, vector.Z, 0.15f);
			}
			else
			{
				col = (flag2 ? V1Hover : V1Card);
			}
			windowDrawList.AddRectFilled(pMin, pMax, ImGui.GetColorU32(col), 7f);
			windowDrawList.AddRect(cursorScreenPos + new Vector2(0.5f, 0.5f), cursorScreenPos + new Vector2(151.5f, 37.5f), ImGui.GetColorU32(flag ? new Vector4(vector.X, vector.Y, vector.Z, 0.7f) : V1Border), 7f);
			windowDrawList.AddRectFilled(cursorScreenPos + new Vector2(0f, 8f), cursorScreenPos + new Vector2(2.5f, 30f), ImGui.GetColorU32(vector), 2f);
			windowDrawList.AddText(FontFor(13f), 13f, new Vector2(cursorScreenPos.X + 11f, cursorScreenPos.Y + 10.5f), ImGui.GetColorU32(flag ? V1Text : V1Dim), huntData.cn);
			if (list2 != null && list2.Count > 0)
			{
				string text = ((list2.Count > 1) ? $"{list2.Count}服" : "");
				if (text.Length > 0)
				{
					float num3 = TW(text, 10.5f);
					windowDrawList.AddText(FontFor(10.5f), 10.5f, new Vector2(cursorScreenPos.X + 152f - 24f - num3, cursorScreenPos.Y + 12.5f), ImGui.GetColorU32(new Vector4(vector.X, vector.Y, vector.Z, 0.9f)), text);
				}
			}
			Vector2 center = new Vector2(cursorScreenPos.X + 152f - 14f, cursorScreenPos.Y + 19f);
			windowDrawList.AddCircle(center, 5.5f, ImGui.GetColorU32(flag ? vector : V1Border), 16, 1.4f);
			if (flag)
			{
				windowDrawList.AddCircleFilled(center, 3f, ImGui.GetColorU32(vector), 12);
			}
			if (flag2)
			{
				string text2 = $"{huntData.cn}\n{huntData.map} · {huntData.patch}\n";
				string? text3;
				if (tuple.Item1 == "open")
				{
					text3 = $"开闸中 {tuple.Item3:0}%";
				}
				else
				{
					text3 = ((tuple.Item1 == "cd") ? ("冷却 " + TimeText(tuple.Item2)) : "窗口已过");
				}
				string text4 = text2 + text3;
				if (list2 != null && list2.Count > 0)
				{
					text4 = text4 + "\n进闸的服：" + string.Join("、", list2);
				}
				string text5 = AutoTriggerHint(huntData.cn, huntData.map);
				if (text5.Length > 0)
				{
					text4 = text4 + "\n自动触发：" + text5;
				}
				ImGui.SetTooltip(text4);
			}
			ImGui.PopID();
		}
		if (list.Count == 0)
		{
			AutoEmptyHint();
		}
		ImGui.EndChild();
	}

	private bool AutoShown(HuntData x, Dictionary<string, (string state, double eta, double pct)> states)
	{
		if (AutoMapOk(x))
		{
			if (autoOnlyOpen)
			{
				if (states.TryGetValue(x.cn, out (string, double, double) value))
				{
					return value.Item1 == "open";
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private void AutoEmptyHint()
	{
		ImGui.Dummy(new Vector2(0f, 18f));
		AutoText("当前没有已开闸的 S", 13.5f, V1Dim);
		AutoText("点上面的「只看开闸 ✓」可以显示全部", 12f, V1Faint);
	}

	private bool AutoCheckRow(string id, string title, string sub, string right, bool on, Vector4 accent, float width, float h, string tip = "")
	{
		ImGui.PushID(id);
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		bool result = ImGui.InvisibleButton("##hit", new Vector2(Math.Max(60f, width), h));
		bool flag = ImGui.IsItemHovered();
		if (flag && tip.Length > 0)
		{
			ImGui.SetTooltip(tip);
		}
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		if (flag | on)
		{
			windowDrawList.AddRectFilled(cursorScreenPos, cursorScreenPos + new Vector2(width, h), ImGui.GetColorU32(on ? new Vector4(accent.X, accent.Y, accent.Z, 0.12f) : V1Hover), 6f);
		}
		Vector2 vector = new Vector2(cursorScreenPos.X + 8f, cursorScreenPos.Y + (h - 15f) / 2f);
		windowDrawList.AddRect(vector, vector + new Vector2(15f, 15f), ImGui.GetColorU32(on ? accent : V1Border), 4f, ImDrawFlags.None, 1.4f);
		if (on)
		{
			windowDrawList.AddRectFilled(vector + new Vector2(3f, 3f), vector + new Vector2(12f, 12f), ImGui.GetColorU32(accent), 2f);
		}
		float x = vector.X + 24f;
		bool flag2 = sub.Length > 0;
		windowDrawList.AddText(FontFor(13.5f), 13.5f, new Vector2(x, cursorScreenPos.Y + (flag2 ? 2f : ((h - 17f) / 2f))), ImGui.GetColorU32(on ? V1Text : V1Dim), title);
		if (flag2)
		{
			windowDrawList.AddText(FontFor(11f), 11f, new Vector2(x, cursorScreenPos.Y + h - 14f), ImGui.GetColorU32(V1Faint), sub);
		}
		if (right.Length > 0)
		{
			float num = TW(right, 11.5f);
			windowDrawList.AddText(FontFor(11.5f), 11.5f, new Vector2(cursorScreenPos.X + width - num - 10f, cursorScreenPos.Y + (h - 14f) / 2f), ImGui.GetColorU32(V1Faint), right);
		}
		ImGui.PopID();
		return result;
	}

	private void AutoText(string text, float size, Vector4 col)
	{
		ImGui.PushFont(FontFor(size));
		ImGui.PushStyleColor(ImGuiCol.Text, col);
		ImGui.TextUnformatted(text);
		ImGui.PopStyleColor();
		ImGui.PopFont();
	}

	private (string state, double eta, double pct) BestStateOf(HuntData h, long now)
	{
		(string, double, double) result = ("unknown", 0.0, 0.0);
		int num = 9;
		WorldDeath[] worlds = h.worlds;
		foreach (WorldDeath worldDeath in worlds)
		{
			if (!(worldDeath.lastDeath <= 0.0) && (autoAllWorlds || autoWorlds.Count <= 0 || autoWorlds.Contains(worldDeath.world)))
			{
				(string, double, double) tuple = CalcState(h, worldDeath.lastDeath, now);
				int num2 = tuple.Item1 switch
				{
					"open" => 0,
					"cd" => 1,
					"over" => 2,
					_ => 3,
				};
				if (num2 < num)
				{
					num = num2;
					result = (tuple.Item1, tuple.Item2, tuple.Item3);
				}
			}
		}
		return result;
	}

	private static string TimeText(double seconds)
	{
		if (seconds <= 0.0)
		{
			return "—";
		}
		TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
		if (timeSpan.TotalHours >= 1.0)
		{
			return $"{(int)timeSpan.TotalHours}h{timeSpan.Minutes:00}m";
		}
		return $"{timeSpan.Minutes}m{timeSpan.Seconds:00}s";
	}

	public Plugin(IDalamudPluginInterface pluginInterface, IGameGui gameGui, ICommandManager commandManager, IDataManager dataManager, IFramework framework, IPluginLog log, IChatGui chatGui, IClientState clientState, IObjectTable objectTable, ICondition condition, ITargetManager targets, IGameInventory gameInventory)
	{
		this.gameGui = gameGui;
		this.commandManager = commandManager;
		this.dataManager = dataManager;
		this.framework = framework;
		this.log = log;
		this.chatGui = chatGui;
		this.clientState = clientState;
		this.objectTable = objectTable;
		this.condition = condition;
		this.targets = targets;
		this.gameInventory = gameInventory;
		pi = pluginInterface;
		uiBuilder = pluginInterface.UiBuilder;
		try
		{
			pluginDirectory = pluginInterface.AssemblyLocation.DirectoryName ?? ".";
		}
		catch
		{
			pluginDirectory = ".";
		}
		CommandInfo info = new CommandInfo(OnCommand)
		{
			HelpMessage = "魔法水桶：/水桶 conditions 条件订阅 · /水桶 size <main|auto|hud|settings|history|conditions> <宽度> <高度> 调整窗口 · /水桶 打开游戏内窗口 · /水桶 history 打开 S 怪历史 · /水桶 sonar [test|probe|clear] 实时击杀自检 · /水桶 trigger 查看已被他人触发的 S（与看板一致）· /水桶 trigger main on|off 看板源开关 · /水桶 trigger scan 与看板对账 · /水桶 trigger clear 清空黑名单 · /水桶 nav <地图> <X> <Y>（开图插旗+vnav寻路） · /水桶 goto <地图> <X> <Y> [服务器] [分线] · /水桶 discard · /水桶 weea · /水桶 stop（取消全部自动流程） · /水桶 summon [手动召唤随从] · /水桶 summonauto on|off · /水桶 leaveauto on|off",
			ShowInHelp = true
		};
		this.commandManager.AddHandler("/srankflag", info);
		try
		{
			this.commandManager.AddHandler("/水桶", info);
		}
		catch
		{
		}
		try
		{
			history = new SrankHistory(pluginDirectory, (string m) =>
			{
				this.log.Info("[魔法水桶] " + m);
			});
		}
		catch (Exception ex)
		{
			history = null;
			try
			{
				this.log.Warning("[魔法水桶] 历史库初始化失败，历史功能停用：" + ex.Message);
			}
			catch
			{
			}
		}
		LoadAutoSettings();
		RefreshUnlockedMinions();
		LoadSrankData();
		lastBearFetch = DateTime.UtcNow - BearInterval;
		this.framework.Update += Framework_OnUpdate;
		this.clientState.Login += ClientState_OnLogin;
		uiBuilder.Draw += DrawMainWindow;
		try
		{
			this.chatGui.ChatMessage += OnChatMessage;
		}
		catch
		{
		}
		try
		{
			this.chatGui.ChatMessage += OnSonarKillChatMessage;
		}
		catch
		{
		}
		StartServer();
		InitNativeHud();
	}

	public void Dispose()
	{
		teleportSnapshotPending = false;
		try
		{
			SavePrefs();
		}
		catch
		{
		}
		commandManager.RemoveHandler("/srankflag");
		commandManager.RemoveHandler("/水桶");
		framework.Update -= Framework_OnUpdate;
		clientState.Login -= ClientState_OnLogin;
		uiBuilder.Draw -= DrawMainWindow;
		try
		{
			nativeHud?.Shutdown();
		}
		catch
		{
		}
		try
		{
			chatGui.ChatMessage -= OnChatMessage;
		}
		catch
		{
		}
		try
		{
			chatGui.ChatMessage -= OnSonarKillChatMessage;
		}
		catch
		{
		}
		cts.Cancel();
		if(bearBulk.IsValueCreated)bearBulk.Value.Dispose();
		bearRequest?.Cancel();
		try
		{
			listener?.Stop();
		}
		catch
		{
		}
		listener = null;
		cts.Dispose();
	}

	private void OnCommand(string command, string args)
	{
		string[] array = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			mainWindowOpen = !mainWindowOpen;
			ApplyMainWindowNow();
			NoteUserWindowChange();
			Print(mainWindowOpen ? "主窗已打开" : "主窗已关闭（再敲 /水桶 可开回来）");
			return;
		}
		switch (array[0].ToLowerInvariant())
		{
		case "dc":
			if(array.Length==2&&srankData?.dcs.Any(dc=>dc.dc==array[1])==true)SelectDc(array[1]);
			else Print("用法：/水桶 dc <豆豆柴|莫古力|猫小胖|陆行鸟>");
			break;
		case "refresh":
			MaybeStartBearFetch(force:true,notify:true);
			break;
		case "settings":
			settingsWindowOpen = !settingsWindowOpen;
			nativeHud?.SetSettingsWindowOpen(settingsWindowOpen);
			NoteUserWindowChange();
			break;
		case "detail":
			string query = string.Join(" ", array.Skip(1));
			var detailRow = BuildMainSnapshot().Hunts.FirstOrDefault(row => query.Length == 0 || row.Cn.Contains(query, StringComparison.OrdinalIgnoreCase) || row.En.Contains(query, StringComparison.OrdinalIgnoreCase));
			if (detailRow == null) { Print("当前筛选中没有匹配的 S 怪。"); break; }
			mainWindowOpen = true;
			nativeHud?.ShowMainDetails(detailRow.Key);
			ApplyMainWindowNow();
			NoteUserWindowChange();
			break;
		case "size":
			if (array.Length == 4 && int.TryParse(array[2], out int width) && int.TryParse(array[3], out int height) && width > 0 && height > 0 && nativeHud?.ResizeWindow(array[1], width, height) == true)
				Print($"窗口尺寸已保存：{array[1]} · {width}×{height}");
			else
				Print("用法：/水桶 size <main|auto|hud|settings|history|conditions> <宽度> <高度>；也可拖动右下角调整。");
			break;
		case "history":
			historyWindowOpen = !historyWindowOpen;
			ApplyHistoryWindowNow();
			NoteUserWindowChange();
			Print(historyWindowOpen ? "S 怪历史窗口已打开" : "S 怪历史窗口已关闭");
			break;
		case "conditions":
		case "condition":
			ToggleConditions();
			break;
		case "sonar":
			SonarCommand((array.Length > 1) ? array[1].ToLowerInvariant() : "");
			break;
		case "trigger":
			TriggerCommand(array);
			break;
		case "auto":
			autoRunOpen = !autoRunOpen;
			nativeHud?.SetAutoWindowOpen(autoRunOpen);
			Print(autoRunOpen ? "自动前往悬浮窗已打开（勾选服务器和 S 后点开始）" : "自动前往悬浮窗已关闭");
			break;
		case "hud":
			autoHudOpen = !autoHudOpen;
			nativeHud?.SetHudWindowOpen(autoHudOpen);
			Print(autoHudOpen ? "触发悬浮窗已打开" : "触发悬浮窗已关闭（主窗按钮或 /水桶 hud 可再打开）");
			break;
		case "flag":
		{
			float result6;
			float result7;
			if (array.Length < 4)
			{
				Print("flag 需要：地图 X Y");
			}
			else if (!float.TryParse(array[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out result6) || !float.TryParse(array[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out result7))
			{
				Print("坐标要写成数字");
			}
			else
			{
				Print(Flag(string.Join(' ', array[1..^2]), result6, result7).Message);
			}
			break;
		}
		case "goto":
		{
			int num = -1;
			int num2 = -1;
			for (int num3 = array.Length - 1; num3 >= 1; num3--)
			{
				if (float.TryParse(array[num3], NumberStyles.Float, CultureInfo.InvariantCulture, out var _))
				{
					if (num >= 0)
					{
						num2 = num3;
						break;
					}
					num = num3;
				}
			}
			if (num2 < 0 || num < 0)
			{
				Print("goto 需要：地图 X Y [服务器] [分线]");
				break;
			}
			float x = float.Parse(array[num2], NumberStyles.Float, CultureInfo.InvariantCulture);
			float y = float.Parse(array[num], NumberStyles.Float, CultureInfo.InvariantCulture);
			string map = string.Join(' ', array[1..num2]);
			string[] array2 = array.Skip(num + 1).ToArray();
			string world = ((array2.Length != 0) ? array2[0] : "");
			int line = ((array2.Length > 1 && int.TryParse(array2[1], out var result9)) ? result9 : 0);
			Print(Goto(map, x, y, world, line).Message);
			break;
		}
		case "telepo":
		{
			float result;
			float result2;
			if (array.Length < 4)
			{
				Print("telepo 需要：地图 X Y");
			}
			else if (!float.TryParse(array[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out result) || !float.TryParse(array[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out result2))
			{
				Print("坐标要写成数字");
			}
			else
			{
				Print(TeleportNearest(string.Join(' ', array[1..^2]), result, result2).Message);
			}
			break;
		}
		case "worldtravel":
			if (array.Length < 2)
			{
				Print("worldtravel 需要：服务器名");
			}
			else
			{
				Print(WorldTravel(string.Join(' ', array[1..])).Message);
			}
			break;
		case "insc":
		{
			if (array.Length < 2 || !int.TryParse(array[1], out var result3))
			{
				Print("insc 需要：分线编号");
			}
			else
			{
				Print(ChangeInstance(result3).Message);
			}
			break;
		}
		case "discard":
			Print(DiscardTrigger().Message);
			break;
		case "weea":
		{
			WeeEaResult weeEaResult = QueryWeeEa();
			Print(weeEaResult.Ok ? ($"小异亚附近 {weeEaResult.Names.Count} 人：" + string.Join("、", weeEaResult.Names)) : weeEaResult.Message);
			break;
		}
		case "nav":
		{
			float result4;
			float result5;
			if (array.Length < 4)
			{
				Print("nav 需要：地图 X Y");
			}
			else if (!float.TryParse(array[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out result4) || !float.TryParse(array[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out result5))
			{
				Print("坐标要写成数字");
			}
			else
			{
				Print(NavTo(string.Join(' ', array[1..^2]), result4, result5).Message);
			}
			break;
		}
		case "summon":
			Print(SummonForCurrentTerritory());
			break;
		case "summonauto":
			if (array.Length >= 2)
			{
				autoSummonMinion = ParseOn(array[1]);
			}
			if (autoSummonMinion)
			{
				RefreshUnlockedMinions();
			}
			SaveAutoSettings();
			Print("自动召唤随从：" + (autoSummonMinion ? "已开启" : "已关闭"));
			break;
		case "leaveauto":
			if (array.Length >= 2)
			{
				autoLeaveDuty = ParseOn(array[1]);
			}
			SaveAutoSettings();
			Print("独身自动退本：" + (autoLeaveDuty ? "已开启" : "已关闭"));
			break;
		case "return":
		case "倒地":
			if (array.Length >= 2)
			{
				autoConfirmReturn = ParseOn(array[1]);
			}
			SaveAutoSettings();
			Print("倒地自动点「确定」返回：" + (autoConfirmReturn ? "已开启" : "已关闭") + (autoConfirmReturn ? "（副本里想躺平等复活就先关掉）" : ""));
			break;
		case "解窗口锁":
		case "lock":
		case "unlock":
		case "解锁":
		case "锁窗":
			Print("v1.2.9 起已移除窗口强锁：/水桶 直接开关主窗，过图落地后会自动恢复。");
			break;
		case "stop":
		case "halt":
		case "cancel":
		case "停止":
			StopAll();
			break;
		case "ui":
			mainWindowOpen = !mainWindowOpen;
			ApplyMainWindowNow();
			NoteUserWindowChange();
			break;
		case "legacy":
			mainWindowOpen = true;
			ApplyMainWindowNow();
			NoteUserWindowChange();
			Print("旧 ImGui 完整版已移除；已打开原生主窗。");
			break;
		case "mount":
			MountUpThen(() =>
			{
				Print("[坐骑] 流程结束");
			});
			break;
		default:
			Print("未知子命令。");
			break;
		}
	}

	private FlagResult Flag(string mapName, float x, float y)
	{
		if (!TryResolveZone(mapName, out uint territoryId, out uint mapId, out string resolved))
		{
			List<string> list = SearchZones(mapName, 4);
			string text = ((list.Count > 0) ? ("（相近：" + string.Join("、", list) + "）") : string.Empty);
			return new FlagResult(Ok: false, "找不到地图「" + mapName + "」" + text);
		}
		framework.RunOnFrameworkThread(() =>
		{
			try
			{
				MapLinkPayload mapLink = new MapLinkPayload(territoryId, mapId, x, y);
				gameGui.OpenMapWithMapLink(mapLink);
			}
			catch (Exception ex)
			{
				log.Error(ex, "[SrankFlag] OpenMapWithMapLink failed");
				try
				{
					chatGui.PrintError("[魔法水桶] 开图失败：" + ex.Message);
				}
				catch
				{
				}
			}
		});
		return new FlagResult(Ok: true, $"已开图插旗：{resolved} ({x:0.0}, {y:0.0})", territoryId, mapId);
	}

	private void FlagAetheryte(string mapName, string huntCn)
	{
		if (!TryResolveZone(mapName, out uint _, out uint mapId, out string resolved))
		{
			Print("[魔法水桶] 找不到地图「" + mapName + "」");
			return;
		}
		Lumina.Excel.Sheets.Map? map = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>()?.GetRow(mapId);
		if (!map.HasValue || map.Value.RowId == 0)
		{
			Print("[魔法水桶] 找不到地图行「" + resolved + "」");
			return;
		}
		List<(string, float, float)> list = AetherytesOnMap(map.Value);
		if (list.Count == 0)
		{
			Print("[魔法水桶] 「" + resolved + "」没有已解锁的大水晶，无法插旗");
			return;
		}
		(string, float, float)? tuple = null;
		if (HuntAetheryte.TryGetValue(huntCn, out string value))
		{
			string[] wants = (from x in value.Split('|').Select(Normalize)
				where x.Length > 0
				select x).ToArray();
			tuple = list.FirstOrDefault(((string Name, float X, float Y) a) => wants.Any((string w) => Normalize(a.Name).Contains(w)));
		}
		(string, float, float) tuple2 = tuple ?? list[0];
		Print(Flag(resolved, tuple2.Item2, tuple2.Item3).Message);
	}

	private bool TryResolveZone(string mapName, out uint territoryId, out uint mapId, out string resolved)
	{
		territoryId = 0u;
		mapId = 0u;
		resolved = mapName;
		string text = Normalize(mapName);
		if (text.Length == 0)
		{
			return false;
		}
		ExcelSheet<Lumina.Excel.Sheets.Map> excelSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
		if (excelSheet != null)
		{
			Lumina.Excel.Sheets.Map? map = null;
			foreach (Lumina.Excel.Sheets.Map item in excelSheet)
			{
				string text2 = SafeMapName(item);
				if (text2.Length != 0 && item.TerritoryType.RowId != 0)
				{
					string text3 = Normalize(text2);
					if (text3 == text)
					{
						mapId = item.RowId;
						territoryId = item.TerritoryType.RowId;
						resolved = text2;
						return true;
					}
					if (!map.HasValue && text3.Contains(text, StringComparison.Ordinal))
					{
						map = item;
					}
				}
			}
			if (map.HasValue)
			{
				Lumina.Excel.Sheets.Map valueOrDefault = map.GetValueOrDefault();
				mapId = valueOrDefault.RowId;
				territoryId = valueOrDefault.TerritoryType.RowId;
				resolved = SafeMapName(valueOrDefault);
				return true;
			}
		}
		ExcelSheet<TerritoryType> excelSheet2 = dataManager.GetExcelSheet<TerritoryType>();
		if (excelSheet2 == null)
		{
			return false;
		}
		foreach (TerritoryType item2 in excelSheet2)
		{
			string text4 = SafePlaceName(item2);
			if (text4.Length != 0 && item2.Map.RowId != 0 && Normalize(text4) == text)
			{
				territoryId = item2.RowId;
				mapId = item2.Map.RowId;
				resolved = text4;
				return true;
			}
		}
		return false;
	}

	private GotoResult Goto(string map, float x, float y, string world, int line, string huntCn = "", bool nav = false, List<Pt>? route = null)
	{
		CaptureWindowStateBeforeTeleport();
		StopAll(quiet: true);
		Resume();
		FlagResult flagResult = Flag(map, x, y);
		if (!flagResult.Ok)
		{
			return new GotoResult(Ok: false, flagResult.Message);
		}
		AetheryteHit? ae = ((route == null || route.Count <= 0 || HuntAetheryte.ContainsKey(huntCn)) ? NearestAetheryte(map, x, y, huntCn) : (AetheryteForRoute(map, route) ?? NearestAetheryte(map, x, y, huntCn)));
		if(!ae.HasValue)return new GotoResult(false,"在「"+map+"」找不到已解锁的大水晶");
		travelPreparationPending = true;
		bool flag = !string.IsNullOrWhiteSpace(world) && !string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase);
		if (flag)
		{
			SendWorldTravel(world);
			WaitUntilOr(() => !condition[ConditionFlag.BetweenAreas] && string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase), Teleport, () =>
			{
				AbortTravel("跨服超时，未到达 " + world);
			}, 480);
		}
		else
		{
			if (!string.IsNullOrWhiteSpace(world))
			{
				Print("已在目标服务器 " + world + "，跳过跨服");
			}
			Sched(Teleport, TimeSpan.FromMilliseconds(250L));
		}
		return new GotoResult(true, "已发起前往"+(flag?" "+world:"")+" · "+map+"（"+ae.Value.Name+"），确认落地后再执行后续动作");
		void Arrived()
		{
			WaitCorrectWorldThen(world, huntCn, () =>
			{
				RunPrepAtDestination(huntCn, world, map, () =>
				{
					if (line > 0)
					{
						Say("/pdr insc " + line);
					}
					if (nav)
					{
						WaitNavReadyThen(() =>
						{
							MountUpThen(() =>
							{
								if (route != null && route.Count > 0)
								{
									StartGreenRoute(map, route, huntCn);
								}
								else
								{
									Say("/vnav flyflag");
								}
							});
						});
					}
				});
			});
		}
		void Teleport()
		{
			TeleportConfirmed(ae.Value,map,world,Arrived);
		}
	}

	private int HuntLineOverride(string huntEn, string world)
	{
		if (string.IsNullOrEmpty(world) || string.IsNullOrEmpty(huntEn))
		{
			return 0;
		}
		string text = srankData?.dcs?.FirstOrDefault((SnapshotData x) => x.worlds.Contains(world, StringComparer.OrdinalIgnoreCase))?.dc ?? "";
		if (huntEn == "Atticus the Primogenitor" && (text == "猫小胖" || text == "陆行鸟"))
		{
			return 2;
		}
		return 0;
	}

	private void StartGreenRoute(string map, List<Pt> pts, string huntCn)
	{
		autoRouteStartedAt = DateTime.UtcNow;
		if (!TryResolveZone(map, out uint _, out uint mapId, out string _) || pts.Count == 0)
		{
			WaitNavReadyThen(() =>
			{
				Say("/vnav flyflag");
			});
			return;
		}
		Print($"[{huntCn}] 开始巡绿点：共 {pts.Count} 个");
		Lumina.Excel.Sheets.Map mapRow = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>()?.GetRow(mapId) ?? default(Lumina.Excel.Sheets.Map);
		ushort sizeFactor = (ushort)((mapRow.RowId != 0) ? mapRow.SizeFactor : 100);
		List<(string Name, float X, float Y)> aets = ((mapRow.RowId != 0) ? AetherytesOnMap(mapRow) : new List<(string, float, float)>());
		List<Pt> left = new List<Pt>(pts);
		int done = 0;
		Step();
		void Step()
		{
			Pt g;
			if (left.Count == 0)
			{
				autoRouteDoneAt = DateTime.UtcNow;
				Print($"[{huntCn}] 绿点已全部逐个试完（{pts.Count} 个），没刷出来就换下一只 S");
			}
			else
			{
				int index = 0;
				float num = float.MaxValue;
				for (int i = 0; i < left.Count; i++)
				{
					float num2 = YalmsTo(mapId, left[i].X, left[i].Y);
					if (num2 < num)
					{
						num = num2;
						index = i;
					}
				}
				g = left[index];
				left.RemoveAt(index);
				done++;
				float num3;
				if (StrictArriveHunts.Contains(huntCn))
				{
					num3 = 5f;
				}
				else
				{
					num3 = (QuickActionHunts.Contains(huntCn) ? 30f : 12f);
				}
				if (YalmsTo(mapId, g.X, g.Y) <= num3)
				{
					AtGreenPoint(g, map, huntCn, Step);
				}
				else
				{
					Print($"[{huntCn}] 去 {g.Id} 号点（{done}/{pts.Count}，后面还剩 {left.Count} 个待试）");
					Flag(map, g.X, g.Y);
					float curD = YalmsTo(mapId, g.X, g.Y);
					(string Name, float X, float Y, float Dist)? near = null;
					foreach (var item in aets)
					{
						float num4 = YalmsBetween((int)sizeFactor, item.X, item.Y, g.X, g.Y);
						if (!near.HasValue || num4 < near.Value.Dist)
						{
							near = (item.Name, item.X, item.Y, num4);
						}
					}
					bool worthTp = near.HasValue && near.GetValueOrDefault().Dist + 170f < curD;
					Sched(() =>
					{
						if (!worthTp)
						{
							Fly();
						}
						else
						{
							(string Name, float X, float Y, float Dist) a2 = near.Value;
							Print($"[{huntCn}] {g.Id} 号点离「{a2.Name}」{a2.Dist:F0} 亚尔（从这儿飞要 {curD:F0} 亚尔），先传大水晶");
							TeleportConfirmed(new AetheryteHit(a2.Name, a2.X, a2.Y, a2.Dist), map, CurrentWorldName(), Fly);
						}
					}, TimeSpan.FromMilliseconds(600L));
				}
			}
			void Fly()
			{
				FlyFlagThen(() =>
				{
					Plugin plugin = this;
					uint mapId2 = mapId;
					float x = g.X;
					float y = g.Y;
					System.Action then = () =>
					{
						AtGreenPoint(g, map, huntCn, Step);
					};
					System.Action giveUp = () =>
					{
						Print($"[{huntCn}] {g.Id} 号点 3 分钟没飞到，先跳过去");
						Step();
					};
					float earlyYalms = (StrictArriveHunts.Contains(huntCn) ? 0f : (QuickActionHunts.Contains(huntCn) ? 30f : 0f));
					plugin.WaitArrive(mapId2, x, y, then, giveUp, earlyYalms, StrictArriveHunts.Contains(huntCn), huntCn);
				});
			}
		}
	}

	private bool HasStatus(string name)
	{
		try
		{
			IPlayerCharacter localPlayer = objectTable.LocalPlayer;
			if (localPlayer == null)
			{
				return false;
			}
			ExcelSheet<Lumina.Excel.Sheets.Status> excelSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
			if (excelSheet == null)
			{
				return false;
			}
			StatusList statusList = localPlayer.StatusList;
			for (int i = 0; i < statusList.Length; i++)
			{
				IStatus status = statusList[i];
				if (status != null && status.StatusId != 0)
				{
					Lumina.Excel.Sheets.Status row = excelSheet.GetRow(status.StatusId);
					if (row.RowId != 0 && row.Name.ToString().Contains(name))
					{
						return true;
					}
				}
			}
		}
		catch (Exception exception)
		{
			log.Debug(exception, "[SrankFlag] 状态读取失败");
		}
		return false;
	}

	private void AtGreenPoint(Pt g, string map, string huntCn, System.Action next, int retry = 0, bool buffCleared = false)
	{
		if (TryResolveZone(map, out uint _, out uint gpMap, out string _))
		{
			float num = YalmsTo(gpMap, g.X, g.Y);
			float num2;
			if (StrictArriveHunts.Contains(huntCn))
			{
				num2 = 10f;
			}
			else
			{
				num2 = (QuickActionHunts.Contains(huntCn) ? 30f : 25f);
			}
			if (num > num2)
			{
				if (retry >= 2)
				{
					Print($"[{huntCn}] {g.Id} 号点几次都没飞到（还差 {num:F0} 亚尔），跳过");
					next();
					return;
				}
				Print($"[{huntCn}] 还没到 {g.Id} 号点（还差 {num:F0} 亚尔），不下坐骑，继续飞");
				Flag(map, g.X, g.Y);
				Sched(() =>
				{
					FlyFlagThen(() =>
					{
						Plugin plugin = this;
						uint mapId = gpMap;
						float x = g.X;
						float y = g.Y;
						System.Action then = () =>
						{
							AtGreenPoint(g, map, huntCn, next, retry + 1);
						};
						System.Action giveUp = next;
						float earlyYalms = (StrictArriveHunts.Contains(huntCn) ? 0f : (QuickActionHunts.Contains(huntCn) ? 30f : 0f));
						plugin.WaitArrive(mapId, x, y, then, giveUp, earlyYalms, StrictArriveHunts.Contains(huntCn), huntCn);
					});
				}, TimeSpan.FromMilliseconds(400L));
				return;
			}
		}
		switch (huntCn)
		{
		case "顾尼图":
			DismountThen(() =>
			{
				GroundedThen(() =>
				{
					Print($"[{huntCn}] {g.Id} 号点 · 已到触发点，开始顾尼图触发流程");
					GounituCore();
				});
			});
			break;
		case "天气预报机器人":
			DismountThen(() =>
			{
				GroundedThen(() =>
				{
					Print($"[{huntCn}] {g.Id} 号点 · 下坐骑放狂风暴雪");
					Say("/青魔法技能 狂风暴雪");
					Sched(() =>
					{
						WaitUntilOr(() => !condition[ConditionFlag.Casting], next, next, 40, 250);
					}, TimeSpan.FromMilliseconds(800L));
				});
			});
			break;
		case "多智兽":
		case "阿姆斯特朗":
			SelfDestructThen(huntCn, g.Id + " 号点", map, next, () =>
			{
				AtGreenPoint(g, map, huntCn, next, retry, buffCleared: true);
			});
			break;
		default:
			next();
			break;
		}
	}

	private void SelfDestructThen(string huntCn, string label, string map, System.Action next, System.Action resume, bool buffCleared = false)
	{
		bool needUniform = huntCn == "阿姆斯特朗";
		if (!buffCleared && HasStatus("意志薄弱"))
		{
			Print($"[{huntCn}] {label} · 身上有「意志薄弱」，先进退副本清掉再自爆");
			WaitDutyResetThen(() =>
			{
				if (HasStatus("意志薄弱"))
				{
					Print("[" + huntCn + "] 意志薄弱还在，跳过（确认 DailyRoutines 的自动进退副本是否正常工作）");
					next();
				}
				else
				{
					Print($"[{huntCn}] 意志薄弱已清掉，回到 {label} 准备自爆");
					resume();
				}
			}, map, huntCn);
			return;
		}
		DismountThen(() =>
		{
			GroundedThen(() =>
			{
				Print(needUniform ? $"[{huntCn}] {label} · 准备即刻咏唱 + 自爆（记得先穿好帝国套，判定要「穿着帝国套时 HP 归零」）" : $"[{huntCn}] {label} · 准备即刻咏唱 + 自爆");
				Sched(() =>
				{
					CastThen("/ac 即刻咏唱", () => HasStatus("即刻咏唱"), () =>
					{
						Sched(() =>
						{
							CastThen("/青魔法技能 自爆", () =>
							{
								bool flag = false;
								try
								{
									flag = condition[ConditionFlag.Casting];
								}
								catch
								{
								}
								return flag || Dead();
							}, () =>
							{
								WaitUntilOr(Dead, () =>
								{
									AfterDeathChain(map, huntCn, next);
								}, () =>
								{
									Print(needUniform ? ("[" + huntCn + "] 自爆没生效——确认切成青魔法师并学会自爆，跳过") : ("[" + huntCn + "] 自爆没生效——确认切成青魔法师、且已经学会自爆，跳过"));
									next();
								}, 45, 200);
							}, "自爆");
						}, TimeSpan.FromMilliseconds(400L));
					}, "即刻咏唱");
				}, TimeSpan.FromMilliseconds(500L));
			});
		});
	}

	private void GounituSignal(bool ok)
	{
		gounituFinishedAt = DateTime.UtcNow;
		gounituFlowOk = ok;
		Action<bool> action = gounituDone;
		gounituDone = null;
		if (action == null)
		{
			return;
		}
		try
		{
			action(ok);
		}
		catch (Exception exception)
		{
			log.Warning(exception, "[SrankFlag] 顾尼图回调出错");
		}
	}

	private void GounituTrigger(string world)
	{
		Resume();
		string map = "黑风海";
		Pt gounituPt = GounituPt;
		Print("[顾尼图] 开始：切战士 → 传黑风海大水晶 → 飞到固定点位 (29.8, 3.9)");
		Print("[顾尼图] " + Goto(map, gounituPt.X, gounituPt.Y, world, 0, "顾尼图", nav: true, new List<Pt> { gounituPt }).Message);
	}

	private void GounituCore()
	{
		gounituStartedAt = DateTime.UtcNow;
		Print("[顾尼图] 已到触发点，开始诱钓：对身边的深海水蛭逐个丢飞斧（3 只不同的）");
		LeechLoop(3, new HashSet<ulong>());
	}

	private void LeechLoop(int remaining, HashSet<ulong> hit)
	{
		if (remaining <= 0)
		{
			ProvokeAngel();
			return;
		}
		ActOnTarget("深海水蛭", "/ac 飞斧", 20f, (IGameObject hitObj) =>
		{
			hit.Add(hitObj.EntityId);
			Print($"[顾尼图] 已打 {3 - remaining + 1}/3 只深海水蛭，等飞斧复唱");
			Sched(() =>
			{
				LeechLoop(remaining - 1, hit);
			}, TimeSpan.FromMilliseconds(5500L));
		}, 10, hit);
	}

	private void ProvokeAngel()
	{
		ActOnTarget("冰海天使", "/ac 挑衅", 25f, (IGameObject _) =>
		{
			WaitAngelGrow();
		});
	}

	private void WaitAngelGrow()
	{
		WaitUntilOr(() => TargetHasStatus("巨大化") || TargetHasStatus("最大化"), WaitAngelMax, () =>
		{
			Print("[顾尼图] 60s 没看到巨大化/最大化 buff，超时（请确认冰海天使已被挑衅）");
			GounituSignal(ok: false);
		});
	}

	private void WaitAngelMax()
	{
		Print("[顾尼图] 已出现巨大化，等待最大化");
		try
		{
			gounituHpMark = objectTable.LocalPlayer?.CurrentHp ?? 0;
		}
		catch
		{
			gounituHpMark = 0f;
		}
		WaitUntilOr(() => TargetHasStatus("最大化"), WaitAngelHit, () =>
		{
			Print("[顾尼图] 看到巨大化但 60s 没到最大化，超时");
			GounituSignal(ok: false);
		});
	}

	private void WaitAngelHit()
	{
		WaitUntilOr(() =>
		{
			float num = 0f;
			try
			{
				num = objectTable.LocalPlayer?.CurrentHp ?? 0;
			}
			catch
			{
			}
			return num > 0f && num < gounituHpMark - 1f;
		}, () =>
		{
			Say("/bm off");
			Print("[顾尼图] ✅ 触发完成：已受到冰海天使口锥伤害");
			GounituSignal(ok: true);
		}, () =>
		{
			Say("/bm off");
			Print("[顾尼图] 已最大化但 30s 内没检测到受击，超时（触发可能仍已完成，请确认 S 是否刷新）");
			GounituSignal(ok: false);
		}, 60);
	}

	private void ActOnTarget(string name, string action, float range, Action<IGameObject> next, int attempts = 10, HashSet<ulong>? exclude = null, bool sheatheAfter = true)
	{
		if (halted)
		{
			return;
		}
		if (attempts <= 0)
		{
			Print("[顾尼图] 多次尝试仍没法对「" + name + "」动手，跳过该步继续");
			Say("/bm off");
			next(null);
			return;
		}
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		IGameObject obj = FindNearestByName(name, exclude);
		if (localPlayer == null || obj == null)
		{
			Print($"[顾尼图] 暂时没找到「{name}」，1.5s 后重试（剩 {attempts - 1} 次）");
			Sched(() =>
			{
				ActOnTarget(name, action, range, next, attempts - 1, exclude);
			}, TimeSpan.FromMilliseconds(1500L));
			return;
		}
		try
		{
			targets.Target = obj;
		}
		catch
		{
		}
		float num = Vector3.Distance(localPlayer.Position, obj.Position);
		if (num > range)
		{
			Print($"[顾尼图] 「{obj.Name}」约 {num:F0} 亚尔（射程 {range:F0}），改用 /vnav movetarget 走过去");
			Say("/vnav movetarget");
			WaitUntilOr(() =>
			{
				IPlayerCharacter localPlayer2 = objectTable.LocalPlayer;
				return localPlayer2 != null && obj != null && Vector3.Distance(localPlayer2.Position, obj.Position) <= range;
			}, () =>
			{
				StopNav();
				Say(action);
				Print($"[顾尼图] 已走到「{obj.Name}」（{range:F0} 亚尔内），使用 {action}");
				if (sheatheAfter)
				{
					Say("/bm off");
				}
				next(obj);
			}, () =>
			{
				StopNav();
				Say("/bm off");
				Print($"[顾尼图] 走到「{obj.Name}」超时（{range:F0} 亚尔内没进去），跳过这一下继续");
				next(obj);
			}, 140, 300);
		}
		else
		{
			Say(action);
			Print($"[顾尼图] 对「{obj.Name}」（{num:F0} 亚尔）使用 {action}");
			if (sheatheAfter)
			{
				Say("/bm off");
			}
			next(obj);
		}
	}

	private unsafe int HqItemCount(uint itemId)
	{
		try
		{
			InventoryManager* ptr = InventoryManager.Instance();
			if (ptr == null)
			{
				return -1;
			}
			return ptr->GetInventoryItemCount(itemId, isHq: true, checkEquipped: true, checkArmory: true, 0);
		}
		catch
		{
			return -1;
		}
	}

	private bool ArtisanBusy()
	{
		try
		{
			return pi.GetIpcSubscriber<bool>("Artisan.IsBusy")?.InvokeFunc() ?? false;
		}
		catch
		{
			return false;
		}
	}

	private void ArtisanSetEndurance(bool on)
	{
		try
		{
			pi.GetIpcSubscriber<bool, object>("Artisan.SetEnduranceStatus")?.InvokeAction(on);
		}
		catch
		{
		}
	}

	private bool GatherBuddySetAuto(bool on)
	{
		string[] array = new string[2] { "GatherBuddyReborn.SetAutoGatherEnabled", "GatherBuddy.SetAutoGatherEnabled" };
		foreach (string name in array)
		{
			try
			{
				ICallGateSubscriber<bool, object> ipcSubscriber = pi.GetIpcSubscriber<bool, object>(name);
				if (ipcSubscriber == null)
				{
					continue;
				}
				ipcSubscriber.InvokeAction(on);
				return true;
			}
			catch
			{
			}
		}
		return false;
	}

	private void CraftViaArtisan(ushort recipeId, uint itemId, int amount, string tag, System.Action then)
	{
		if(!EnsurePreparationLocation())return;
		int hqBefore;
		int gen;
		try
		{
			ICallGateSubscriber<ushort, int, object> craftSub = pi.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
			if (craftSub == null)
			{
				Print(tag + " 找不到 Artisan IPC（Artisan.CraftItem），请确认 Artisan 已启用，或手动制作");
				then();
				return;
			}
			if (ArtisanBusy())
			{
				Print(tag + " Artisan 正在忙别的制作，不插队，请手动处理");
				then();
				return;
			}
			hqBefore = HqItemCount(itemId);
			Print($"{tag} 调用 Artisan 自动制作：配方 {recipeId} × {amount}");
			gen = runGen;
			Sched(() =>
			{
				if (gen == runGen && !halted)
				{
					try
					{
						ownsArtisanCraft=true;
						craftSub.InvokeAction(recipeId, amount);
					}
					catch (Exception ex2)
					{
						ownsArtisanCraft=false;
						Print(tag + " Artisan 调用失败：" + ex2.Message + "，请手动制作");
						then();
						return;
					}
					Poll(0, 20);
				}
			}, TimeSpan.FromMilliseconds(100L));
		}
		catch (Exception ex)
		{
			Print(tag + " Artisan IPC 读取失败：" + ex.Message + "，请手动制作");
			then();
		}
		void Poll(int phase, int left)
		{
			Sched(() =>
			{
				if (gen == runGen && !halted)
				{
					try
					{
						bool flag = ArtisanBusy();
						if (phase == 0)
						{
							if (flag)
							{
								Poll(1, 300);
								return;
							}
							if (left <= 0)
							{
								ownsArtisanCraft = false;
								ArtisanSetEndurance(on: false);
								Print(tag + " Artisan 20 秒内没开始制作（材料不够/没启用？），请手动制作");
								then();
								return;
							}
						}
						else
						{
							if (!flag)
							{
								ownsArtisanCraft=false;
								ArtisanSetEndurance(on: false);
								int num = HqItemCount(itemId);
								Print($"{tag} Artisan 制作结束：背包 HQ {hqBefore} → {num}" + ((num > hqBefore) ? "，HQ 已做出 ✓" : "（没做出 HQ，请检查材料）"));
								then();
								return;
							}
							if (left <= 0)
							{
								ownsArtisanCraft = false;
								ArtisanSetEndurance(on: false);
								Print(tag + " Artisan 制作超过 10 分钟没结束，请手动检查");
								then();
								return;
							}
						}
					}
					catch (Exception ex2)
					{
						if (ownsArtisanCraft) ArtisanSetEndurance(false);
						ownsArtisanCraft = false;
						Print(tag + " 等 Artisan 出错：" + ex2.Message + "，请手动制作");
						then();
						return;
					}
					Poll(phase, left - 1);
				}
			}, TimeSpan.FromSeconds((phase == 0) ? 1 : 2));
		}
	}

	private void CraftStartThen(string recipeName, string tag, System.Action then)
	{
		ExcelSheet<Recipe> excelSheet = dataManager.GetExcelSheet<Recipe>();
		if (excelSheet == null)
		{
			Print(tag + " 配方表读不到，请手动制作「" + recipeName + "」");
			then();
			return;
		}
		uint num = 0u;
		uint itemId = 0u;
		foreach (Recipe item in excelSheet)
		{
			if (item.ItemResult.Value.Name.ToString().Contains(recipeName, StringComparison.Ordinal))
			{
				num = item.RowId;
				itemId = item.ItemResult.Value.RowId;
				break;
			}
		}
		if (num == 0 || num > 65535)
		{
			Print(tag + " 配方表里没找到「" + recipeName + "」（没学会？），请手动制作");
			then();
		}
		else
		{
			CraftViaArtisan((ushort)num, itemId, 1, tag, then);
		}
	}

	private unsafe AddonGathering* GatheringAddon()
	{
		try
		{
			AtkUnitBasePtr addonByName = gameGui.GetAddonByName("Gathering");
			if (addonByName.IsNull || !addonByName.IsVisible)
			{
				return null;
			}
			return (AddonGathering*)addonByName.Address;
		}
		catch
		{
			return null;
		}
	}

	private unsafe bool GatherOnce(int index, int strategy)
	{
		AddonGathering* ptr = GatheringAddon();
		if (ptr == null)
		{
			return false;
		}
		AtkComponentCheckBox* ptr2 = ptr->GatheredItemComponentCheckbox[index];
		if (ptr2 == null || !ptr2->IsEnabled)
		{
			return false;
		}
		if (!ptr2->IsChecked)
		{
			ptr2->SetChecked(isChecked: true);
		}
		AtkUnitBase* ptr3 = (AtkUnitBase*)ptr;
		try
		{
			if (strategy == 0)
			{
				AtkComponentNode* ownerNode = ptr2->OwnerNode;
				if (ownerNode == null)
				{
					return false;
				}
				AtkEvent* ptr4 = ownerNode->AtkResNode.AtkEventManager.Event;
				if (ptr4 == null)
				{
					return false;
				}
				AtkEventData* atkEventData = stackalloc AtkEventData[1];
				ptr3->ReceiveEvent(ptr4->State.EventType, (int)ptr4->Param, ptr4, atkEventData);
				return true;
			}
			AtkValue* ptr5 = stackalloc AtkValue[2];
			ptr5->Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int;
			ptr5->Int = 2;
			ptr5[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt;
			ptr5[1].UInt = (uint)index;
			ptr3->FireCallback(1u, ptr5);
			return true;
		}
		catch (Exception ex)
		{
			Print($"[采集窗口] 点击第 {index} 行失败：{ex.Message}");
			return false;
		}
	}

	private void XuanxueGatherThen(string huntCn, System.Action done)
	{
		uint itemId = 0u;
		try
		{
			ExcelSheet<Item> excelSheet = dataManager.GetExcelSheet<Item>();
			if (excelSheet != null)
			{
				foreach (Item item2 in excelSheet)
				{
					if (item2.Name.ToString() == "矮人棉")
					{
						itemId = item2.RowId;
						break;
					}
				}
			}
		}
		catch
		{
		}
		if (itemId == 0)
		{
			Print($"[{huntCn}] 物品表里没找到「{"矮人棉"}」，这次触发跳过");
			autoGatherDoneAt = DateTime.UtcNow;
			done();
			return;
		}
		if (!TryResolveZone("珂露西亚岛", out uint _, out uint mapId, out string _))
		{
			Print($"[{huntCn}] 找不到地图「{"珂露西亚岛"}」，这次触发跳过");
			autoGatherDoneAt = DateTime.UtcNow;
			done();
			return;
		}
		int gen = runGen;
		int count = 0;
		int groupIdx = 0;
		int slotIdx = -1;
		int round = 1;
		int stall = 0;
		int strategy = 0;
		int windowRetry = 0;
		DateTime started = DateTime.UtcNow;
		DateTime lastProgress = started;
		try
		{
			gameInventory.InventoryChanged += OnInvChanged;
		}
		catch (Exception ex)
		{
			Print($"[{huntCn}] 背包事件订阅失败：{ex.Message}，这次触发跳过");
			autoGatherDoneAt = DateTime.UtcNow;
			done();
			return;
		}
		Resume();
		StopNav();
		Print($"[{huntCn}] 开始巡草场：{XuanxueGrassGroups.Length} 组 × 每组 2 个候选（点位已用游戏内草场对象逐个验证），命中一个就原地反复采（耐久会恢复），最多采「{"矮人棉"}」{300} 次或触发成功");
		NextPoint(XuanxueAdvance.NextSlot);
		void ApproachGrass()
		{
			if (gen != runGen || halted)
			{
				Finish("已停止，退出采集");
			}
			else
			{
				Print($"[{huntCn}] 已到草场点，下坐骑找旁边的「{"草场"}」");
				StopNav();
				DismountThen(() =>
				{
					GroundedThen(() =>
					{
						if (gen != runGen || halted)
						{
							Finish("已停止，退出采集");
						}
						else
						{
							TryFindGrass(0);
						}
					});
				});
			}
		}
		void Finish(string msg)
		{
			try
			{
				gameInventory.InventoryChanged -= OnInvChanged;
			}
			catch
			{
			}
			StopNav();
			CloseAddonIfOpen("Gathering");
			autoGatherDoneAt = DateTime.UtcNow;
			Print("[" + huntCn + "] " + msg);
			done();
		}
		unsafe void GatherAtPoint()
		{
			if (gen != runGen || halted)
			{
				Finish("已停止，退出采集");
			}
			else if (count >= 300)
			{
				NextPoint(XuanxueAdvance.NextGroup);
			}
			else
			{
				AddonGathering* ptr = GatheringAddon();
				if (ptr == null)
				{
					RetrySameGrass("采集窗口关了");
				}
				else
				{
					windowRetry = 0;
					int num = -1;
					Span<uint> itemIds = ptr->ItemIds;
					for (int i = 0; i < itemIds.Length; i++)
					{
						if (itemIds[i] == itemId)
						{
							num = i;
							break;
						}
					}
					if (num < 0)
					{
						List<string> list = new List<string>();
						for (int j = 0; j < itemIds.Length; j++)
						{
							if (itemIds[j] != 0)
							{
								string value = "?";
								try
								{
									Item? item = dataManager.GetExcelSheet<Item>()?.GetRowOrDefault(itemIds[j]);
									if (item.HasValue)
									{
										value = item.GetValueOrDefault().Name.ToString();
									}
								}
								catch
								{
								}
								list.Add($"{j}:{value}");
							}
						}
						Print($"[{huntCn}] 采集窗口里没有「{"矮人棉"}」（本窗口：{string.Join("、", list)}），换下一组");
						CloseAddonIfOpen("Gathering");
						Sched(() =>
						{
							NextPoint(XuanxueAdvance.NextGroup);
						}, TimeSpan.FromMilliseconds(400L));
					}
					else if (num >= ptr->GatheredItemComponentCheckbox.Length)
					{
						CloseAddonIfOpen("Gathering");
						Sched(() =>
						{
							NextPoint(XuanxueAdvance.NextGroup);
						}, TimeSpan.FromMilliseconds(400L));
					}
					else
					{
						int before = count;
						if (!GatherOnce(num, strategy))
						{
							if (++stall < 4)
							{
								Sched(GatherAtPoint, TimeSpan.FromMilliseconds(3000L));
							}
							else
							{
								Print($"[{huntCn}] 「{"矮人棉"}」这一行一直点不动，退出采集窗口换下一组（{count}/{300}）");
								CloseAddonIfOpen("Gathering");
								Sched(() =>
								{
									NextPoint(XuanxueAdvance.NextGroup);
								}, TimeSpan.FromMilliseconds(500L));
							}
						}
						else
						{
							Sched(() =>
							{
								if (gen != runGen || halted)
								{
									Finish("已停止，退出采集");
								}
								else
								{
									if (count > before)
									{
										stall = 0;
										lastProgress = DateTime.UtcNow;
										if (count % 10 == 0)
										{
											Print($"[{huntCn}] 采集进度：{count}/{300} 次");
										}
									}
									else
									{
										int num2 = stall + 1;
										stall = num2;
										if (num2 >= 4)
										{
											stall = 0;
											if (strategy != 0)
											{
												strategy = 0;
												Print($"[{huntCn}] 这个草场点采不动，换下一组（{count}/{300}）");
												CloseAddonIfOpen("Gathering");
												NextPoint(XuanxueAdvance.NextGroup);
												return;
											}
											strategy = 1;
											Print("[" + huntCn + "] 点窗口没采到东西，切换另一种点击方式再试");
										}
									}
									GatherAtPoint();
								}
							}, TimeSpan.FromMilliseconds(3000L));
						}
					}
				}
			}
		}
		unsafe void NextPoint(XuanxueAdvance how)
		{
			if (gen != runGen || halted)
			{
				Finish("已停止，退出采集");
			}
			else if (count >= 300)
			{
				Finish($"已采「{"矮人棉"}」{count} 次（≥{300}），自动退出采集窗口 ✓");
			}
			else
			{
				DateTime utcNow = DateTime.UtcNow;
				if ((utcNow - started).TotalMinutes >= 30.0)
				{
					Finish($"总超时，只采到 {count}/{300} 次，退出");
				}
				else if ((utcNow - lastProgress).TotalMinutes >= 4.0)
				{
					Finish($"4 分钟没采到「{"矮人棉"}」（{count}/{300} 次），退出");
				}
				else if (GatheringAddon() != null)
				{
					CloseAddonIfOpen("Gathering");
					Print($"[{huntCn}] 采集已收尾，退出采集窗口，再去下一个点位（{count}/{300}）");
					Sched(() =>
					{
						if (gen != runGen || halted)
						{
							Finish("已停止，退出采集");
						}
						else
						{
							if (GatheringAddon() != null)
							{
								CloseAddonIfOpen("Gathering");
							}
							Advance();
						}
					}, TimeSpan.FromMilliseconds(600L));
				}
				else
				{
					Advance();
				}
			}
			void Advance()
			{
				(float, float)[] array = XuanxueGrassGroups[groupIdx];
				int num;
				if (how == XuanxueAdvance.NextGroup || slotIdx >= array.Length - 1)
				{
					if (how == XuanxueAdvance.NextGroup)
					{
						Print($"[{huntCn}] 这一组只刷一个草场，已采完，跳过同组另一个候选（{count}/{300}）");
					}
					num = groupIdx;
					groupIdx = num + 1;
					slotIdx = -1;
					if (groupIdx >= XuanxueGrassGroups.Length)
					{
						groupIdx = 0;
						num = round;
						round = num + 1;
						Print($"[{huntCn}] {XuanxueGrassGroups.Length} 组草场巡完一轮，开始第 {round} 轮（已采 {count}/{300}）");
					}
				}
				num = slotIdx;
				slotIdx = num + 1;
				array = XuanxueGrassGroups[groupIdx];
				(float X, float Y) p = array[slotIdx];
				stall = 0;
				windowRetry = 0;
				Print($"[{huntCn}] 第 {round} 轮 · 第 {groupIdx + 1}/{XuanxueGrassGroups.Length} 组 · 候选 {slotIdx + 1}/{array.Length} → ({p.X:F2}, {p.Y:F2})（已采 {count}/{300}）");
				Print($"[{huntCn}] 【vnav 点位】flyflag → {"珂露西亚岛"} ({p.X:F2}, {p.Y:F2}) 〔第 {round} 轮 · 第 {groupIdx + 1}/{XuanxueGrassGroups.Length} 组 · 候选 {slotIdx + 1}/{array.Length}〕");
				FlagResult flagResult = Flag("珂露西亚岛", p.X, p.Y);
				if (!flagResult.Ok)
				{
					Print("[" + huntCn + "] 插旗失败：" + flagResult.Message);
					Sched(() =>
					{
						NextPoint(XuanxueAdvance.NextSlot);
					}, TimeSpan.FromSeconds(2L));
				}
				else
				{
					Vector3 flyFrom;
					try
					{
						flyFrom = objectTable.LocalPlayer?.Position ?? Vector3.Zero;
					}
					catch
					{
						flyFrom = Vector3.Zero;
					}
					FlyFlagThen(() =>
					{
						WaitUntilOr(() =>
						{
							IPlayerCharacter localPlayer = objectTable.LocalPlayer;
							if (localPlayer == null)
							{
								return false;
							}
							return !(Vector3.Distance(flyFrom, localPlayer.Position) < 3f) && YalmsTo(mapId, p.X, p.Y) <= 8f;
						}, ApproachGrass, () =>
						{
							Print("[" + huntCn + "] 这个候选点没飞到，自动跳过");
							NextPoint(XuanxueAdvance.NextSlot);
						}, 600, 150);
					});
				}
			}
		}
		void OnInvChanged(IReadOnlyCollection<InventoryEventArgs> events)
		{
			if (gen != runGen)
			{
				return;
			}
			foreach (InventoryEventArgs @event in events)
			{
				if (@event != null && @event.Item.ItemId == itemId)
				{
					if (@event.Type == GameInventoryEvent.Added)
					{
						count++;
						break;
					}
					if (@event.Type == GameInventoryEvent.Changed && @event is InventoryItemChangedArgs inventoryItemChangedArgs && inventoryItemChangedArgs.Item.Quantity > inventoryItemChangedArgs.OldItemState.Quantity)
					{
						count++;
						break;
					}
				}
			}
		}
		unsafe void OpenWindow(IGameObject gameObject)
		{
			if (gen != runGen || halted)
			{
				Finish("已停止，退出采集");
			}
			else
			{
				try
				{
					GameObject* address = (GameObject*)gameObject.Address;
					ulong num = TargetSystem.Instance()->InteractWithObject(address);
					Print((num != 0L) ? $"[{huntCn}] 已点「{"草场"}」，等采集窗口" : $"[{huntCn}] 点「{"草场"}」没响应（可能还差几步 / 没切园艺工）");
				}
				catch (Exception ex2)
				{
					Print($"[{huntCn}] 交互「{"草场"}」出错：{ex2.Message}");
				}
				WaitUntilOr(() => GatheringAddon() != null, GatherAtPoint, () =>
				{
					RetrySameGrass("8 秒没等到采集窗口");
				}, 16);
			}
		}
		void RetrySameGrass(string why)
		{
			if (gen != runGen || halted)
			{
				Finish("已停止，退出采集");
			}
			else if (windowRetry >= 6)
			{
				Print($"[{huntCn}] {why}；连续 {6} 次都开不了窗口，去下一组（{count}/{300}）");
				CloseAddonIfOpen("Gathering");
				Sched(() =>
				{
					NextPoint(XuanxueAdvance.NextGroup);
				}, TimeSpan.FromMilliseconds(800L));
			}
			else
			{
				windowRetry++;
				Print($"[{huntCn}] {why}，原地等耐久恢复后继续采（第 {windowRetry}/{6} 次，{count}/{300}）");
				Sched(() =>
				{
					if (gen != runGen || halted)
					{
						Finish("已停止，退出采集");
					}
					else
					{
						StopNav();
						TryFindGrass(0);
					}
				}, TimeSpan.FromMilliseconds(1500L));
			}
		}
		void TryFindGrass(int attempt)
		{
			if (gen != runGen || halted)
			{
				Finish("已停止，退出采集");
			}
			else
			{
				IGameObject gameObject = FindNearestByName("草场", null, 30f) ?? FindNearestByName("图姆拉村", null, 30f);
				if (gameObject != null)
				{
					UseGrass(gameObject);
				}
				else
				{
					string text = ((attempt == 0) ? "草场" : "图姆拉村");
					try
					{
						targets.Target = null;
					}
					catch
					{
					}
					Say("/target " + text);
					Sched(() =>
					{
						if (gen != runGen || halted)
						{
							Finish("已停止，退出采集");
						}
						else
						{
							IGameObject gameObject2 = null;
							try
							{
								gameObject2 = targets.Target;
							}
							catch
							{
							}
							string text2 = gameObject2?.Name?.ToString() ?? string.Empty;
							IPlayerCharacter localPlayer = objectTable.LocalPlayer;
							float num = ((localPlayer != null && gameObject2 != null) ? Vector3.Distance(localPlayer.Position, gameObject2.Position) : float.MaxValue);
							if (gameObject2 != null && num <= 30f && (text2.Contains("草场", StringComparison.OrdinalIgnoreCase) || text2.Contains("图姆拉村", StringComparison.OrdinalIgnoreCase)))
							{
								Print($"[{huntCn}] /target 选中「{text2}」（{num:F1} 亚尔），交给 vnav movetarget");
								UseGrass(gameObject2);
							}
							else if (attempt < 1)
							{
								Sched(() =>
								{
									TryFindGrass(attempt + 1);
								}, TimeSpan.FromMilliseconds(500L));
							}
							else
							{
								List<string> list = new List<string>();
								try
								{
									IPlayerCharacter localPlayer2 = objectTable.LocalPlayer;
									if (localPlayer2 != null)
									{
										foreach (IGameObject item3 in objectTable)
										{
											if (item3 != null)
											{
												string text3 = item3.Name?.ToString() ?? string.Empty;
												if (text3.Length != 0)
												{
													float num2 = Vector3.Distance(localPlayer2.Position, item3.Position);
													if (num2 <= 30f)
													{
														list.Add($"{text3}@{num2:F0}");
													}
												}
											}
										}
									}
								}
								catch
								{
								}
								Print($"[{huntCn}] 这个点位旁边（{30f:F0} 亚尔内）没有「{"草场"}」，直接去下一组；附近对象：{((list.Count > 0) ? string.Join("、", list.Take(12)) : "（对象表里没有）")}");
								Sched(() =>
								{
									NextPoint(XuanxueAdvance.NextGroup);
								}, TimeSpan.FromMilliseconds(300L));
							}
						}
					}, TimeSpan.FromMilliseconds(400L));
				}
			}
		}
		void UseGrass(IGameObject gameObject)
		{
			if (gen != runGen || halted)
			{
				Finish("已停止，退出采集");
			}
			else
			{
				try
				{
					targets.Target = gameObject;
				}
				catch
				{
				}
				(float, float) tuple = XuanxueGrassGroups[groupIdx][slotIdx];
				string value = gameObject.Name?.ToString() ?? "草场";
				Print($"[{huntCn}] 【vnav 点位】movetarget → 「{value}」世界({gameObject.Position.X:F1}, {gameObject.Position.Y:F1}, {gameObject.Position.Z:F1})；本组点位 第 {groupIdx + 1}/{XuanxueGrassGroups.Length} 组 候选 {slotIdx + 1}/2 ({tuple.Item1:F2}, {tuple.Item2:F2})");
				if (Near())
				{
					OpenWindow(gameObject);
				}
				else
				{
					Say("/vnav movetarget");
					WaitUntilOr(Near, () =>
					{
						StopNav();
						OpenWindow(gameObject);
					}, () =>
					{
						StopNav();
						RetrySameGrass("靠不到「草场」");
					}, 40, 250);
				}
			}
			bool Near()
			{
				try
				{
					IPlayerCharacter localPlayer = objectTable.LocalPlayer;
					return localPlayer != null && Vector3.Distance(localPlayer.Position, gameObject.Position) <= 3.2f;
				}
				catch
				{
					return false;
				}
			}
		}
	}

	private void GatherStrikesThen(string itemName, int strikes, string tag, System.Action then)
	{
		uint itemId = 0u;
		try
		{
			ExcelSheet<Item> excelSheet = dataManager.GetExcelSheet<Item>();
			if (excelSheet != null)
			{
				foreach (Item item in excelSheet)
				{
					if (item.Name.ToString() == itemName)
					{
						itemId = item.RowId;
						break;
					}
				}
			}
		}
		catch
		{
		}
		if (itemId == 0)
		{
			Print($"{tag} 物品表里没找到「{itemName}」，请手动采集 {strikes} 次");
			then();
			return;
		}
		int count = 0;
		int gen = runGen;
		DateTime lastTick = DateTime.UtcNow;
		int lastCount = 0;
		int lastReport = 0;
		bool hinted = false;
		bool draining = false;
		int drainTries = 20;
		try
		{
			gameInventory.InventoryChanged += OnInvChanged;
		}
		catch (Exception ex)
		{
			Print($"{tag} 背包事件订阅失败：{ex.Message}，请手动采集 {strikes} 次");
			then();
			return;
		}
		Print($"{tag} 开始自动采集：目标 {itemName} × {strikes} 次（GatherBuddy 自动采集）。" + "请确认 GatherBuddy 采集清单里有该物品且数量 ≥ 999");
		if (!GatherBuddySetAuto(on: true))
		{
			Print($"{tag} 调用 GatherBuddy 自动采集失败（IPC 没接上），请手动采集 {strikes} 次");
			try
			{
				gameInventory.InventoryChanged -= OnInvChanged;
			}
			catch
			{
			}
			then();
			return;
		}
		Sched(() =>
		{
			if (gen == runGen)
			{
				string text = GatherBuddyAutoStatus();
				if (text.Length > 0)
				{
					Print(tag + " GatherBuddy 状态：" + text);
				}
			}
		}, TimeSpan.FromSeconds(3L));
		Poll();
		void Finish(string msg, bool disableAuto)
		{
			try
			{
				gameInventory.InventoryChanged -= OnInvChanged;
			}
			catch
			{
			}
			if (disableAuto)
			{
				GatherBuddySetAuto(on: false);
			}
			CloseAddonIfOpen("Gathering");
			if (gen == runGen && !halted)
			{
				autoGatherDoneAt = DateTime.UtcNow;
			}
			Print(msg);
			then();
		}
		void OnInvChanged(IReadOnlyCollection<InventoryEventArgs> events)
		{
			if (gen != runGen)
			{
				return;
			}
			foreach (InventoryEventArgs @event in events)
			{
				if (@event != null && @event.Item.ItemId == itemId)
				{
					if (@event.Type == GameInventoryEvent.Added)
					{
						count++;
						break;
					}
					if (@event.Type == GameInventoryEvent.Changed && @event is InventoryItemChangedArgs inventoryItemChangedArgs && inventoryItemChangedArgs.Item.Quantity > inventoryItemChangedArgs.OldItemState.Quantity)
					{
						count++;
						break;
					}
				}
			}
		}
		void Poll()
		{
			Sched(() =>
			{
				if (gen != runGen || halted)
				{
					Finish(tag + " 流程已停止，自动采集退出", disableAuto: true);
				}
				else
				{
					try
					{
						if (count >= strikes)
						{
							draining = true;
						}
						bool flag = false;
						try
						{
							AtkUnitBasePtr addonByName = gameGui.GetAddonByName("Gathering");
							flag = !addonByName.IsNull && addonByName.IsVisible;
						}
						catch
						{
						}
						if (draining)
						{
							if (!flag || drainTries <= 0)
							{
								Finish($"{tag} 采集完成：已采 {itemName} {count} 次（≥{strikes}）✓", disableAuto: true);
								return;
							}
							drainTries--;
						}
						DateTime utcNow = DateTime.UtcNow;
						if (count != lastCount)
						{
							lastCount = count;
							lastTick = utcNow;
							hinted = false;
						}
						if (count > 0 && count / 10 != lastReport / 10)
						{
							lastReport = count;
							Print($"{tag} 采集进度：{count}/{strikes} 次");
						}
						if ((utcNow - lastTick).TotalMinutes >= 5.0 && !hinted)
						{
							hinted = true;
							Print(tag + " 5 分钟没有采到 " + itemName + "：检查 GatherBuddy 采集清单里有没有它（数量 ≥ 999）、当前是不是园艺工");
						}
						if ((utcNow - lastTick).TotalMinutes >= 40.0)
						{
							Finish($"{tag} 自动采集超时（只采到 {count}/{strikes} 次），退出并继续，剩下的请手动补", disableAuto: true);
							return;
						}
					}
					catch (Exception ex2)
					{
						Finish(tag + " 采集计数出错：" + ex2.Message + "，退出自动采集", disableAuto: true);
						return;
					}
					Poll();
				}
			}, TimeSpan.FromSeconds(2L));
		}
	}

	private unsafe bool CloseAddonIfOpen(string addonName)
	{
		try
		{
			AtkUnitBasePtr addonByName = gameGui.GetAddonByName(addonName);
			if (addonByName.IsNull || !addonByName.IsVisible)
			{
				return false;
			}
			AtkUnitBase* address = (AtkUnitBase*)addonByName.Address;
			if (address == null)
			{
				return false;
			}
			address->Close(fireCallback: true);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private string GatherBuddyAutoStatus()
	{
		string[] array = new string[2] { "GatherBuddyReborn.", "GatherBuddy." };
		foreach (string text in array)
		{
			try
			{
				bool flag = pi.GetIpcSubscriber<bool>(text + "IsAutoGatherEnabled").InvokeFunc();
				return string.Concat(str2: pi.GetIpcSubscriber<string>(text + "GetAutoGatherStatusText").InvokeFunc(), str0: flag ? "已开启" : "未开启", str1: " / ");
			}
			catch
			{
			}
		}
		return string.Empty;
	}

	private IGameObject? FindNearestByName(string name, HashSet<ulong>? exclude = null, float maxDist = float.MaxValue)
	{
		try
		{
			IPlayerCharacter localPlayer = objectTable.LocalPlayer;
			if (localPlayer == null)
			{
				return null;
			}
			IGameObject result = null;
			float num = float.MaxValue;
			foreach (IGameObject item in objectTable)
			{
				if (item != null && (exclude == null || !exclude.Contains(item.EntityId)) && (item.Name?.ToString() ?? string.Empty).Contains(name, StringComparison.OrdinalIgnoreCase))
				{
					float num2 = Vector3.Distance(localPlayer.Position, item.Position);
					if (!(num2 > maxDist) && num2 < num)
					{
						num = num2;
						result = item;
					}
				}
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private IGameObject? CurrentTargetObj()
	{
		try
		{
			IPlayerCharacter localPlayer = objectTable.LocalPlayer;
			if (localPlayer == null)
			{
				return null;
			}
			ulong tid = localPlayer.TargetObjectId;
			if (tid == 0L)
			{
				return null;
			}
			return objectTable.FirstOrDefault((IGameObject o) => o != null && o.EntityId == tid);
		}
		catch
		{
			return null;
		}
	}

	private bool TargetHasStatus(string name)
	{
		try
		{
			if (!(CurrentTargetObj() is IBattleChara battleChara))
			{
				return false;
			}
			StatusList statusList = battleChara.StatusList;
			ExcelSheet<Lumina.Excel.Sheets.Status> excelSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
			if (excelSheet == null)
			{
				return false;
			}
			for (int i = 0; i < statusList.Length; i++)
			{
				IStatus status = statusList[i];
				if (status != null && status.StatusId != 0)
				{
					Lumina.Excel.Sheets.Status row = excelSheet.GetRow(status.StatusId);
					if (row.RowId != 0 && row.Name.ToString().Contains(name))
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private bool Dead()
	{
		try
		{
			if (condition[ConditionFlag.Unconscious])
			{
				return true;
			}
			IPlayerCharacter localPlayer = objectTable.LocalPlayer;
			return localPlayer != null && localPlayer.CurrentHp == 0;
		}
		catch
		{
			return false;
		}
	}

	private void GroundedThen(System.Action then)
	{
		int tries = 0;
		Sched(Tick, TimeSpan.FromMilliseconds(150L));
		void Tick()
		{
			bool flag = false;
			try
			{
				flag = condition[ConditionFlag.InFlight] || condition[ConditionFlag.Jumping];
			}
			catch
			{
			}
			if (!flag || ++tries > 20)
			{
				then();
			}
			else
			{
				Sched(Tick, TimeSpan.FromMilliseconds(200L));
			}
		}
	}

	private void ConfirmReturnThen(string map, string huntCn, System.Action then)
	{
		bool wasDead = false;
		int left = 240;
		Sched(Tick, TimeSpan.FromMilliseconds(400L));
		void Tick()
		{
			try
			{
				if (Dead())
				{
					if (!wasDead)
					{
						Print("[" + huntCn + "] 已倒地，等「返回大水晶」弹窗");
					}
					wasDead = true;
					ClickYesNo("SelectYesno");
				}
				else if (wasDead && !IsZoning())
				{
					if (!InMap(map))
					{
						AetheryteHit? aetheryteHit = NearestAetheryte(map, 0f, 0f, huntCn);
						if (aetheryteHit.HasValue)
						{
							AetheryteHit valueOrDefault = aetheryteHit.GetValueOrDefault();
							Print($"[{huntCn}] 复活后不在「{map}」——不用改返回点，直接传本图大水晶，继续下一个绿点");
							TeleportConfirmed(valueOrDefault, map, CurrentWorldName(), () =>
							{
								Print($"[{huntCn}] 已传回「{map}」大水晶，继续下一个绿点");
								then();
							});
						}
						else
						{
							AbortTravel($"复活后不在「{map}」，找不到已解锁的大水晶");
						}
					}
					else
					{
						Print("[" + huntCn + "] 已在本图复活，继续下一个绿点");
						then();
					}
					return;
				}
			}
			catch
			{
			}
			if (--left > 0)
			{
				Sched(Tick, TimeSpan.FromMilliseconds(250L));
			}
			else
			{
				AbortTravel("等待复活超时：" + huntCn);
			}
		}
	}

	private unsafe void ClickYesNo(string addonName)
	{
		try
		{
			AddonSelectYesno* addonByName = gameGui.GetAddonByName<AddonSelectYesno>(addonName);
			if (addonByName != null && addonByName->IsVisible)
			{
				addonByName->FireCallbackInt(0);
			}
		}
		catch
		{
		}
	}

	private void AfterDeathChain(string map, string huntCn, System.Action next)
	{
		ConfirmReturnThen(map, huntCn, () =>
		{
			WaitDutyResetThen(next, map, huntCn);
		});
	}

	private void WaitDutyResetThen(System.Action then, string map = "", string huntCn = "")
	{
		Print("[意志薄弱] /pdr joinexitduty：进退副本一次");
		Say("/pdr joinexitduty");
		int ticks = 0;
		bool sawDuty = false;
		bool sawBusy = false;
		int quiet = 0;
		bool warned = false;
		Sched(Tick, TimeSpan.FromMilliseconds(800L));
		void Tick()
		{
			bool flag = false;
			bool flag2 = false;
			try
			{
				flag = condition[ConditionFlag.BoundByDuty];
				flag2 = flag || condition[ConditionFlag.BetweenAreas];
			}
			catch
			{
			}
			if (flag)
			{
				sawDuty = true;
			}
			if (flag2)
			{
				if (!sawBusy)
				{
					Print("[意志薄弱] 已进本，等它退出");
				}
				sawBusy = true;
				quiet = 0;
			}
			else if (sawBusy)
			{
				quiet++;
				if (quiet >= 12)
				{
					Print("[意志薄弱] 已出本，继续");
					ResumeAfterDutyThen(map, huntCn, then);
					return;
				}
			}
			ticks++;
			if (!sawBusy && ticks == 120 && !warned)
			{
				warned = true;
				Print("[意志薄弱] 60 秒还没进本——确认 DailyRoutines 的「自动进退副本」模块是开着的");
			}
			if (!sawBusy && ticks > 180)
			{
				Print("[意志薄弱] 90 秒还没进本，这次进退副本跳过");
				ResumeAfterDutyThen(map, huntCn, then);
			}
			else if (ticks > 960)
			{
				Print(sawDuty ? "[意志薄弱] 8 分钟还没退出副本，先继续下一个绿点" : "[意志薄弱] 8 分钟没等到进退副本完成，先继续下一个绿点");
				ResumeAfterDutyThen(map, huntCn, then);
			}
			else
			{
				Sched(Tick, TimeSpan.FromMilliseconds(500L));
			}
		}
	}

	private void ResumeAfterDutyThen(string map, string huntCn, System.Action then)
	{
		if (string.IsNullOrWhiteSpace(map))
		{
			then();
			return;
		}
		int ticks = 0;
		Sched(Tick, TimeSpan.FromMilliseconds(200L));
		void Tick()
		{
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			try
			{
				flag = InMap(map);
				flag2 = IsZoning() || condition[ConditionFlag.BoundByDuty];
				flag3 = !flag2 && !Dead() && (Mounted() || CanMountNow());
			}
			catch
			{
			}
			if (flag & flag3)
			{
				Print("[" + huntCn + "] 出本后已在本图落地，马上飞下一个触发点");
				then();
			}
			else
			{
				if (!flag && !flag2 && ticks >= 4)
				{
					AetheryteHit? aetheryteHit = NearestAetheryte(map, 0f, 0f, huntCn);
					if (aetheryteHit.HasValue)
					{
						AetheryteHit valueOrDefault = aetheryteHit.GetValueOrDefault();
						Print($"[{huntCn}] 退本后不在「{map}」，直接传本图大水晶（不用改返回点）");
						TeleportConfirmed(valueOrDefault, map, CurrentWorldName(), then);
					}
					else AbortTravel("退本后找不到已解锁的大水晶：" + map);
					return;
				}
				if (++ticks > 60)
				{
					AbortTravel("退本后未就绪：" + huntCn);
				}
				else
				{
					Sched(Tick, TimeSpan.FromMilliseconds(500L));
				}
			}
		}
	}

	private float YalmsTo(uint mapId, float x, float y)
	{
		try
		{
			(float, float)? tuple = PlayerMapCoords(mapId);
			if (tuple.HasValue)
			{
				(float, float) valueOrDefault = tuple.GetValueOrDefault();
				Lumina.Excel.Sheets.Map map = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>()?.GetRow(mapId) ?? default(Lumina.Excel.Sheets.Map);
				if (map.RowId == 0)
				{
					return float.MaxValue;
				}
				return YalmsBetween((int)map.SizeFactor, valueOrDefault.Item1, valueOrDefault.Item2, x, y);
			}
			return float.MaxValue;
		}
		catch
		{
			return float.MaxValue;
		}
	}

	private (float X, float Y)? PlayerMapCoords(uint mapId)
	{
		try
		{
			IPlayerCharacter localPlayer = objectTable.LocalPlayer;
			if (localPlayer == null)
			{
				return null;
			}
			Lumina.Excel.Sheets.Map map = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>()?.GetRow(mapId) ?? default(Lumina.Excel.Sheets.Map);
			if (map.RowId == 0)
			{
				return null;
			}
			return (MapUtil.ConvertWorldCoordXZToMapCoord(localPlayer.Position.X, map.SizeFactor, map.OffsetX), MapUtil.ConvertWorldCoordXZToMapCoord(localPlayer.Position.Z, map.SizeFactor, map.OffsetY));
		}
		catch
		{
			return null;
		}
	}

	private static float YalmsBetween(float sizeFactor, float x1, float y1, float x2, float y2)
	{
		float num = x1 - x2;
		float num2 = y1 - y2;
		return MathF.Sqrt(num * num + num2 * num2) * sizeFactor / 2f;
	}

	private List<(string Name, float X, float Y)> AetherytesOnMap(Lumina.Excel.Sheets.Map mapRow)
	{
		List<(string, float, float)> list = new List<(string, float, float)>();
		try
		{
			ExcelSheet<Lumina.Excel.Sheets.Aetheryte> excelSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
			SubrowExcelSheet<MapMarker> subrowExcelSheet = dataManager.GetSubrowExcelSheet<MapMarker>();
			if (excelSheet == null || subrowExcelSheet == null)
			{
				return list;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			float num = (float)(int)mapRow.SizeFactor / 100f;
			foreach (SubrowCollection<MapMarker> item in subrowExcelSheet)
			{
				foreach (MapMarker item2 in item)
				{
					if (item2.DataType != 3 || item2.DataKey.RowId == 0)
					{
						continue;
					}
					Lumina.Excel.Sheets.Aetheryte row;
					try
					{
						row = excelSheet.GetRow(item2.DataKey.RowId);
					}
					catch
					{
						continue;
					}
					if (row.RowId != 0 && row.IsAetheryte && row.Map.RowId == mapRow.RowId)
					{
						string text = SafePlaceNameFrom(row);
						if (!string.IsNullOrEmpty(text) && hashSet.Add(text) && AetheryteUnlocked(row.RowId))
						{
							float value = ((float)item2.X - 1024f) * num;
							float value2 = ((float)item2.Y - 1024f) * num;
							list.Add((text, MapUtil.ConvertWorldCoordXZToMapCoord(value, mapRow.SizeFactor, mapRow.OffsetX), MapUtil.ConvertWorldCoordXZToMapCoord(value2, mapRow.SizeFactor, mapRow.OffsetY)));
						}
					}
				}
			}
		}
		catch (Exception exception)
		{
			log.Debug(exception, "[SrankFlag] 大水晶坐标读取失败");
		}
		return list;
	}

	private AetheryteHit? AetheryteForRoute(string map, List<Pt> pts)
	{
		try
		{
			if (!TryResolveZone(map, out uint _, out uint mapId, out string _))
			{
				return null;
			}
			Lumina.Excel.Sheets.Map mapRow = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>()?.GetRow(mapId) ?? default(Lumina.Excel.Sheets.Map);
			if (mapRow.RowId == 0)
			{
				return null;
			}
			AetheryteHit? result = null;
			foreach (var item in AetherytesOnMap(mapRow))
			{
				float num = float.MaxValue;
				foreach (Pt pt in pts)
				{
					num = MathF.Min(num, YalmsBetween((int)mapRow.SizeFactor, item.X, item.Y, pt.X, pt.Y));
				}
				if (!result.HasValue || (double)num < result.Value.Dist)
				{
					result = new AetheryteHit(item.Name, item.X, item.Y, num);
				}
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private unsafe bool AetheryteUnlocked(uint aetheryteId)
	{
		try
		{
			UIState* ptr = UIState.Instance();
			if (ptr == null)
			{
				return false;
			}
			return ptr->IsAetheryteUnlocked(aetheryteId);
		}
		catch
		{
			return false;
		}
	}

	private void WaitArrive(uint mapId, float x, float y, System.Action then, System.Action? giveUp = null, float earlyYalms = 0f, bool strictArrive = false, string label = "")
	{
		int left = 1200;
		int still = 0;
		bool hasLast = false;
		Vector3 last = Vector3.Zero;
		int retries = 0;
		Sched(Tick, TimeSpan.FromMilliseconds(120L));
		void Tick()
		{
			float num = YalmsTo(mapId, x, y);
			bool flag = false;
			try
			{
				IPlayerCharacter localPlayer = objectTable.LocalPlayer;
				if (localPlayer != null)
				{
					Vector3 position = localPlayer.Position;
					if (hasLast && Vector3.Distance(position, last) < 0.4f)
					{
						still++;
					}
					else
					{
						still = 0;
					}
					last = position;
					hasLast = true;
					flag = true;
				}
			}
			catch
			{
			}
			if (strictArrive)
			{
				bool flag2 = !Mounted() && !condition[ConditionFlag.InFlight];
				if ((num <= 5f) & flag2)
				{
					Print($"[{label}] 已严格到达触发点（{num:F1} 亚尔，已落地），立即停寻路并自爆");
					StopNav();
					then();
				}
				else if (flag && still >= 20 && num <= 10f)
				{
					Print($"[{label}] 已落在触发点附近（{num:F1} 亚尔，落稳），容错放行自爆");
					StopNav();
					then();
				}
				else if (--left <= 0)
				{
					giveUp?.Invoke();
				}
				else
				{
					Sched(Tick, TimeSpan.FromMilliseconds(150L));
				}
			}
			else if (earlyYalms > 0f && num <= earlyYalms)
			{
				Print($"还差 {num:F0} 亚尔，快到触发点：停止寻路，下坐骑做触发动作");
				StopNav();
				Sched(then, TimeSpan.FromMilliseconds(500L));
			}
			else
			{
				if (flag && still >= 20)
				{
					if (num <= 12f)
					{
						Print($"已在点上停稳（{num:F0} 亚尔），动手");
						then();
						return;
					}
					if (num <= 25f)
					{
						Print($"还差 {num:F0} 亚尔停稳——够近了，直接算到点");
						then();
						return;
					}
					still = 0;
					if (retries >= 2)
					{
						Print($"这个点没能飞到（还差 {num:F0} 亚尔），跳过");
						giveUp?.Invoke();
						return;
					}
					retries++;
					Print($"寻路卡住了（还差 {num:F0} 亚尔），继续飞，不下坐骑");
					Say("/vnav flyflag");
				}
				if (--left <= 0)
				{
					giveUp?.Invoke();
				}
				else
				{
					Sched(Tick, TimeSpan.FromMilliseconds(150L));
				}
			}
		}
	}

	private (float X, float Y) MapPos(uint mapId)
	{
		try
		{
			IPlayerCharacter localPlayer = objectTable.LocalPlayer;
			ExcelSheet<Lumina.Excel.Sheets.Map> excelSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
			if (localPlayer == null || excelSheet == null)
			{
				return (X: 0f, Y: 0f);
			}
			Lumina.Excel.Sheets.Map row = excelSheet.GetRow(mapId);
			if (row.RowId == 0)
			{
				return (X: 0f, Y: 0f);
			}
			return (X: MapUtil.ConvertWorldCoordXZToMapCoord(localPlayer.Position.X, row.SizeFactor, row.OffsetX), Y: MapUtil.ConvertWorldCoordXZToMapCoord(localPlayer.Position.Z, row.SizeFactor, row.OffsetY));
		}
		catch
		{
			return (X: 0f, Y: 0f);
		}
	}

	private GotoResult GotoAetheryte(string map, string world, string huntCn = "")
	{
		CaptureWindowStateBeforeTeleport();
		StopAll(quiet: true);
		Resume();
		AetheryteHit? ae = NearestAetheryte(map, 0f, 0f, huntCn);
		if(!ae.HasValue)return new GotoResult(false,"在「"+map+"」找不到已解锁的大水晶");
		travelPreparationPending = true;
		bool flag = !string.IsNullOrWhiteSpace(world) && !string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase);
		if (flag)
		{
			SendWorldTravel(world);
			WaitUntilOr(() => !condition[ConditionFlag.BetweenAreas] && string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase), Teleport, () =>
			{
				AbortTravel("跨服超时，未到达 " + world);
			}, 480);
		}
		else
		{
			if (!string.IsNullOrWhiteSpace(world))
			{
				Print("已在目标服务器 " + world + "，直接传大水晶");
			}
			Sched(Teleport, TimeSpan.FromMilliseconds(250L));
		}
		return new GotoResult(true, "已发起前往" + (flag ? " " + world : "") + " · " + map + "（" + ae.Value.Name + "），确认落地后再执行后续动作");
		void Arrived()
		{
			WaitCorrectWorldThen(world, huntCn, () =>
			{
				RunPrepAtDestination(huntCn, world, map, () =>
				{
					if (huntCn == "牛头黑神")
					{
						int limit = aetheryteBombLimit;
						int done = 0;
						System.Action loop = null;
						loop = () =>
						{
							if (limit > 0 && done >= limit)
							{
								autoRouteDoneAt = DateTime.UtcNow;
								Print($"[牛头黑神] 已自爆 {limit} 次没出怪，先停手（概率触发，可以稍后重试）");
							}
							else
							{
								done++;
								SelfDestructThen("牛头黑神", "大水晶", map, loop, loop);
							}
						};
						loop();
					}
				});
			});
		}
		void Teleport()
		{
			TeleportConfirmed(ae.Value,map,world,Arrived);
		}
	}

	private bool WorldReady(string world)
	{
		if (string.IsNullOrWhiteSpace(world))
		{
			return true;
		}
		try
		{
			return !IsZoning() && objectTable.LocalPlayer != null && string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private void WaitCorrectWorldThen(string world, string huntCn, System.Action then)
	{
		if (string.IsNullOrWhiteSpace(world) || WorldReady(world))
		{
			then();
			return;
		}
		Print($"[{huntCn}] 当前还不是 {world}（现在是 {CurrentWorldName()}），等落地确认后再开始触发");
		WaitUntilOr(() => WorldReady(world), () =>
		{
			Print($"[{huntCn}] 已确认在 {world}，开始触发");
			then();
		}, () =>
		{
			Print($"[{huntCn}] 90 秒没确认到 {world}，这只跳过（不在错的服上触发）");
			autoSkipped.Add((huntCn, "跨服没确认", "90 秒没确认到 " + world));
			AutoStep();
		}, 180);
	}

	private static string PrepHint(string huntCn)
	{
		if (HuntPreps.TryGetValue(huntCn, out var value))
		{
			return value.Kind switch
			{
				PrepKind.Cook => " · 落地切烹调师",
				PrepKind.Blu => " · 落地切青魔法师",
				PrepKind.Garden => " · 落地切园艺工·自动采集",
				PrepKind.Warrior => " · 落地切战士",
				_ => " · 落地召" + value.Arg,
			};
		}
		return "";
	}

	private void DismountThen(System.Action then)
	{
		if (!Mounted())
		{
			then();
			return;
		}
		Say("/gaction 随机坐骑");
		int tries = 0;
		Sched(Tick, TimeSpan.FromMilliseconds(150L));
		void Tick()
		{
			if (!Mounted())
			{
				then();
			}
			else
			{
				tries++;
				if (tries == 6)
				{
					Say("/mount");
				}
				else if (tries == 12)
				{
					Say("/gaction 随机坐骑");
				}
				else if (tries >= 20)
				{
					Print("[坐骑] 下坐骑没成功，先继续（还骑着的话技能会因为坐骑状态放不出来）");
					then();
					return;
				}
				Sched(Tick, TimeSpan.FromMilliseconds(150L));
			}
		}
	}

	private void RunPrepThen(string huntCn, System.Action then)
	{
		if (string.IsNullOrEmpty(huntCn) || !HuntPreps.TryGetValue(huntCn, out var prep))
		{
			then();
			return;
		}
		DismountThen(() =>
		{
			switch (prep.Kind)
			{
			case PrepKind.Cook:
				Print("[" + huntCn + "] 切烹调师：触发要制作" + prep.Arg);
				SwitchJobThen(15u, "烹调师", () =>
				{
					CraftStartThen(prep.Arg, "[" + huntCn + "]", () =>
					{
						System.Action cont = then;
						int rounds = 4;
						bool logged = false;
						Sched(CloseCraft, TimeSpan.FromMilliseconds(600L));
						void CloseCraft()
						{
							if ((CloseAddonIfOpen("Synthesis") | CloseAddonIfOpen("SynthesisSimple") | CloseAddonIfOpen("RecipeNote")) && !logged)
							{
								logged = true;
								Print("[" + huntCn + "] 已自动退出制作菜单");
							}
							if (--rounds > 0)
							{
								Sched(CloseCraft, TimeSpan.FromMilliseconds(400L));
							}
							else
							{
								autoRouteDoneAt = DateTime.UtcNow;
								cont();
							}
						}
					});
				});
				break;
			case PrepKind.Blu:
				Print("[" + huntCn + "] 切青魔法师：触发要用" + prep.Arg);
				SwitchJobThen(36u, "青魔法师", then);
				break;
			case PrepKind.Garden:
				Print("[" + huntCn + "] 切园艺工：交给 GatherBuddy 自动采「矮人棉」");
				if (huntCn == "得到宽恕的炫学")
				{
					SwitchJobThen(17u, "园艺工", () =>
					{
						GatherStrikesThen("矮人棉", 300, "[" + huntCn + "]", then);
					});
				}
				else
				{
					SwitchJobThen(17u, "园艺工", then);
				}
				break;
			case PrepKind.Warrior:
				Print("[" + huntCn + "] 切战士：触发要用战士");
				SwitchJobThen(21u, "战士", then);
				break;
			default:
				PetSummonThen(huntCn, prep.Arg, then);
				break;
			}
		});
	}

	private uint CurrentJobId()
	{
		try
		{
			return objectTable.LocalPlayer?.ClassJob.RowId ?? 0;
		}
		catch
		{
			return 0u;
		}
	}

	private void SwitchJobThen(uint jobId, string label, System.Action then)
	{
		if (CurrentJobId() == jobId)
		{
			then();
			return;
		}
		int num = FindGearsetIndex(jobId);
		string byIndex = ((num >= 0) ? ("/gs change " + (num + 1)) : null);
		string byName = "/gs change " + label;
		string first = byIndex ?? byName;
		Say(first);
		log.Info($"[SrankFlag] 触发前置：{first}（目标 {label}，查到套装 " + ((num >= 0) ? $"#{num + 1}" : "未找到") + "）");
		WaitUntilOr(() => CurrentJobId() == jobId, then, () =>
		{
			if (byIndex == null)
			{
				AutoPrepFail(label, first, then);
			}
			else
			{
				Say(byName);
				log.Info("[SrankFlag] 触发前置：序号没切成，改按名字 " + byName);
				WaitUntilOr(() => CurrentJobId() == jobId, then, () =>
				{
					AutoPrepFail(label, first + " → " + byName, then);
				}, 12, 250);
			}
		}, 20, 250);
	}

	private void AutoPrepFail(string label, string cmds, System.Action then)
	{
		string text = $"[{label}] 换装失败（已试 {cmds}）";
		if (autoRunning || autoPaused)
		{
			autoPrepFailed = true;
			Print(text + "，判定触发失败，去下一只");
		}
		else
		{
			Print(text + "；手动模式仍继续触发动作");
			then();
		}
	}

	private unsafe int FindGearsetIndex(uint jobId)
	{
		try
		{
			RaptureGearsetModule* ptr = RaptureGearsetModule.Instance();
			if (ptr == null)
			{
				return -1;
			}
			for (int i = 0; i < 100; i++)
			{
				RaptureGearsetModule.GearsetEntry* gearset = ptr->GetGearset(i);
				if (gearset != null && gearset->ClassJob == jobId)
				{
					return i;
				}
			}
		}
		catch
		{
		}
		return -1;
	}

	private unsafe void PetSummonThen(string huntCn, string petName, System.Action then)
	{
		try
		{
			uint num = FindMinionId(petName);
			if (num == 0)
			{
				Print($"[{huntCn}] 找不到宠物「{petName}」，跳过召唤");
				then();
				return;
			}
			UIState* ptr = UIState.Instance();
			if (ptr == null || !ptr->IsCompanionUnlocked(num))
			{
				Print($"[{huntCn}] 宠物「{petName}」还没解锁，跳过召唤");
				then();
				return;
			}
			ActionManager* ptr2 = ActionManager.Instance();
			if (ptr2 == null)
			{
				then();
				return;
			}
			if (ptr2->GetActionStatus(ActionType.Companion, num, 3758096384uL, checkRecastActive: true, checkCastingActive: true, null) != 0)
			{
				Print($"[{huntCn}] 宠物「{petName}」现在召不出来（战斗中或冷却）");
				then();
				return;
			}
			ptr2->UseAction(ActionType.Companion, num, 3758096384uL, 0u, ActionManager.UseActionMode.None, 0u, null);
			Print($"[{huntCn}] 已召唤宠物「{petName}」");
			log.Info($"[SrankFlag] 触发前置：召唤宠物 {petName}(#{num})");
		}
		catch
		{
		}
		Sched(then, TimeSpan.FromMilliseconds(1000L));
	}

	private uint FindMinionId(string name)
	{
		try
		{
			ExcelSheet<Companion> excelSheet = dataManager.GetExcelSheet<Companion>();
			if (excelSheet == null)
			{
				return 0u;
			}
			string text = Normalize(name);
			foreach (Companion item in excelSheet)
			{
				if (Normalize(item.Singular.ToString()) == text)
				{
					return item.RowId;
				}
			}
		}
		catch
		{
		}
		return 0u;
	}

	private void WaitUntil(Func<bool> cond, System.Action then, int tries = 120, int intervalMs = 500)
	{
		Step(tries);
		void Step(int left)
		{
			Sched(() =>
			{
				try
				{
					if (cond())
					{
						then();
						return;
					}
				}
				catch
				{
				}
				if (left > 0)
				{
					Step(left - 1);
				}
			}, TimeSpan.FromMilliseconds(intervalMs));
		}
	}

	private void WaitUntilOr(Func<bool> cond, System.Action then, System.Action? giveUp, int tries = 120, int intervalMs = 500)
	{
		Step(tries);
		void Step(int left)
		{
			Sched(() =>
			{
				try
				{
					if (cond())
					{
						then();
						return;
					}
				}
				catch
				{
				}
				if (left > 0)
				{
					Step(left - 1);
				}
				else
				{
					giveUp?.Invoke();
				}
			}, TimeSpan.FromMilliseconds(intervalMs));
		}
	}

	private void CastThen(string cmd, Func<bool> took, System.Action after, string dbg)
	{
		int tries = 0;
		Step();
		void Step()
		{
			Print($"[触发] 尝试释放{dbg}（第 {tries + 1} 次）");
			Say(cmd);
			Sched(() =>
			{
				try
				{
					if (took())
					{
						after();
						return;
					}
				}
				catch
				{
				}
				if (++tries >= 27)
				{
					Print("[触发] " + dbg + " 3 秒内未确认生效，继续（交给死亡监控兜底）");
					after();
				}
				else
				{
					Step();
				}
			}, TimeSpan.FromMilliseconds(110L));
		}
	}

	private bool Mounted()
	{
		try
		{
			return condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion];
		}
		catch
		{
			return false;
		}
	}

	private bool Mounting()
	{
		try
		{
			return condition[ConditionFlag.Mounting] || condition[ConditionFlag.Mounting71] || condition[ConditionFlag.MountOrOrnamentTransition];
		}
		catch
		{
			return false;
		}
	}

	private bool CanMountNow()
	{
		try
		{
			if (Mounted() || Mounting())
			{
				return false;
			}
			if (condition[ConditionFlag.BetweenAreas])
			{
				return false;
			}
			if (condition[ConditionFlag.OccupiedInCutSceneEvent])
			{
				return false;
			}
			if (condition[ConditionFlag.WatchingCutscene])
			{
				return false;
			}
			if (condition[ConditionFlag.InCombat])
			{
				return false;
			}
			return true;
		}
		catch
		{
			return true;
		}
	}

	private void MountUpThen(System.Action then)
	{
		Resume();
		if (Mounted())
		{
			then();
			return;
		}
		int elapsed = 0;
		int nextSend = 0;
		int sent = 0;
		Sched(Tick, TimeSpan.FromMilliseconds(200L));
		void Tick()
		{
			try
			{
				if (Mounted())
				{
					Print("[坐骑] 已上坐骑，继续寻路");
					Sched(then, TimeSpan.FromMilliseconds(500L));
				}
				else if (elapsed >= 12000)
				{
					Print("[坐骑] 12 秒没召唤成功（采集硬直 / 副本 / 剧情 / 战斗中都骑不上），改用 /vnav moveflag 地面寻路");
					then();
				}
				else
				{
					if (elapsed >= nextSend && CanMountNow())
					{
						sent++;
						string text = ((sent < 3) ? "/gaction 随机坐骑" : "/mount 专属陆行鸟");
						Say(text);
						Print((sent == 1) ? ("[坐骑] " + text) : $"[坐骑] 还没骑上，重试第 {sent} 次（{text}）");
						nextSend = elapsed + 3500;
					}
					elapsed += 300;
					Sched(Tick, TimeSpan.FromMilliseconds(300L));
				}
			}
			catch
			{
				then();
			}
		}
	}

	private bool InMap(string map)
	{
		try
		{
			if (condition[ConditionFlag.BetweenAreas])
			{
				return false;
			}
			return Normalize(TerritoryName(clientState.TerritoryType)) == Normalize(map);
		}
		catch
		{
			return false;
		}
	}

	private FlagResult TeleportNearest(string map, float x, float y)
	{
		StopAll(quiet:true);
		Resume();
		AetheryteHit? aetheryteHit = NearestAetheryte(map, x, y);
		if (aetheryteHit.HasValue)
		{
			AetheryteHit valueOrDefault = aetheryteHit.GetValueOrDefault();
			bool accepted=TryNativeTeleport(valueOrDefault,out string reason);
			return new FlagResult(accepted,accepted?"已发起原生传送："+valueOrDefault.Name:reason);
		}
		return new FlagResult(Ok: false, "在「" + map + "」找不到大水晶");
	}

	private FlagResult WorldTravel(string world)
	{
		if (string.IsNullOrWhiteSpace(world))
		{
			return new FlagResult(Ok: false, "服务器名不能为空");
		}
		if (string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase))
		{
			return new FlagResult(Ok: true, "已经在 " + world + "，无需跨服");
		}
		SendWorldTravel(world);
		return new FlagResult(Ok: true, "已发送跨服传送：" + world);
	}

	private void SendWorldTravel(string world)
	{
		if (!string.IsNullOrWhiteSpace(world))
		{
			Say("/pdr worldtravel " + world);
		}
	}

	private FlagResult ChangeInstance(int line)
	{
		if (line <= 0)
		{
			return new FlagResult(Ok: false, "分线编号要 > 0");
		}
		Say("/pdr insc " + line);
		return new FlagResult(Ok: true, "已发送切分线：" + line + " 线");
	}

	private AetheryteHit? NearestAetheryte(string mapName, float x, float y, string huntCn = "")
	{
		ExcelSheet<Lumina.Excel.Sheets.Map> excelSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
		if (excelSheet == null)
		{
			return null;
		}
		uint num = 0u;
		foreach (Lumina.Excel.Sheets.Map item in excelSheet)
		{
			if (!(Normalize(SafeMapName(item)) != Normalize(mapName)))
			{
				num = item.RowId;
				break;
			}
		}
		if (num == 0)
		{
			return null;
		}
		ExcelSheet<Lumina.Excel.Sheets.Aetheryte> excelSheet2 = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
		if (excelSheet2 == null)
		{
			return null;
		}
		string text = ((huntCn.Length > 0 && HuntAetheryte.TryGetValue(huntCn, out string value)) ? value : null);
		string[] array = ((text == null) ? Array.Empty<string>() : (from text3 in text.Split('|').Select(Normalize)
			where text3.Length > 0
			select text3).ToArray());
		AetheryteHit? result = null;
		foreach (Lumina.Excel.Sheets.Aetheryte item2 in excelSheet2)
		{
			if (item2.Map.RowId != num || !item2.IsAetheryte || !AetheryteUnlocked(item2.RowId))
			{
				continue;
			}
			string text2 = SafePlaceNameFrom(item2);
			if (!string.IsNullOrEmpty(text2))
			{
				AetheryteHit aetheryteHit = new AetheryteHit(text2, x, y, 0.0, item2.RowId, item2.Territory.RowId);
				if (array.Length == 0)
				{
					return aetheryteHit;
				}
				string n = Normalize(text2);
				if (array.Any((string w) => n.Contains(w, StringComparison.Ordinal)))
				{
					return aetheryteHit;
				}
				AetheryteHit valueOrDefault = result.GetValueOrDefault();
				if (!result.HasValue)
				{
					valueOrDefault = aetheryteHit;
					result = valueOrDefault;
				}
			}
		}
		return result;
	}

	private unsafe DiscardResult DiscardTrigger()
	{
		uint territoryType = clientState.TerritoryType;
		if (!TriggerDiscardItems.TryGetValue(territoryType, out (uint, int) value))
		{
			Print($"当前地图（territory {territoryType}）没有需要自动丢弃的触发道具。");
			return new DiscardResult(Ok: false, $"当前地图不支持自动丢弃（territory {territoryType}）");
		}
		if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
		{
			return new DiscardResult(Ok: false, "正在过图，等落地再试");
		}
		InventoryManager* ptr = InventoryManager.Instance();
		if (ptr == null)
		{
			return new DiscardResult(Ok: false, "背包系统未就绪");
		}
		uint item = value.Item1;
		int amount = value.Item2;
		(InventoryType, short, int) tuple = BuildStacks(ptr, item).FirstOrDefault(((InventoryType Type, short Slot, int Quantity) s) => s.Quantity >= amount);
		if (tuple.Item3 < amount)
		{
			Print($"背包里「{item}」不够 {amount} 个，无法按组丢弃。");
			return new DiscardResult(Ok: false, $"数量不足（需要 {amount} 个）");
		}
		if (tuple.Item3 > amount)
		{
			if (ptr->SplitItem(tuple.Item1, (ushort)tuple.Item2, amount) != 0)
			{
				Print("拆分失败（背包可能满了）。");
				return new DiscardResult(Ok: false, "拆分失败（背包可能满了）");
			}
			(InventoryType, short, int) tuple2 = BuildStacks(ptr, item).FirstOrDefault(((InventoryType Type, short Slot, int Quantity) s) => s.Quantity == amount);
			if (tuple2.Item3 != amount)
			{
				return new DiscardResult(Ok: false, "拆分后没找到整组，请手动检查背包");
			}
			tuple = tuple2;
		}
		if (ptr->DiscardItem(tuple.Item1, (ushort)tuple.Item2) != 0)
		{
			return new DiscardResult(Ok: false, "丢弃失败");
		}
		Print($"已整组丢弃「{item}」×{amount}。");
		return new DiscardResult(Ok: true, $"已整组丢弃 ×{amount}");
	}

	private unsafe List<(InventoryType Type, short Slot, int Quantity)> BuildStacks(InventoryManager* mgr, uint itemId)
	{
		List<(InventoryType, short, int)> list = new List<(InventoryType, short, int)>();
		InventoryType[] array = new InventoryType[4]
		{
			InventoryType.Inventory1,
			InventoryType.Inventory2,
			InventoryType.Inventory3,
			InventoryType.Inventory4
		};
		foreach (InventoryType inventoryType in array)
		{
			InventoryContainer* inventoryContainer = mgr->GetInventoryContainer(inventoryType);
			if (inventoryContainer == null)
			{
				continue;
			}
			for (int j = 0; j < inventoryContainer->Size; j++)
			{
				ref InventoryItem reference = ref inventoryContainer->Items[j];
				if (reference.ItemId == itemId)
				{
					list.Add((reference.GetInventoryType(), reference.Slot, reference.Quantity));
				}
			}
		}
		return list;
	}

	private WeeEaResult QueryWeeEa()
	{
		uint territoryType = clientState.TerritoryType;
		if (territoryType != 960)
		{
			return new WeeEaResult(Ok: false, $"当前不在小异亚地图（territory {territoryType}，需在 960）", new List<string>());
		}
		List<string> list = new List<string>();
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer == null)
		{
			return new WeeEaResult(Ok: false, "找不到本地玩家", list);
		}
		Vector3 position = localPlayer.Position;
		foreach (IGameObject obj in objectTable)
		{
			if (obj != null && obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion && !(obj.Name.TextValue != "小异亚"))
			{
				float num = obj.Position.X - position.X;
				float num2 = obj.Position.Z - position.Z;
				if (!(Math.Sqrt(num * num + num2 * num2) > 10.0) && objectTable.FirstOrDefault((IGameObject o) => o is IPlayerCharacter playerCharacter2 && playerCharacter2.EntityId == obj.OwnerId) is IPlayerCharacter playerCharacter)
				{
					list.Add($"{playerCharacter.Name.TextValue}@{playerCharacter.HomeWorld.Value.Name}");
				}
			}
		}
		return new WeeEaResult(Ok: true, $"找到 {list.Count} 个小异亚", list);
	}

	private FlagResult NavTo(string mapName, float x, float y, string world = "", string huntCn = "")
	{
		Resume();
		FlagResult result = Flag(mapName, x, y);
		if (!result.Ok)
		{
			return result;
		}
		bool flag = false;
		if (!string.IsNullOrWhiteSpace(world))
		{
			if (!string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase))
			{
				SendWorldTravel(world);
				flag = true;
				Print("跨服目标 " + world + "，落地后自动寻路。");
			}
			else
			{
				Print("已在 " + world + "，无需跨服。");
			}
		}
		if (flag)
		{
			WaitUntil(() => !condition[ConditionFlag.BetweenAreas] && string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase), StartNav, 240);
		}
		else
		{
			Sched(StartNav, TimeSpan.FromMilliseconds(1200L));
		}
		return new FlagResult(Ok: true, result.Message + "，" + (flag ? "跨服落地后" : "马上") + "上坐骑 + 自动飞行寻路（/vnav flyflag）", result.TerritoryId, result.MapId);
		void StartNav()
		{
			RunPrepAtDestination(huntCn, world, mapName, () =>
			{
				FlyFlagThen(() =>
				{
				});
			});
		}
	}

	private void StopNav()
	{
		Say("/vnav stop");
	}

	private bool VnavReady()
	{
		try
		{
			return pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady").InvokeFunc();
		}
		catch
		{
			return true;
		}
	}

	private float VnavBuildProgress()
	{
		try
		{
			return pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress").InvokeFunc();
		}
		catch
		{
			return 1f;
		}
	}

	private void WaitNavReadyThen(System.Action then, double timeoutSec = 120.0)
	{
		DateTime start = DateTime.UtcNow;
		System.Action poll = null;
		poll = () =>
		{
			if (VnavReady())
			{
				then();
			}
			else
			{
				double totalSeconds = (DateTime.UtcNow - start).TotalSeconds;
				if (totalSeconds >= timeoutSec)
				{
					Print("[寻路] 等 vnavmesh 加载超过 " + (int)timeoutSec + " 秒仍未就绪，仍尝试寻路（若一直不动请手动 /vnav reload）");
					then();
				}
				else
				{
					if ((int)totalSeconds % 5 == 0)
					{
						float num = VnavBuildProgress();
						Print("[寻路] 等待 vnavmesh 加载当前地图寻路网格…（" + ((num > 0f) ? ((num * 100f).ToString("F0") + "%") : "加载中") + "）");
					}
					Sched(poll, TimeSpan.FromMilliseconds(1000L));
				}
			}
		};
		poll();
	}

	private void FlyFlagThen(System.Action after)
	{
		WaitNavReadyThen(() =>
		{
			MountUpThen(() =>
			{
				if (Mounted())
				{
					Say("/vnav flyflag");
				}
				else
				{
					Print("[寻路] 没坐骑，改用地面寻路 /vnav moveflag");
					Say("/vnav moveflag");
				}
				after();
			});
		});
	}

	private void ClientState_OnLogin()
	{
		RefreshUnlockedMinions();
	}

	private unsafe void RefreshUnlockedMinions()
	{
		try
		{
			if (!clientState.IsLoggedIn)
			{
				return;
			}
			unlockedMinions.Clear();
			foreach (KeyValuePair<uint, uint> item in MinionMap)
			{
				if (UIState.Instance()->IsCompanionUnlocked(item.Value))
				{
					unlockedMinions.Add(item.Value);
				}
			}
		}
		catch
		{
		}
	}

	private void Framework_OnUpdate(IFramework framework)
	{
		try
		{
			TickTeleportWindowRestore();
		}
		catch
		{
		}
		try
		{
			if (autoSummonMinion)
			{
				TryAutoSummon();
			}
			if (autoLeaveDuty)
			{
				TryAutoLeave();
			}
			TryAutoConfirmReturn();
			MaybeStartBearFetch(force: false);
			MaybeStartPointsFetch(force: false);
			MaybeRefreshRestart(force: false);
			TickTriggeredGuard();
			TickTriggeredSweep();
			TickNativeHud();
		}
		catch
		{
		}
	}

	private void ApplyMainWindowNow()
	{
		try
		{
			nativeHud?.ReviveAfterError();
			nativeHud?.SetMainWindowOpen(mainWindowOpen);
		}
		catch (Exception exception)
		{
			try
			{
				log.Warning(exception, "[魔法水桶] 主窗状态同步失败");
			}
			catch
			{
			}
		}
	}

	private void ApplyHistoryWindowNow()
	{
		try
		{
			nativeHud?.ReviveAfterError();
			nativeHud?.SetHistoryWindowOpen(historyWindowOpen);
			nativeHud?.ForceHistoryRefresh();
		}
		catch (Exception exception)
		{
			try
			{
				log.Warning(exception, "[魔法水桶] 历史窗口状态同步失败");
			}
			catch
			{
			}
		}
	}

	private void CaptureWindowStateBeforeTeleport()
	{
		if (teleportSnapshotPending)
		{
			teleportSnapshotAtUtc = DateTime.UtcNow;
			return;
		}
		if (!stableWindowValid)
		{
			RecordStableWindowState();
		}
		bool flag = false;
		try
		{
			flag = condition[ConditionFlag.BetweenAreas];
		}
		catch
		{
		}
		if (!flag)
		{
			RecordStableWindowState();
		}
		teleportSnapshotMain = stableWindowMain;
		teleportSnapshotHud = stableWindowHud;
		teleportSnapshotAuto = stableWindowAuto;
		teleportSnapshotSettings = stableWindowSettings;
		teleportSnapshotHistory = stableWindowHistory;
        teleportSnapshotConditions = stableWindowConditions;
		teleportSnapshotPending = true;
		teleportSnapshotAtUtc = DateTime.UtcNow;
		restoreAfterTeleportUtc = DateTime.MinValue;
	}

	private void RecordStableWindowState()
	{
		stableWindowMain = mainWindowOpen;
		stableWindowHud = autoHudOpen;
		stableWindowAuto = autoRunOpen;
		stableWindowSettings = settingsWindowOpen;
		stableWindowHistory = historyWindowOpen;
        stableWindowConditions = conditionWindowOpen;
		stableWindowValid = true;
	}

	private void SyncSnapshotFromStable()
	{
		if (teleportSnapshotPending)
		{
			teleportSnapshotMain = stableWindowMain;
			teleportSnapshotHud = stableWindowHud;
			teleportSnapshotAuto = stableWindowAuto;
			teleportSnapshotSettings = stableWindowSettings;
			teleportSnapshotHistory = stableWindowHistory;
        teleportSnapshotConditions = stableWindowConditions;
		}
	}

	private void NoteUserWindowChange()
	{
		RecordStableWindowState();
		SyncSnapshotFromStable();
	}

	private void TickTeleportWindowRestore()
	{
		bool flag;
		try
		{
			flag = condition[ConditionFlag.BetweenAreas];
		}
		catch
		{
			return;
		}
		bool flag2;
		try
		{
			flag2 = clientState.IsLoggedIn && objectTable.LocalPlayer != null;
		}
		catch
		{
			return;
		}
		if (flag)
		{
			if (!teleportSnapshotPending)
			{
				CaptureWindowStateBeforeTeleport();
			}
			teleportSnapshotAtUtc = DateTime.UtcNow;
			wasBetweenAreas = true;
			restoreAfterTeleportUtc = DateTime.MinValue;
			return;
		}
		if (flag2)
		{
			RecordStableWindowState();
			if (teleportSnapshotPending && restoreAfterTeleportUtc == DateTime.MinValue)
			{
				SyncSnapshotFromStable();
			}
		}
		bool flag3 = wasBetweenAreas;
		wasBetweenAreas = false;
		if (!teleportSnapshotPending)
		{
			return;
		}
		if ((DateTime.UtcNow - teleportSnapshotAtUtc).TotalSeconds > 90.0)
		{
			teleportSnapshotPending = false;
			restoreAfterTeleportUtc = DateTime.MinValue;
		}
		else
		{
			if (!flag2)
			{
				return;
			}
			if (restoreAfterTeleportUtc == DateTime.MinValue)
			{
				int num = (flag3 ? 500 : 2000);
				restoreAfterTeleportUtc = DateTime.UtcNow.AddMilliseconds(num);
			}
			if (DateTime.UtcNow < restoreAfterTeleportUtc)
			{
				return;
			}
			restoreAfterTeleportUtc = DateTime.MinValue;
			teleportSnapshotPending = false;
			mainWindowOpen = teleportSnapshotMain;
			autoHudOpen = teleportSnapshotHud;
			autoRunOpen = teleportSnapshotAuto;
			settingsWindowOpen = teleportSnapshotSettings;
			historyWindowOpen = teleportSnapshotHistory;
            conditionWindowOpen = teleportSnapshotConditions;
			RecordStableWindowState();
			try
			{
				nativeHud?.RestoreAfterTeleport(teleportSnapshotMain, teleportSnapshotHud, teleportSnapshotAuto, teleportSnapshotSettings, teleportSnapshotHistory, teleportSnapshotConditions);
			}
			catch (Exception exception)
			{
				try
				{
					log.Warning(exception, "[魔法水桶] 落地窗口恢复失败");
				}
				catch
				{
				}
			}
		}
	}

	private void InitNativeHud()
	{
		try
		{
			nativeHud = new NativeHudRuntime(BuildHudActions(), BuildAutoWindowActions(), BuildMainActions(), BuildSettingsActions(), BuildHistoryActions(), BuildConditionActions(), (string msg) =>
			{
				log.Info("[魔法水桶] " + msg);
			});
			nativeHud.BeginInit(pi);
		}
		catch (Exception exception)
		{
			nativeHud = null;
			try
			{
				log.Error(exception, "[魔法水桶] 原生窗口初始化失败，回退 ImGui");
			}
			catch
			{
			}
		}
	}

	private void TickNativeHud()
	{
		TickConditionUi();
		NativeHudRuntime nativeHudRuntime = nativeHud;
		if (nativeHudRuntime == null)
		{
			return;
		}
		try
		{
			nativeHudRuntime.Tick(autoHudOpen, BuildHudSnapshot());
		}
		catch (Exception exception)
		{
			nativeHud = null;
			try
			{
				log.Error(exception, "[魔法水桶] 原生 HUD 同步异常，回退 ImGui 悬浮窗");
				return;
			}
			catch
			{
				return;
			}
		}
		try
		{
			nativeHudRuntime.TickAuto(autoRunOpen, BuildAutoWindowSnapshot());
		}
		catch (Exception exception2)
		{
			try
			{
				log.Error(exception2, "[魔法水桶] 自动前往原生窗口同步异常，本帧跳过");
			}
			catch
			{
			}
		}
		try
		{
			bool wantOpen = mainWindowOpen;
			nativeHudRuntime.TickMain(wantOpen, BuildMainSnapshot());
		}
		catch (Exception exception3)
		{
			try
			{
				log.Error(exception3, "[魔法水桶] 主窗口原生同步异常，本帧跳过");
			}
			catch
			{
			}
		}
		try
		{
			nativeHudRuntime.TickSettings(settingsWindowOpen, BuildSettingsSnapshot());
		}
		catch (Exception exception4)
		{
			try
			{
				log.Error(exception4, "[魔法水桶] 设置面板原生同步异常，本帧跳过");
			}
			catch
			{
			}
		}
		try
		{
			nativeHudRuntime.TickHistory(historyWindowOpen, BuildHistorySnapshot());
		}
		catch (Exception exception5)
		{
			try
			{
				log.Error(exception5, "[魔法水桶] 历史窗口原生同步异常，本帧跳过");
			}
			catch
			{
			}
		}
	}

	private SettingsActions BuildSettingsActions()
	{
		return new SettingsActions
		{
			Toggle = (string key) =>
			{
				switch (key)
				{
				case "main":
					mainWindowOpen = !mainWindowOpen;
					ApplyMainWindowNow();
					break;
				case "auto":
					autoRunOpen = !autoRunOpen;
					nativeHud?.SetAutoWindowOpen(autoRunOpen);
					break;
				case "hud":
					autoHudOpen = !autoHudOpen;
					nativeHud?.SetHudWindowOpen(autoHudOpen);
					break;
				case "conditions":
					ToggleConditions();
					break;
				case "summon":
					autoSummonMinion = !autoSummonMinion;
					if (autoSummonMinion)
					{
						RefreshUnlockedMinions();
					}
					SaveAutoSettings();
					break;
				case "leave":
					autoLeaveDuty = !autoLeaveDuty;
					SaveAutoSettings();
					break;
				case "return":
					autoConfirmReturn = !autoConfirmReturn;
					SaveAutoSettings();
					break;
				case "theme":
					SetLightTheme(!lightMode);
					break;
				}
				nativeHud?.ForceSettingsRefresh();
				nativeHud?.ForceMainRefresh();
			},
			Refresh = () =>
			{
				if (srankData?.data == null)
				{
					LoadSrankData();
				}
				MaybeStartBearFetch(force: true,notify:true);
				MaybeStartPointsFetch(force: true);
				MaybeRefreshRestart(force: true);
				nativeHud?.ForceMainRefresh();
			},
			Closed = () =>
			{
				settingsWindowOpen = false;
				nativeHud?.SetSettingsWindowOpen(open: false);
			}
		};
	}

	private SettingsSnapshot BuildSettingsSnapshot()
	{
		return new SettingsSnapshot
		{
			ShowMain = mainWindowOpen,
			ShowAuto = autoRunOpen,
			ShowHud = autoHudOpen,
			ShowConditions = conditionWindowOpen,
			AutoSummon = autoSummonMinion,
			AutoLeave = autoLeaveDuty,
			ConfirmReturn = autoConfirmReturn,
			LightTheme = lightMode,
			Status = ((restartAt > 0.0) ? ("维护 T 已同步 · " + currentDc) : ("当前大区 · " + currentDc))
		};
	}

	private HistoryActions BuildHistoryActions()
	{
		return new HistoryActions
		{
			SetFilter = (HistoryFilter f) =>
			{
				historyFilter = f;
				historyPage = 0;
				nativeHud?.ForceHistoryRefresh();
			},
			PageBy = (int delta) =>
			{
				historyPage = Math.Max(0, historyPage + delta);
				nativeHud?.ForceHistoryRefresh();
			},
			Clear = () =>
			{
				history?.Clear();
				historyPage = 0;
				nativeHud?.ForceHistoryRefresh();
				Print("已清空 S 怪历史记录（srank_history.json）");
			},
			Closed = () =>
			{
				historyWindowOpen = false;
				nativeHud?.SetHistoryWindowOpen(open: false);
				NoteUserWindowChange();
			}
		};
	}

	private HistorySnapshot BuildHistorySnapshot()
	{
		SrankHistory srankHistory = history;
		HistorySnapshot historySnapshot = ((srankHistory != null) ? srankHistory.Snapshot(historyFilter, historyPage) : new HistorySnapshot
		{
			Status = "历史库未就绪"
		});
		if (historySnapshot.Page != historyPage)
		{
			historyPage = historySnapshot.Page;
		}
		return historySnapshot;
	}

	private void LoadAutoSettings()
	{
		try
		{
			if (File.Exists(AutoSettingsFile))
			{
				AutoSettings autoSettings = JsonSerializer.Deserialize<AutoSettings>(File.ReadAllText(AutoSettingsFile));
				if (autoSettings != null)
				{
					autoSummonMinion = autoSettings.autoSummonMinion;
					autoLeaveDuty = autoSettings.autoLeaveDuty;
					autoConfirmReturn = autoSettings.autoConfirmReturn;
				}
			}
		}
		catch
		{
		}
	}

	private void SaveAutoSettings()
	{
		try
		{
			AutoSettings value = new AutoSettings
			{
				autoSummonMinion = autoSummonMinion,
				autoLeaveDuty = autoLeaveDuty,
				autoConfirmReturn = autoConfirmReturn
			};
			File.WriteAllText(AutoSettingsFile, JsonSerializer.Serialize(value));
		}
		catch
		{
		}
	}

	private MainActions BuildMainActions()
	{
		return new MainActions
		{
			OpenConditions = ToggleConditions,
			Search = (string t) =>
			{
				searchText = t;
			},
			NextDc = () =>
			{
				List<SnapshotData> list = srankData?.dcs;
				if (list != null && list.Count != 0)
				{
					int num = list.FindIndex((SnapshotData x) => x.dc == currentDc);
					if (num < 0)
					{
						num = 0;
					}
					SelectDc(list[(num + 1) % list.Count].dc);
				}
			},
			ToggleOnlyOpen = () =>
			{
				onlyOpen = !onlyOpen;
				nativeHud?.ForceMainRefresh();
			},
			Refresh = () =>
			{
				if (srankData?.data == null)
				{
					LoadSrankData();
				}
				MaybeStartBearFetch(force: true,notify:true);
				MaybeStartPointsFetch(force: true);
				MaybeRefreshRestart(force: true);
				nativeHud?.ForceMainRefresh();
			},
			OpenAuto = () =>
			{
				autoRunOpen = !autoRunOpen;
			},
			Legacy = () =>
			{
			},
			OpenSettings = () =>
			{
				settingsWindowOpen = !settingsWindowOpen;
				nativeHud?.ForceSettingsRefresh();
			},
			OpenHistory = () =>
			{
				historyWindowOpen = !historyWindowOpen;
				ApplyHistoryWindowNow();
				NoteUserWindowChange();
			},
			Mini = () =>
			{
			},
			StopAll = () =>
			{
				StopAll();
			},
			AllWorlds = () =>
			{
				SnapshotData snapshotData = srankData?.data;
				if (snapshotData != null)
				{
					string[] worlds = snapshotData.worlds;
					foreach (string text in worlds)
					{
						if (text.Length > 0)
						{
							worldFilter.Add(text);
						}
					}
				}
			},
			ClearWorlds = () =>
			{
				worldFilter.Clear();
			},
			CurrentWorldOnly = () =>
			{
				string text = CurrentWorldName();
				worldFilter.Clear();
				if (text.Length > 0)
				{
					worldFilter.Add(text);
				}
			},
			ToggleWorld = (string w) =>
			{
				if (!worldFilter.Remove(w))
				{
					worldFilter.Add(w);
				}
			},
			TogglePatch = (string k) =>
			{
				int count = patchSel.Count;
				if (k == "ALL")
				{
					patchSel.Clear();
				}
				else if (!patchSel.Remove(k))
				{
					patchSel.Add(k);
				}
				try
				{
					log.Info($"[SrankFlag.UI] 左键点击版本: {k}, 当前选中数量: {count} -> {patchSel.Count}");
				}
				catch
				{
				}
			},
			SelectDc = (string dc) =>
			{
				SelectDc(dc);
			},
			SelectPatch = (string k) =>
			{
				if (k == "ALL")
				{
					patchSel.Clear();
				}
				else
				{
					patchSel.Clear();
					patchSel.Add(k);
				}
			},
			Primary = (string en, string w) =>
			{
				MainPrimary(en, w);
			},
			GotoPoint = (string en, string w, float x, float y) =>
			{
				MainGotoPoint(en, w, x, y);
			},
			GotoAetheryte = (string en, string w) =>
			{
				MainGotoAetheryte(en, w);
			},
			Flag = (string m, float x, float y) =>
			{
				Flag(m, x, y);
			},
			FlagAetheryte = (string m, string c) =>
			{
				FlagAetheryte(m, c);
			},
			Expand = (string k) =>
			{
				mainExpandedKey = k ?? "";
			},
			SelectHuntWorld = (string en, string w) =>
			{
				huntWorld[en] = w;
			},
			Closed = () =>
			{
				mainWindowOpen = false;
			}
		};
	}

	private MainSnapshot BuildMainSnapshot()
	{
		MainSnapshot mainSnapshot = new MainSnapshot();
		SnapshotData snapshotData = srankData?.data;
		mainSnapshot.Ready = snapshotData != null;
		mainSnapshot.DataError = ((dataError.Length > 0) ? dataError : "未知原因");
		mainSnapshot.Search = searchText;
		mainSnapshot.OnlyOpen = onlyOpen;
		mainSnapshot.Fetching = bearFetching;
		mainSnapshot.CurrentWorld = CurrentWorldName();
		mainSnapshot.Dc = snapshotData?.dc ?? currentDc;
		mainSnapshot.Status = bearStatus;
		List<SnapshotData> list = srankData?.dcs;
		if (list != null && list.Count > 0)
		{
			foreach (SnapshotData item6 in list)
			{
				mainSnapshot.Dcs.Add(item6.dc);
			}
		}
		mainSnapshot.DcSelected = currentDc;
		mainSnapshot.NeedsManualRefresh = currentDc != "豆豆柴";
		mainSnapshot.BearStatus = "";
		List<SnapshotData> list2 = srankData?.dcs;
		if (list2 != null && list2.Count > 0)
		{
			int num = list2.FindIndex((SnapshotData x) => x.dc == currentDc);
			if (num < 0)
			{
				num = 0;
			}
			mainSnapshot.DcNext = list2[(num + 1) % list2.Count].dc;
		}
		(string, string)[] array = new (string, string)[7]
		{
			("ALL", "全部"),
			("ARR", "2.0"),
			("HW", "3.0"),
			("SB", "4.0"),
			("ShB", "5.0"),
			("EW", "6.0"),
			("DT", "7.0")
		};
		for (int num2 = 0; num2 < array.Length; num2++)
		{
			(string, string) tuple = array[num2];
			mainSnapshot.PatchKeys.Add(tuple.Item1);
			mainSnapshot.PatchLabels.Add(tuple.Item2);
			mainSnapshot.PatchOn.Add((tuple.Item1 == "ALL") ? (patchSel.Count == 0) : patchSel.Contains(tuple.Item1));
		}
		if (snapshotData == null)
		{
			return mainSnapshot;
		}
		foreach (string item7 in snapshotData.worlds.Take(16))
		{
			mainSnapshot.Worlds.Add(item7);
			mainSnapshot.WorldOn.Add(worldFilter.Contains(item7));
		}
		mainSnapshot.WorldMenuLabel = $"服务器 {worldFilter.Count}/{snapshotData.worlds.Length}";
		mainSnapshot.Counts = $"{snapshotData.worlds.Length}服 · {snapshotData.hunts.Length}只 · 已选{worldFilter.Count}";
		long num3 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		Dictionary<string, int> pri = new Dictionary<string, int>
		{
			["open"] = 0,
			["cd"] = 1,
			["over"] = 2,
			["unknown"] = 3
		};
		List<(HuntData, string, string, double, double)> list3 = new List<(HuntData, string, string, double, double)>();
		HuntData[] hunts = snapshotData.hunts;
		foreach (HuntData huntData in hunts)
		{
			if ((patchSel.Count > 0 && !patchSel.Contains(huntData.patch)) || (searchText.Length > 0 && !(huntData.cn + " " + huntData.en + " " + huntData.map).Contains(searchText, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			WorldDeath[] worlds = huntData.worlds;
			foreach (WorldDeath worldDeath in worlds)
			{
				if (worldDeath.lastDeath <= 0.0 || !worldFilter.Contains(worldDeath.world))
				{
					continue;
				}
				double lastDeath = Math.Max(worldDeath.lastDeath, restartAt);
				int num5;
				double num6;
				if (restartAt > 0.0||worldDeath.isMaint)
				{
					num5 = ((restartAt >= worldDeath.lastDeath||worldDeath.isMaint) ? 1 : 0);
					if (num5 != 0 && huntData.mt.Length >= 1)
					{
						num6 = huntData.mt[0];
						goto IL_0542;
					}
				}
				else
				{
					num5 = 0;
				}
				num6 = huntData.cd;
				goto IL_0542;
				IL_0542:
				double cdH = num6;
				double winH = ((num5 != 0 && huntData.mt.Length >= 2) ? huntData.mt[1] : huntData.win);
				(string, double, double) tuple2 = Calc(lastDeath, num3, cdH, winH);
				list3.Add((huntData, worldDeath.world, tuple2.Item1, tuple2.Item2, tuple2.Item3));
			}
		}
		list3.Sort(((HuntData h, string world, string st, double eta, double pct) a, (HuntData h, string world, string st, double eta, double pct) b) =>
		{
			int num8 = pri.GetValueOrDefault(a.st, 3) - pri.GetValueOrDefault(b.st, 3);
			if (num8 != 0)
			{
				return num8;
			}
			num8 = a.eta.CompareTo(b.eta);
			return (num8 == 0) ? string.CompareOrdinal(a.h.cn, b.h.cn) : num8;
		});
		List<(HuntData, string, string, double, double)> list4 = list3.Where(r => HuntWindowDisplay.Includes(r.Item3, onlyOpen)).ToList();
		int value = Enumerable.Count(list3, ((HuntData h, string world, string st, double eta, double pct) r) => r.st == "open");
		mainSnapshot.Counts = $"{worldFilter.Count}服 · {(onlyOpen ? "已开闸" : "未开闸")}{list4.Count}条";
		foreach (var item8 in list4)
		{
			HuntData item = item8.Item1;
			string item2 = item8.Item2;
			string item3 = item8.Item3;
			double item4 = item8.Item4;
			double item5 = item8.Item5;
			string text = item.en + "\u0001" + item2;
			MainHuntRow mainHuntRow = new MainHuntRow
			{
				Key = text,
				En = item.en,
				World = item2,
				Cn = item.cn,
				Map = item.map,
				State = item3,
				Patch = item.patch,
				Pct = ((item3 == "unknown") ? "—" : $"{Math.Round(item5)}%"),
				Eta = ((item3 == "open") ? "可踩" : ((item3 == "cd") ? (Dur(item4) + "后") : "已过窗")),
				BestWorld = item2,
				RowLabel = item.cn,
				HasSpecial = HuntPreps.ContainsKey(item.cn),
				Triggered = IsTriggered(item.en, item2),
				ProgressLabel = Dots(item5, item3) + "  " + ((item3 == "unknown") ? "—" : (Math.Round(item5) + "%")),
				PctValue = item5,
				EtaSeconds = item4,
				Clock = ((item3 == "cd") ? HuntWindowDisplay.Countdown(item4) : ((item3 == "open") ? "00:00" : "--:--"))
			};
			bool flag = DiscardMaps.ContainsKey(item.map);
			if (text == mainExpandedKey)
			{
				mainHuntRow.Expanded = true;
				List<Pt> list5 = GreensFor(item, item2);
				ref string primaryLabel = ref mainHuntRow.PrimaryLabel;
				string text2;
				if (item.cn == "顾尼图")
				{
					text2 = "黑风海触发";
				}
				else if (list5.Count > 0)
				{
					text2 = "前往绿点";
				}
				else
				{
					text2 = (flag ? "前往大水晶+丢弃" : "前往大水晶");
				}
				primaryLabel = text2;
				ref string pointsHint = ref mainHuntRow.PointsHint;
				string text3;
				if (list5.Count > 0)
				{
					text3 = $"可踩绿点 {list5.Count} 个 · {pointsStatus}";
				}
				else
				{
					text3 = (NoPointHunts.Contains(item.cn) ? "触发与点位无关 · 到大水晶等刷新" : "");
				}
				pointsHint = text3;
				bool timed = ConditionForecast.HasSpec(item.en);
				var conditionWindow = timed ? ConditionForecast.NextWindow(item.en, num3) : null;
				var conditionTime = ConditionClock.Evaluate(timed, conditionWindow?.Start, conditionWindow?.End, num3);
				mainHuntRow.Condition = ConditionClock.TimerText(conditionTime);
				mainHuntRow.ConditionPhase = conditionTime.Phase;
				if (conditionWindow.HasValue)
					mainHuntRow.ConditionRange = ConditionClock.RangeText(conditionWindow.Value.Start, conditionWindow.Value.End);
				if (item.tips.Length != 0)
				{
					mainHuntRow.Tips = Trunc(string.Join(" / ", item.tips.Take(2)), 48);
				}
				if (item.cn == "顾尼图")
				{
					mainHuntRow.Points.Add(new MainPointRow
					{
						Label = "▶ 黑风海触发",
						Special = true
					});
				}
				else
				{
					for (int num7 = 0; num7 < list5.Count && num7 < 14; num7++)
					{
						Pt pt = list5[num7];
						mainHuntRow.Points.Add(new MainPointRow
						{
							Label = $"◈ ({pt.X:0.0}, {pt.Y:0.0})",
							X = pt.X,
							Y = pt.Y
						});
					}
					mainHuntRow.Points.Add(new MainPointRow
					{
						Label = "⬡ 大水晶 · " + item.map,
						Aetheryte = true
					});
				}
			}
			mainSnapshot.Hunts.Add(mainHuntRow);
		}
		return mainSnapshot;
		static string ClockText(double sec)
		{
			if (!(sec <= 0.0))
			{
				long num8 = (long)sec;
				long num9 = num8 / 3600;
				long value2 = num8 % 3600 / 60;
				long value3 = num8 % 60;
				if (num9 > 0)
				{
					return $"{num9}:{value2:00}:{value3:00}";
				}
				return $"{value2:00}:{value3:00}";
			}
			return "00:00";
		}
		static string Dots(double pct, string st)
		{
			int num8 = ((!(st == "unknown")) ? Math.Clamp((int)Math.Round(pct / 20.0), 0, 5) : 0);
			return new string('●', num8) + new string('○', 5 - num8);
		}
	}

	private static string Trunc(string t, int max)
	{
		if (t.Length > max)
		{
			return t.Substring(0, max) + "…";
		}
		return t;
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

	private void MainPrimary(string en, string world)
	{
		SnapshotData snapshotData = srankData?.data;
		if (snapshotData == null)
		{
			return;
		}
		HuntData h = snapshotData.hunts.FirstOrDefault((HuntData x) => x.en == en);
		if (h == null)
		{
			return;
		}
		if (IsTriggered(en, world))
		{
			Print($"[{h.cn}] 已被他人触发（{world}），不再前往。击杀播报或主站击杀时间到了才会解锁。");
			nativeHud?.ForceMainRefresh();
			return;
		}
		CaptureWindowStateBeforeTeleport();
		List<Pt> list = GreensFor(h, world);
		bool flag = DiscardMaps.ContainsKey(h.map);
		bool flag2 = list.Count > 0;
		if (h.cn == "顾尼图")
		{
			GounituTrigger(world);
			Print("[" + h.cn + "] 开始自动触发（切战士 → 传黑风海 → 飞触发点 → 水蛭飞斧 → 挑衅冰海天使）");
			return;
		}
		if (flag2)
		{
			Pt pt = list[0];
			GotoResult gotoResult = Goto(h.map, pt.X, pt.Y, world, HuntLineOverride(h.en, world), h.cn, nav: true, list);
			Print("[" + h.cn + "] " + gotoResult.Message);
			return;
		}
		GotoResult gotoResult2 = GotoAetheryte(h.map, world, h.cn);
		Print("[" + h.cn + "] " + gotoResult2.Message);
		if (!(gotoResult2.Ok & flag))
		{
			return;
		}
		WaitUntil(() => InMap(h.map), () =>
		{
			Sched(() =>
			{
				Print(DiscardTrigger().Message);
			}, TimeSpan.FromMilliseconds(2500L));
		}, 240);
	}

	private List<Pt> GreensFor(HuntData h, string world)
	{
		if (!PointHunts.Contains(h.cn) || NoPointHunts.Contains(h.cn))
		{
			return new List<Pt>();
		}
		return LiveGreens(h.en, world) ?? (from q in HuntPoints(srankData.points.GetValueOrDefault(h.en), world)
			where q.S == 'g'
			select q).ToList();
	}

	private void MainGotoPoint(string en, string world, float x, float y)
	{
		SnapshotData snapshotData = srankData?.data;
		if (snapshotData != null)
		{
			HuntData huntData = snapshotData.hunts.FirstOrDefault((HuntData q) => q.en == en);
			if (huntData != null)
			{
				List<Pt> route = GreensFor(huntData, world);
				GotoResult gotoResult = Goto(huntData.map, x, y, world, HuntLineOverride(en, world), huntData.cn, nav: true, route);
				Print("[" + huntData.cn + "] " + gotoResult.Message);
			}
		}
	}

	private void MainGotoAetheryte(string en, string world)
	{
		SnapshotData snapshotData = srankData?.data;
		if (snapshotData == null)
		{
			return;
		}
		HuntData h = snapshotData.hunts.FirstOrDefault((HuntData q) => q.en == en);
		if (h == null)
		{
			return;
		}
		GotoResult gotoResult = GotoAetheryte(h.map, world, h.cn);
		Print("[" + h.cn + "] " + gotoResult.Message);
		if (!gotoResult.Ok || !DiscardMaps.ContainsKey(h.map))
		{
			return;
		}
		WaitUntil(() => InMap(h.map), () =>
		{
			Sched(() =>
			{
				Print(DiscardTrigger().Message);
			}, TimeSpan.FromMilliseconds(2500L));
		}, 240);
	}

	private unsafe void TryAutoConfirmReturn()
	{
		if (!autoConfirmReturn || (!autoRunning && !autoPaused))
		{
			return;
		}
		try
		{
			if (!Dead())
			{
				returnClickLogged = false;
				lastReturnClick = default;
				return;
			}
		}
		catch
		{
			return;
		}
		if (DateTime.Now - lastReturnClick <= TimeSpan.FromMilliseconds(500L))
		{
			return;
		}
		try
		{
			AddonSelectYesno* addonByName = gameGui.GetAddonByName<AddonSelectYesno>("SelectYesno");
			if (addonByName != null && addonByName->IsVisible)
			{
				lastReturnClick = DateTime.Now;
				addonByName->FireCallbackInt(0);
				if (!returnClickLogged)
				{
					returnClickLogged = true;
					Print("[自动前往] 已自动点「确定」，传送到返回点");
				}
			}
		}
		catch
		{
		}
	}

	private unsafe void TryAutoSummon()
	{
		if (DateTime.Now - lastSummonCheck <= TimeSpan.FromSeconds(2L))
		{
			return;
		}
		lastSummonCheck = DateTime.Now;
		uint territoryType = clientState.TerritoryType;
		if (!MinionMap.TryGetValue(territoryType, out var value) || !unlockedMinions.Contains(value) || condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion] || condition[ConditionFlag.MountOrOrnamentTransition] || condition[ConditionFlag.Mounting] || condition[ConditionFlag.Mounting71])
		{
			return;
		}
		ActionManager* ptr = ActionManager.Instance();
		if (ptr == null || ptr->GetActionStatus(ActionType.Companion, value, 3758096384uL, checkRecastActive: true, checkCastingActive: true, null) != 0)
		{
			return;
		}
		foreach (IGameObject item in objectTable)
		{
			if (item is ICharacter { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion } character)
			{
				if (character.BaseId == value)
				{
					return;
				}
				break;
			}
		}
		ptr->UseAction(ActionType.Companion, value, 3758096384uL, 0u, ActionManager.UseActionMode.None, 0u, null);
	}

	private unsafe string SummonForCurrentTerritory()
	{
		uint territoryType = clientState.TerritoryType;
		if (!MinionMap.TryGetValue(territoryType, out var value))
		{
			return $"当前地图（territory {territoryType}）没有配置自动召唤随从。";
		}
		if (!unlockedMinions.Contains(value))
		{
			return $"随从 {value} 还没解锁，无法召唤。";
		}
		ActionManager* ptr = ActionManager.Instance();
		if (ptr == null)
		{
			return "动作系统未就绪";
		}
		if (ptr->GetActionStatus(ActionType.Companion, value, 3758096384uL, checkRecastActive: true, checkCastingActive: true, null) == 0)
		{
			ptr->UseAction(ActionType.Companion, value, 3758096384uL, 0u, ActionManager.UseActionMode.None, 0u, null);
			return $"已召唤随从（minionId {value}）。";
		}
		return $"随从 {value} 当前不可用（冷却/条件不满足）。";
	}

	private void TryAutoLeave()
	{
		if (!(DateTime.Now - lastLeaveTry <= TimeSpan.FromSeconds(1L)) && !IsInParty() && condition[ConditionFlag.BoundByDuty])
		{
			lastLeaveTry = DateTime.Now;
			DoLeaveDuty();
		}
	}

	private unsafe static bool IsInParty()
	{
		try
		{
			GroupManager* ptr = GroupManager.Instance();
			return ptr != null && ptr->MainGroup.MemberCount > 0;
		}
		catch
		{
			return false;
		}
	}

	private void DoLeaveDuty()
	{
		if (EventFramework.CanLeaveCurrentContent())
		{
			EventFramework.LeaveCurrentContent(forced: true);
			Print("独身自动退本：已退出当前副本。");
		}
	}

	private void InitFontAtlasIfNeeded()
	{
		if (fontInitTried)
		{
			return;
		}
		fontInitTried = true;
		try
		{
			string text = FindYaheiPath();
			if (text == null)
			{
				log.Warning("[SrankFlag] 没找到微软雅黑，继续用 Dalamud 默认字体");
				return;
			}
			ushort[] array = BuildGlyphRanges();
			IFontAtlas atlas = (fontAtlas = uiBuilder.CreateFontAtlas(FontAtlasAutoRebuildMode.OnNewFrame, isGlobalScaled: false, "SrankFlag"));
			fontHandleS = AddYaheiFont(atlas, text, array, 14f);
			fontHandleM = AddYaheiFont(atlas, text, array, 22f);
			fontHandleL = AddYaheiFont(atlas, text, array, 44f);
			log.Info($"[SrankFlag] 字体已切换到 {Path.GetFileName(text)}（{array.Length / 2} 段字形范围）");
		}
		catch (Exception exception)
		{
			fontAtlas = null;
			fontHandleS = (fontHandleM = (fontHandleL = null));
			log.Error(exception, "[SrankFlag] 微软雅黑初始化失败，回退默认字体");
		}
	}

	private string? FindYaheiPath()
	{
		string path;
		try
		{
			path = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		}
		catch
		{
			path = "C:" + Path.DirectorySeparatorChar + "Windows";
		}
		string[] array = new string[5] { "msyh.ttc", "msyhbd.ttc", "msyhl.ttc", "simhei.ttf", "simsun.ttc" };
		string[] array2 = array;
		foreach (string path2 in array2)
		{
			string text = Path.Combine(path, "Fonts", path2);
			if (File.Exists(text))
			{
				return text;
			}
		}
		array2 = array;
		foreach (string path3 in array2)
		{
			string text2 = Path.Combine(pluginDirectory, path3);
			if (File.Exists(text2))
			{
				return text2;
			}
		}
		return null;
	}

	private ushort[] BuildGlyphRanges()
	{
		SortedSet<int> set = new SortedSet<int>();
		for (int i = 32; i <= 126; i++)
		{
			set.Add(i);
		}
		Take("\u3000、。，．・：；！？（）【】「」『』《》“”‘’—～·…※×÷°±←↑→↓●○◆◇■□★☆⌃⌄▶▸▾⟳✕✓…魔法桶");
		Take("·×—…⌃⌄▶▸▾●⟳\u3000、。「」不丢个中为之乌了亚产人从令件伊休传佐作俄修先克入全兰关具兽内再写冷凯出击分切到制刷前副加务动助努劳勇包匹占卡即却厄厌原发取口只召可右号叹同名后启员命唤器回因图在地坐垓塔士夏外多够大天太失奇奈奥妲姆始子字存学安家宽寻小尔尚尤就尼展山岛岸川左已布帕常庇开异弃弗张当录影往得德忆忌快态思怪恕息恰戏成或戾所手扎打找把报拆拉拖持按换据排接控提插搜支收数整文斯新旗无旧时明显晓晶智暂暗曦最月有服朗未本机杀条来析林查柴标树格检森次正步每气水沉没法泪津活海清游湖湿满炫点焰照熊物特状独狭玛玩珀珂理用的监目相看着知示穆空窗立站端等管系索红线组细终经绘统绪维绿缇缓编缝缺网置美翁背胝能自艾茶荒菲萨落藏虫行被西要解触记试询该请读调谢豆败起足跨路跳踩身载辉过近还这连迦迷退送逆途道遗部配里重野量金钟银锁键闭间阔阳阿附除随隐雷需露页顾预颇风飞马驱鲁黑（），：？一与且东云剩区央实微推日星晴暴灵现碧秒算纳续翻致薄衣计诺进采阴集雨雪雾上下专业串主乃亮伐会伽体余你倒做兀光共准初利剧功化南去反叠向含呱和咕围备复夺套孔完宠属巡巨师希并建弱弹形心志忽恶情意战执技折指摩撒改放效斗更朝朱束构楚概段洛流测深烹爆版牛犎狂狩猎率生申画留白盐直确磷科程符第筛精级结继职色芙英范蓝蝶装认讨贝走超软返迪选逐造都铺闪闸陆限隆雀雅青面题骑魔鸟默但绑设跑我停也候别国它帝幻搭是混穿判好定归零差话任工延止消何儿己断离给轮块模住几稳刻咏唱头神否掉园艺《》『』【】・三仍伤使冰受害对射尝平待挑攻斧猛突约蛭衅诱距边钓锥！．；～位域基潜规固补交代单吃合品方菜表世侧凑列勾双变响圈境少层底式强悬感户择旦期栏样框桶永浮潮片界盯种稍紧聊至苍莲让错队顶龙");
		List<SnapshotData> list = srankData?.dcs;
		if (list != null && list.Count > 0)
		{
			foreach (SnapshotData item in list)
			{
				Take(item.dc);
				string[] worlds = item.worlds;
				foreach (string text in worlds)
				{
					Take(text);
				}
				HuntData[] hunts = item.hunts;
				foreach (HuntData huntData in hunts)
				{
					Take(huntData.cn);
					Take(huntData.en);
					Take(huntData.map);
					Take(huntData.patch);
					Take(huntData.kind);
					worlds = huntData.tips;
					if (worlds != null && worlds.Length > 0)
					{
						worlds = huntData.tips;
						foreach (string text2 in worlds)
						{
							Take(text2);
						}
					}
				}
			}
		}
		SnapshotData snapshotData = srankData?.data;
		if (snapshotData != null)
		{
			Take(snapshotData.dc);
			string[] worlds = snapshotData.worlds;
			foreach (string text3 in worlds)
			{
				Take(text3);
			}
			HuntData[] hunts = snapshotData.hunts;
			foreach (HuntData huntData2 in hunts)
			{
				Take(huntData2.cn);
				Take(huntData2.en);
				Take(huntData2.map);
				Take(huntData2.patch);
			}
		}
		foreach (string noPointHunt in NoPointHunts)
		{
			Take(noPointHunt);
		}
		NavHunt[] navHunts = NavHunts;
		foreach (NavHunt navHunt in navHunts)
		{
			Take(navHunt.Cn);
			Take(navHunt.Map);
		}
		foreach (string key in DiscardMaps.Keys)
		{
			Take(key);
		}
		TakeAssemblyLiterals(Take);
		int[] array = set.ToArray();
		List<ushort> list2 = new List<ushort>(array.Length + 8);
		for (int l = 0; l < array.Length; l++)
		{
			int num = array[l];
			int num2 = num;
			while (l + 1 < array.Length && array[l + 1] == num2 + 1)
			{
				l++;
				num2 = array[l];
			}
			list2.Add((ushort)num);
			list2.Add((ushort)num2);
		}
		list2.Add(0);
		return list2.ToArray();
		void Take(string? text4)
		{
			if (!string.IsNullOrEmpty(text4))
			{
				foreach (int num3 in text4)
				{
					if (num3 >= 32 && num3 < 65533)
					{
						set.Add(num3);
					}
				}
			}
		}
	}

	private static void TakeAssemblyLiterals(Action<string?> take)
	{
		try
		{
			Type[] types = typeof(Plugin).Assembly.GetTypes();
			foreach (Type type in types)
			{
				FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					if ((object)fieldInfo != null && fieldInfo.IsLiteral && fieldInfo.FieldType == typeof(string))
					{
						take(fieldInfo.GetRawConstantValue() as string);
					}
				}
				MethodInfo[] methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				for (int j = 0; j < methods.Length; j++)
				{
					ScanIlStrings(methods[j], take);
				}
				ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				for (int j = 0; j < constructors.Length; j++)
				{
					ScanIlStrings(constructors[j], take);
				}
			}
		}
		catch
		{
		}
	}

	private static void ScanIlStrings(MethodBase m, Action<string?> take)
	{
		byte[] array;
		try
		{
			array = m.GetMethodBody()?.GetILAsByteArray();
		}
		catch
		{
			return;
		}
		if (array == null)
		{
			return;
		}
		for (int i = 0; i + 4 < array.Length; i++)
		{
			if (array[i] == 114)
			{
				int metadataToken = BitConverter.ToInt32(array, i + 1);
				try
				{
					take(m.Module.ResolveString(metadataToken));
				}
				catch
				{
				}
			}
		}
	}

	private static IFontHandle AddYaheiFont(IFontAtlas atlas, string path, ushort[] ranges, float sizePx)
	{
		return atlas.NewDelegateFontHandle((IFontAtlasBuildToolkit step) =>
		{
			step.OnPreBuild((IFontAtlasBuildToolkitPreBuild toolkit) =>
			{
				try
				{
					SafeFontConfig fontConfig = new SafeFontConfig
					{
						SizePx = sizePx,
						GlyphRanges = ranges,
						PixelSnapH = true,
						OversampleH = 1,
						OversampleV = 1
					};
					toolkit.AddFontFromFile(path, in fontConfig);
				}
				catch
				{
				}
			});
		});
	}

	private void BeginFonts()
	{
		fontReady = false;
		if (fontAtlas == null)
		{
			return;
		}
		try
		{
			IFontHandle? fontHandle = fontHandleS;
			if (fontHandle != null && fontHandle.Available)
			{
				IFontHandle? fontHandle2 = fontHandleM;
				if (fontHandle2 != null && fontHandle2.Available)
				{
					IFontHandle? fontHandle3 = fontHandleL;
					if (fontHandle3 != null && fontHandle3.Available)
					{
						fontLockS = fontHandleS.Lock();
						fontLockM = fontHandleM.Lock();
						fontLockL = fontHandleL.Lock();
						fontPtrS = fontLockS.ImFont;
						fontPtrM = fontLockM.ImFont;
						fontPtrL = fontLockL.ImFont;
						fontReady = true;
						if (!fontProbeLogged)
						{
							fontProbeLogged = true;
							log.Info("[SrankFlag] 雅黑字形图集就绪（14 / 22 / 44px）");
						}
						return;
					}
				}
			}
			if (!fontProbeLogged)
			{
				Exception ex = fontHandleS?.LoadException;
				if (ex != null)
				{
					fontProbeLogged = true;
					log.Error(ex, "[SrankFlag] 雅黑字体构建失败，继续用默认字体");
				}
			}
		}
		catch
		{
			EndFonts();
		}
	}

	private void EndFonts()
	{
		fontReady = false;
		try
		{
			fontLockS?.Dispose();
		}
		catch
		{
		}
		try
		{
			fontLockM?.Dispose();
		}
		catch
		{
		}
		try
		{
			fontLockL?.Dispose();
		}
		catch
		{
		}
		fontLockS = (fontLockM = (fontLockL = null));
	}

	private ImFontPtr FontFor(float size)
	{
		if (!fontReady)
		{
			return ImGui.GetFont();
		}
		if (size <= 14f)
		{
			return fontPtrS;
		}
		if (size <= 22f)
		{
			return fontPtrM;
		}
		return fontPtrL;
	}

	private float TW(string text, float size)
	{
		if (!fontReady)
		{
			return ImGui.CalcTextSize(text).X * size / ImGui.GetFontSize();
		}
		ImGui.PushFont(FontFor(size));
		float result = ImGui.CalcTextSize(text).X * size / ImGui.GetFontSize();
		ImGui.PopFont();
		return result;
	}

	private void DrawMainWindow()
	{
		TrackFilterChanges();
	}

	private ISharedImmediateTexture? MiniIcon()
	{
		if (miniIcon != null || miniIconTried)
		{
			return miniIcon;
		}
		miniIconTried = true;
		try
		{
			string text = Path.Combine(pluginDirectory, "icon.png");
			if (!File.Exists(text))
			{
				return null;
			}
			if (pi.GetService(typeof(ITextureProvider)) is ITextureProvider textureProvider)
			{
				miniIcon = textureProvider.GetFromFileAbsolute(text);
			}
		}
		catch (Exception exception)
		{
			log.Warning(exception, "[魔法水桶] 小方块图标加载失败");
		}
		return miniIcon;
	}

	private void DrawMiniWindow()
	{
	}

	private void DrawWindowChrome()
	{
		Vector2 windowPos = ImGui.GetWindowPos();
		Vector2 vector = windowPos + ImGui.GetWindowSize();
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		string text = srankData?.data?.dc ?? "—";
		windowDrawList.AddRectFilled(windowPos, vector, ImGui.GetColorU32(V1Bg), 10f);
		uint colorU = ImGui.GetColorU32(lightTheme ? new Vector4(0f, 0f, 0f, 0.018f) : new Vector4(1f, 1f, 1f, 0.045f));
		windowDrawList.AddRectFilledMultiColor(windowPos + new Vector2(12f, 1f), new Vector2(vector.X - 12f, windowPos.Y + 96f), colorU, colorU, ImGui.GetColorU32(Transparent), ImGui.GetColorU32(Transparent));
		windowDrawList.AddRect(windowPos + new Vector2(0.5f, 0.5f), vector - new Vector2(0.5f, 0.5f), ImGui.GetColorU32(V1Border), 10f);
		Vector2 vector2 = windowPos + new Vector2(18f, 13f);
		windowDrawList.AddRectFilled(vector2, vector2 + new Vector2(36f, 36f), ImGui.GetColorU32(V1Accent), 8f);
		float num = TW("水", 22f);
		windowDrawList.AddText(FontFor(22f), 22f, vector2 + new Vector2(18f - num / 2f, 4f), ImGui.GetColorU32(V1OnAccent), "水");
		float num2 = vector2.X + 36f + 12f - windowPos.X;
		windowDrawList.AddText(FontFor(19f), 19f, windowPos + new Vector2(num2, 8f), ImGui.GetColorU32(V1Text), settingsOpen ? "魔法水桶 · 高级设置" : "魔法水桶");
		windowDrawList.AddText(FontFor(12f), 12f, windowPos + new Vector2(num2, 33f), ImGui.GetColorU32(V1Dim), "/// 熊站 · 可触发监控");
		float num3 = windowPos.Y + 62f - 2f;
		float num4 = windowPos.X + 16f;
		float num5 = vector.X - 16f;
		float x = (num4 + num5) * 0.5f;
		uint colorU2 = ImGui.GetColorU32(V1Border);
		uint colorU3 = ImGui.GetColorU32(Transparent);
		windowDrawList.AddRectFilledMultiColor(new Vector2(num4, num3), new Vector2(x, num3 + 1f), colorU3, colorU2, colorU2, colorU3);
		windowDrawList.AddRectFilledMultiColor(new Vector2(x, num3), new Vector2(num5, num3 + 1f), colorU2, colorU3, colorU3, colorU2);
		Vector2 vector3 = new Vector2(vector.X - 44f, windowPos.Y + 16f);
		Vector2 mousePos = ImGui.GetMousePos();
		int num6;
		if (mousePos.X >= vector3.X && mousePos.X <= vector3.X + 26f && mousePos.Y >= vector3.Y)
		{
			num6 = ((mousePos.Y <= vector3.Y + 26f) ? 1 : 0);
			if (num6 != 0)
			{
				windowDrawList.AddRectFilled(vector3, vector3 + new Vector2(26f, 26f), ImGui.GetColorU32(new Vector4(V1Pink.X, V1Pink.Y, V1Pink.Z, 0.16f)), 7f);
				windowDrawList.AddRect(vector3 + new Vector2(0.5f, 0.5f), vector3 + new Vector2(25.5f, 25.5f), ImGui.GetColorU32(new Vector4(V1Pink.X, V1Pink.Y, V1Pink.Z, 0.45f)), 7f);
			}
		}
		else
		{
			num6 = 0;
		}
		uint colorU4 = ImGui.GetColorU32((num6 != 0) ? V1Pink : V1Dim);
		windowDrawList.AddLine(vector3 + new Vector2(9f, 9f), vector3 + new Vector2(17f, 17f), colorU4, 1.7f);
		windowDrawList.AddLine(vector3 + new Vector2(17f, 9f), vector3 + new Vector2(9f, 17f), colorU4, 1.7f);
		if (num6 != 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
		{
			SavePrefs();
			if (settingsOpen)
			{
				settingsOpen = false;
			}
			else if (legacyMainOpen)
			{
				legacyMainOpen = false;
			}
			else
			{
				mainWindowOpen = false;
			}
			miniMode = false;
		}
		Vector2 vector4 = new Vector2(vector3.X - 10f - 58f, windowPos.Y + 16f);
		bool flag = mousePos.X >= vector4.X && mousePos.X <= vector4.X + 58f && mousePos.Y >= vector4.Y && mousePos.Y <= vector4.Y + 26f;
		windowDrawList.AddRectFilled(vector4, vector4 + new Vector2(58f, 26f), ImGui.GetColorU32(flag ? new Vector4(V1Pink.X, V1Pink.Y, V1Pink.Z, 0.18f) : new Vector4(1f, 1f, 1f, 0.035f)), 7f);
		windowDrawList.AddRect(vector4 + new Vector2(0.5f, 0.5f), vector4 + new Vector2(57.5f, 25.5f), ImGui.GetColorU32(new Vector4(V1Pink.X, V1Pink.Y, V1Pink.Z, flag ? 0.55f : 0.26f)), 7f);
		float num7 = TW("停止", 13f);
		float num8 = 12f + num7;
		float num9 = vector4.X + (58f - num8) / 2f;
		float num10 = vector4.Y + 13f;
		windowDrawList.AddRectFilled(new Vector2(num9, num10 - 3.5f), new Vector2(num9 + 7f, num10 + 3.5f), ImGui.GetColorU32(flag ? V1Pink : new Vector4(V1Pink.X, V1Pink.Y, V1Pink.Z, 0.72f)), 1.5f);
		windowDrawList.AddText(FontFor(13f), 13f, new Vector2(num9 + 7f + 5f, num10 - 9.099999f), ImGui.GetColorU32(flag ? V1Pink : V1Dim), "停止");
		if (flag && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
		{
			StopAll();
		}
		if (flag)
		{
			ImGui.SetTooltip("停止全部：取消巡绿点 / 上坐骑 / 到点动作 / 进退副本（也可 /水桶 stop）");
		}
		Vector2 vector5 = new Vector2(vector4.X - 10f - 26f, windowPos.Y + 16f);
		int num11;
		if (mousePos.X >= vector5.X && mousePos.X <= vector5.X + 26f && mousePos.Y >= vector5.Y)
		{
			num11 = ((mousePos.Y <= vector5.Y + 26f) ? 1 : 0);
			if (num11 != 0)
			{
				windowDrawList.AddRectFilled(vector5, vector5 + new Vector2(26f, 26f), ImGui.GetColorU32(new Vector4(V1Accent.X, V1Accent.Y, V1Accent.Z, 0.14f)), 7f);
				windowDrawList.AddRect(vector5 + new Vector2(0.5f, 0.5f), vector5 + new Vector2(25.5f, 25.5f), ImGui.GetColorU32(new Vector4(V1Accent.X, V1Accent.Y, V1Accent.Z, 0.42f)), 7f);
			}
		}
		else
		{
			num11 = 0;
		}
		windowDrawList.AddLine(col: ImGui.GetColorU32((num11 != 0) ? V1AccentInk : V1Dim), p1: vector5 + new Vector2(9f, 17f), p2: vector5 + new Vector2(17f, 17f), thickness: 1.7f);
		if (num11 != 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
		{
			miniMode = true;
		}
		if (num11 != 0)
		{
			ImGui.SetTooltip("最小化成小方块（点小方块展开）");
		}
		float num12 = windowPos.X + num2 + TW("魔法水桶", 19f) + 26f;
		float num13 = vector3.X - 14f - 66f;
		searchInHeader = ImGui.GetWindowSize().X >= 820f && num13 - num12 >= 330f;
		bool flag2 = ImGui.GetWindowSize().X < 660f;
		bool flag3 = ImGui.GetWindowSize().X < 500f;
		DateTime utcDateTime = DateTimeOffset.FromUnixTimeSeconds((long)((double)DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1440.0 / 70.0)).UtcDateTime;
		string text2 = $"ET {utcDateTime:HH:mm}";
		string text3 = $"LT {DateTime.Now:HH:mm}";
		float num14 = TW(text2, 14f);
		float num15 = TW(text3, 14f);
		float num16 = vector3.X - 20f - 66f - num14;
		if (!flag3 && !searchInHeader)
		{
			windowDrawList.AddText(FontFor(14f), 14f, new Vector2(num16, windowPos.Y + 21f), ImGui.GetColorU32(V1Text), text2);
			windowDrawList.AddText(FontFor(14f), 14f, new Vector2(num16 - num15 - 16f, windowPos.Y + 21f), ImGui.GetColorU32(V1Dim), text3);
		}
		if (!flag2 && !searchInHeader)
		{
			Vector2 vector6 = new Vector2(num16 - num15 - 28f - 108f, windowPos.Y + 15f);
			windowDrawList.AddRectFilled(vector6, vector6 + new Vector2(108f, 28f), ImGui.GetColorU32(V1Bg2), 6f);
			windowDrawList.AddRect(vector6 + new Vector2(0.5f, 0.5f), vector6 + new Vector2(107.5f, 27.5f), ImGui.GetColorU32(V1BorderHi), 6f);
			windowDrawList.AddText(FontFor(11f), 11f, vector6 + new Vector2(11f, 8f), ImGui.GetColorU32(V1Dim), "DC");
			windowDrawList.AddText(FontFor(14f), 14f, vector6 + new Vector2(33f, 6f), ImGui.GetColorU32(V1Text), text);
		}
		Vector2 vector7 = vector - new Vector2(20f, 20f);
		bool flag4 = mousePos.X >= vector7.X && mousePos.X <= vector.X && mousePos.Y >= vector7.Y && mousePos.Y <= vector.Y;
		if (flag4 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
		{
			winResizing = true;
			winResizeStart = mousePos;
			winResizeBase = ImGui.GetWindowSize();
		}
		if (winResizing)
		{
			if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
			{
				Vector2 vector8 = mousePos - winResizeStart;
				winPendingSize = new Vector2(Math.Clamp(winResizeBase.X + vector8.X, 520f, 3000f), Math.Clamp(winResizeBase.Y + vector8.Y, 480f, 2400f));
			}
			else
			{
				winResizing = false;
				winPendingSize = null;
			}
		}
		Vector4 col = ((flag4 || winResizing) ? new Vector4(V1Accent.X, V1Accent.Y, V1Accent.Z, 0.95f) : new Vector4(V1Faint.X, V1Faint.Y, V1Faint.Z, V1Faint.W));
		for (int i = 0; i < 3; i++)
		{
			Vector2 vector9 = new Vector2(vector.X - 8f - (float)(i * 5), vector.Y - 8f);
			windowDrawList.AddLine(vector9, vector9 + new Vector2(0f, -(4 + i * 5)), ImGui.GetColorU32(col), 1.4f);
		}
		if (flag4 && !winResizing)
		{
			ImGui.SetTooltip("拖动调整窗口大小");
		}
		if (searchInHeader)
		{
			ImGui.SetCursorScreenPos(new Vector2(num12, windowPos.Y + 12f));
			DrawSearchBar(num13 - num12);
		}
	}

	private static void ApplyTheme(bool light)
	{
		lightTheme = light;
		if (light)
		{
			V1Bg = new Vector4(0.98f, 0.984f, 0.973f, 1f);
			V1Bg2 = new Vector4(0.898f, 0.91f, 0.878f, 1f);
			V1Card = new Vector4(0.855f, 0.871f, 0.831f, 1f);
			V1Input = new Vector4(1f, 1f, 1f, 1f);
			V1Border = new Vector4(0.612f, 0.647f, 0.584f, 1f);
			V1BorderHi = new Vector4(0.408f, 0.447f, 0.384f, 1f);
			V1BorderSoft = new Vector4(0.408f, 0.447f, 0.384f, 0.65f);
			V1Accent = new Vector4(0.792f, 0.902f, 0.235f, 1f);
			V1AccentInk = new Vector4(0.259f, 0.333f, 0f, 1f);
			V1Text = new Vector4(0.035f, 0.047f, 0.027f, 1f);
			V1Dim = new Vector4(0.157f, 0.184f, 0.145f, 1f);
			V1Faint = new Vector4(0.157f, 0.184f, 0.145f, 0.62f);
			V1FaintSoft = new Vector4(0.157f, 0.184f, 0.145f, 0.82f);
			V1Lime = new Vector4(0.208f, 0.353f, 0f, 1f);
			V1Mint = new Vector4(0f, 0.353f, 0.196f, 1f);
			V1Pink = new Vector4(0.635f, 0.043f, 0.196f, 1f);
			V1Panel = new Vector4(0.929f, 0.937f, 0.914f, 1f);
			V1PanelBorder = new Vector4(0.612f, 0.647f, 0.584f, 1f);
			V1Hover = new Vector4(0f, 0f, 0f, 0.06f);
			V1HoverMid = new Vector4(0f, 0f, 0f, 0.1f);
			V1HoverStrong = new Vector4(0f, 0f, 0f, 0.16f);
			V1Sel = new Vector4(0f, 0f, 0f, 0.07f);
			V1Scroll = new Vector4(0f, 0f, 0f, 0.28f);
			V1ScrollHi = new Vector4(0f, 0f, 0f, 0.42f);
			V1FrameHi = new Vector4(0.976f, 0.98f, 0.969f, 1f);
			V1BtnHi = new Vector4(0.804f, 0.824f, 0.78f, 1f);
			V1BtnAct = new Vector4(0.757f, 0.78f, 0.729f, 1f);
			V1PopBg = new Vector4(1f, 1f, 1f, 0.99f);
			V1Track = new Vector4(0.78f, 0.8f, 0.745f, 1f);
		}
		else
		{
			V1Bg = new Vector4(0.043f, 0.051f, 0.051f, 0.99f);
			V1Bg2 = new Vector4(0.059f, 0.071f, 0.067f, 1f);
			V1Card = new Vector4(0.075f, 0.086f, 0.082f, 1f);
			V1Input = new Vector4(0.055f, 0.063f, 0.059f, 1f);
			V1Border = new Vector4(0.118f, 0.137f, 0.129f, 1f);
			V1BorderHi = new Vector4(0.173f, 0.196f, 0.18f, 1f);
			V1BorderSoft = new Vector4(0.173f, 0.196f, 0.18f, 0.55f);
			V1Accent = new Vector4(0.839f, 0.949f, 0.294f, 1f);
			V1AccentInk = new Vector4(0.839f, 0.949f, 0.294f, 1f);
			V1Text = new Vector4(0.949f, 0.961f, 0.937f, 1f);
			V1Dim = new Vector4(0.455f, 0.502f, 0.475f, 1f);
			V1Faint = new Vector4(0.455f, 0.502f, 0.475f, 0.45f);
			V1FaintSoft = new Vector4(0.455f, 0.502f, 0.475f, 0.75f);
			V1Lime = new Vector4(0.839f, 0.949f, 0.294f, 1f);
			V1Mint = new Vector4(0.231f, 0.89f, 0.541f, 1f);
			V1Pink = new Vector4(1f, 0.302f, 0.553f, 1f);
			V1Panel = new Vector4(0.059f, 0.071f, 0.067f, 1f);
			V1PanelBorder = new Vector4(0.118f, 0.137f, 0.129f, 1f);
			V1Hover = new Vector4(1f, 1f, 1f, 0.045f);
			V1HoverMid = new Vector4(1f, 1f, 1f, 0.07f);
			V1HoverStrong = new Vector4(1f, 1f, 1f, 0.13f);
			V1Sel = new Vector4(1f, 1f, 1f, 0.03f);
			V1Scroll = new Vector4(1f, 1f, 1f, 0.08f);
			V1ScrollHi = new Vector4(1f, 1f, 1f, 0.14f);
			V1FrameHi = new Vector4(0.098f, 0.11f, 0.106f, 1f);
			V1BtnHi = new Vector4(0.11f, 0.125f, 0.117f, 1f);
			V1BtnAct = new Vector4(0.075f, 0.086f, 0.082f, 1f);
			V1PopBg = new Vector4(0.043f, 0.051f, 0.051f, 0.98f);
			V1Track = new Vector4(0.086f, 0.098f, 0.094f, 1f);
		}
	}

	private void LoadTheme()
	{
		try
		{
			lightMode = File.Exists(ThemeFile) && File.ReadAllText(ThemeFile).Trim().Equals("light", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			lightMode = false;
		}
		ApplyTheme(lightMode);
	}

	private void SetLightTheme(bool light)
	{
		lightMode = light;
		ApplyTheme(light);
		try
		{
			File.WriteAllText(ThemeFile, light ? "light" : "dark");
		}
		catch
		{
		}
	}

	private Vector4 WorldColor(string w)
	{
		int num = WorldPalette.Length - 1;
		List<SnapshotData> list = srankData?.dcs;
		if (list != null && list.Count > 0)
		{
			for (int i = 0; i < list.Count; i++)
			{
				int num2 = Array.IndexOf(list[i].worlds, w);
				if (num2 >= 0)
				{
					num = (i * 5 + num2) % WorldPalette.Length;
					break;
				}
			}
		}
		Vector4 result = WorldPalette[num];
		if (!lightTheme)
		{
			return result;
		}
		return new Vector4(result.X * 0.72f, result.Y * 0.72f, result.Z * 0.72f, result.W);
	}

	private void EnsureMaintPairs()
	{
		DataFile dataFile = srankData;
		if (dataFile == null)
		{
			return;
		}
		Fill(dataFile.data?.hunts);
		foreach (SnapshotData dc in dataFile.dcs)
		{
			Fill(dc?.hunts);
		}
		static void Fill(HuntData[]? hunts)
		{
			if (hunts != null)
			{
				foreach (HuntData huntData in hunts)
				{
					if (huntData != null)
					{
						double[] mt = huntData.mt;
						if (mt == null || mt.Length < 2)
						{
							huntData.mt = (MaintPairOverride.TryGetValue(huntData.en, out double[] value) ? ((double[])value.Clone()) : new double[2]
							{
								huntData.cd * 0.6,
								huntData.win * 0.6
							});
						}
					}
				}
			}
		}
	}

	private void LoadSrankData()
	{
		try
		{
			string text = Path.Combine(pluginDirectory, "srank_data.json");
			if (!File.Exists(text))
			{
				dataError = "找不到数据文件：" + text;
				log.Warning("[SrankFlag] 找不到数据文件 {0}", text);
				return;
			}
			srankData = JsonSerializer.Deserialize<DataFile>(File.ReadAllText(text));
			EnsureMaintPairs();
			bool flag = srankData?.data == null;
			if (flag)
			{
				int? num = srankData?.dcs.Count;
				bool flag2 = ((!num.HasValue || num.GetValueOrDefault() == 0) ? true : false);
				flag = flag2;
			}
			dataError = (flag ? ("数据文件解析为空：" + text) : "");
			List<SnapshotData> list = srankData?.dcs;
			if (list != null && list.Count > 0)
			{
				string want = LoadDcPref();
				if (want.Length == 0)
				{
					string cur = CurrentWorldName();
					want = list.FirstOrDefault((SnapshotData x) => x.worlds.Contains(cur, StringComparer.OrdinalIgnoreCase))?.dc ?? "";
				}
				SnapshotData snapshotData = list.FirstOrDefault((SnapshotData x) => x.dc == want) ?? list[0];
				srankData.data = snapshotData;
				currentDc = snapshotData.dc;
			}
			SnapshotData snapshotData2 = srankData?.data;
			if (snapshotData2 != null)
			{
				if (snapshotData2.snapshot <= 0.0)
				{
					snapshotData2.snapshot = new DateTimeOffset(File.GetLastWriteTimeUtc(text)).ToUnixTimeSeconds();
				}
				ApplyFilter(currentDc, snapshotData2);
			}
			MergeBearCache();
			ApplySonarKills();
			LoadTriggered();
			SweepTriggeredByDeath();
		}
		catch (Exception ex)
		{
			dataError = "读取 srank_data.json 失败：" + ex.Message;
			log.Error(ex, "[SrankFlag] 读取 srank_data.json 失败");
		}
	}

	private string LoadDcPref()
	{
		LoadPrefs();
		if (prefs.dc.Length > 0)
		{
			return prefs.dc;
		}
		try
		{
			string path = Path.Combine(pluginDirectory, "dc.txt");
			return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
		}
		catch
		{
			return "";
		}
	}

	private void LoadPrefs()
	{
		if (prefsLoaded)
		{
			return;
		}
		prefsLoaded = true;
		try
		{
			string path = Path.Combine(pluginDirectory, "prefs.json");
			if (File.Exists(path))
			{
				UiPrefs uiPrefs = JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(path));
				if (uiPrefs != null)
				{
					prefs = uiPrefs;
				}
				UiPrefs uiPrefs2 = prefs;
				if (uiPrefs2.byDc == null)
				{
					Dictionary<string, DcFilter> dictionary = (uiPrefs2.byDc = new Dictionary<string, DcFilter>(StringComparer.Ordinal));
				}
			}
		}
		catch (Exception ex)
		{
			log.Warning("[SrankFlag] prefs.json 读取失败：" + ex.Message);
		}
		if (prefs.windowFilterVersion < 1)
		{
			foreach (DcFilter filter in prefs.byDc.Values) filter.onlyOpen = true;
			prefs.windowFilterVersion = 1;
		}
	}

	private void SavePrefs()
	{
		try
		{
			CaptureFilter();
			filterDirtyAt = DateTime.MinValue;
			File.WriteAllText(Path.Combine(pluginDirectory, "prefs.json"), JsonSerializer.Serialize(prefs, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			log.Warning("[SrankFlag] prefs.json 写入失败：" + ex.Message);
		}
		try
		{
			nativeHud?.ForceMainRefresh();
		}
		catch
		{
		}
	}

	private void CaptureFilter()
	{
		if (currentDc.Length != 0)
		{
			prefs.dc = currentDc;
			prefs.byDc[currentDc] = new DcFilter
			{
				worlds = worldFilter.ToList(),
				worldsSet = true,
				patches = patchSel.ToList(),
				onlyOpen = onlyOpen,
				search = searchText
			};
		}
	}

	private void ApplyFilter(string dc, SnapshotData d)
	{
		LoadPrefs();
		worldFilter.Clear();
		patchSel.Clear();
		onlyOpen = true;
		searchText = "";
		if (prefs.byDc.TryGetValue(dc, out DcFilter value) && value.worldsSet)
		{
			foreach (string world in value.worlds)
			{
				if (d.worlds.Contains(world, StringComparer.OrdinalIgnoreCase))
				{
					worldFilter.Add(world);
				}
			}
			foreach (string patch in value.patches)
			{
				patchSel.Add(patch);
			}
			onlyOpen = value.onlyOpen;
			searchText = value.search ?? "";
		}
		else
		{
			string[] worlds = d.worlds;
			foreach (string item in worlds)
			{
				worldFilter.Add(item);
			}
		}
		lastFilterSig = FilterSig();
		filterDirtyAt = DateTime.MinValue;
	}

	private string FilterSig()
	{
		return currentDc + "|" + string.Join(",", worldFilter.OrderBy((string x) => x, StringComparer.Ordinal)) + "|" + string.Join(",", patchSel.OrderBy((string x) => x, StringComparer.Ordinal)) + "|" + onlyOpen + "|" + searchText;
	}

	private void TrackFilterChanges()
	{
		string text = FilterSig();
		if (text == lastFilterSig)
		{
			if (filterDirtyAt != DateTime.MinValue && (DateTime.UtcNow - filterDirtyAt).TotalSeconds > 1.2)
			{
				SavePrefs();
			}
		}
		else
		{
			lastFilterSig = text;
			CaptureFilter();
			filterDirtyAt = DateTime.UtcNow;
		}
	}

	private void SelectDc(string dc)
	{
		List<SnapshotData> list = srankData?.dcs;
		if (list == null || list.Count <= 0)
		{
			return;
		}
		SnapshotData snapshotData = list.FirstOrDefault((SnapshotData x) => x.dc == dc) ?? list[0];
		if (srankData.data != snapshotData)
		{
			if(bearFetching&&bearNotify)Print($"已取消 {currentDc} 的手动刷新（切换大区）。");
			bearNotify=false;
			CaptureFilter();
			srankData.data = snapshotData;
			currentDc = snapshotData.dc;
			ApplyFilter(snapshotData.dc, snapshotData);
			bearStatus = "尚未拉取";
			bearRequestGeneration++;
			bearRequest?.Cancel();
			bearRequest?.Dispose();
			bearRequest=null;
			bearFetching=false;
			lastBearFetch = DateTime.MinValue;
			try
			{
				File.WriteAllText(Path.Combine(pluginDirectory, "dc.txt"), snapshotData.dc);
			}
			catch
			{
			}
			SavePrefs();
			if (currentDc == "豆豆柴")
			{
				MaybeStartBearFetch(force: true);
			}
			else
			{
				bearStatus = "当前大区数据需手动刷新";
			}
			Print((currentDc == "豆豆柴") ? $"已切换到 {snapshotData.dc}（{snapshotData.worlds.Length} 个服 · {snapshotData.hunts.Length} 只 S）" : $"已切换到 {snapshotData.dc}（{snapshotData.worlds.Length} 个服 · {snapshotData.hunts.Length} 只 S）；该大区数据来自公开熊站，请点右上角「刷新」手动拉取");
		}
	}

	private static async Task<(string hunt, string world, double ts)> BearQueryAsync(string huntEn, string world,CancellationToken token=default)
	{
		using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
		timeout.CancelAfter(TimeSpan.FromSeconds(8));
		string content = JsonSerializer.Serialize(new
		{
			HuntName = huntEn,
			WorldName = world
		});
		using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
		using HttpResponseMessage resp = await BearHttp.Value.PostAsync(BearApi, content2,timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
		if(jsonDocument.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("公开熊站返回的记录格式无效");
		return (huntEn,world,BearRefreshPolicy.LatestKill(jsonDocument.RootElement));
	}

	private void MaybeRefreshRestart(bool force)
	{
		if(!BearRefreshPolicy.UsesMainSite(currentDc))return;
		if (force || !(DateTime.UtcNow - lastRestartFetch <= TimeSpan.FromSeconds(60L)))
		{
			lastRestartFetch = DateTime.UtcNow;
			Task.Run(() => RefreshRestartAsync());
		}
	}

	private async Task RefreshRestartAsync()
	{
		_ = 1;
		try
		{
			using HttpResponseMessage resp = await BearHttp.Value.GetAsync("http://119.29.119.179/api/restart").ConfigureAwait(continueOnCapturedContext: false);
			if (!resp.IsSuccessStatusCode)
			{
				return;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false));
			if (jsonDocument.RootElement.TryGetProperty("restartAt", out var value) && value.ValueKind == JsonValueKind.Number)
			{
				double num = value.GetDouble();
				restartAt = ((num > 0.0) ? num : 0.0);
			}
		}
		catch (Exception ex)
		{
			log.Warning("[SrankFlag] 重启时间拉取失败，沿用上次值：" + ex.Message);
		}
	}

	private void MaybeStartBearFetch(bool force,bool notify=false)
	{
		if(bearFetching){if(notify)Print($"{currentDc} 正在刷新，请稍候。");return;}
		var target=srankData?.data;
		if(target==null||target.hunts.Length==0||target.worlds.Length==0){if(notify)Print("暂无可刷新的狩猎数据。");return;}
		string dc=target.dc;
		if(!BearRefreshPolicy.CanStart(dc,force,DateTime.UtcNow-lastBearFetch)){if(notify)Print("刷新间隔至少 10 秒，请稍后重试。");return;}
		bool mainSite=BearRefreshPolicy.UsesMainSite(dc);
		bearRequest?.Dispose();
		var request=CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
		request.CancelAfter(TimeSpan.FromMinutes(5));
		bearRequest=request;
		CancellationToken token=request.Token;
		int generation=++bearRequestGeneration;
		bearFetching=true;bearNotify=notify;lastBearFetch=DateTime.UtcNow;
		bearStatus=mainSite?"主站拉取中…":"公开熊站手动加载中…";
		if(notify)Print($"开始刷新 {dc} · {(mainSite?"主站":"公开熊站")}，完成后会提示。");
		log.Information($"[SrankFlag.Bear] 开始加载：{dc} · {(mainSite?"主站":"手动公开熊站")}");
		var pairs=target.hunts.Select(h=>h.en).Where(en=>!string.IsNullOrWhiteSpace(en)).Distinct()
			.SelectMany(hunt=>target.worlds.Select(world=>(h:hunt,w:world))).ToList();
		var elapsed=System.Diagnostics.Stopwatch.StartNew();
		Task.Run(async()=>
		{
			var bag=new ConcurrentDictionary<string,double>(StringComparer.Ordinal);
			var maintenance=new ConcurrentDictionary<string,double>(StringComparer.Ordinal);
			int completed=0,failed=0,batchCount=0,historyQueries=0,total=pairs.Count;
			string? lastError=null;
			bool interrupted=false;
			try
			{
				if(!mainSite)
				{
					try
					{
						var timers=await bearBulk.Value.FetchAsync(target.hunts.Select(h=>h.en).Distinct().ToArray(),target.worlds,token).ConfigureAwait(false);
						foreach(var (key,timer) in timers)
						{
							bag[key]=timer.Timestamp;
							if(timer.Maintenance)maintenance[key]=timer.Timestamp;
						}
						completed=batchCount=bag.Count;
						if(completed==total)return;
						pairs=pairs.Where(pair=>!bag.ContainsKey(pair.h+"|"+pair.w)).ToList();
						if(batchCount>0)
							_ = framework.RunOnTick(()=>
							{
								if(cts.IsCancellationRequested||generation!=bearRequestGeneration)return;
								ApplyBearResult(bag,target,true,maintenance);
								bearStatus=$"公开熊站 · 已批量加载 {batchCount}/{total} · 补查缺失项…";
								nativeHud?.ForceMainRefresh();
							});
					}
					catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
					catch(HttpRequestException ex) when(ex.StatusCode==HttpStatusCode.TooManyRequests)
					{failed++;interrupted=true;lastError="公开熊站限流，请稍后重试";return;}
					catch(Exception ex){log.Warning("[SrankFlag] 公开熊站批量查询暂不可用，退回缺失项查询："+ex.Message);}
				}
				if(mainSite)
				{
					// Only 豆豆柴 may use the main site's aggregation endpoint.
					try
					{
						using var response=await BearHttp.Value.GetAsync(LiveApi+Uri.EscapeDataString(dc),token).ConfigureAwait(false);
						response.EnsureSuccessStatusCode();
						using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
						if(document.RootElement.TryGetProperty("live",out var live)&&live.ValueKind==JsonValueKind.Object)
							foreach(var world in live.EnumerateObject())
								if(world.Value.ValueKind==JsonValueKind.Object&&target.worlds.Contains(world.Name))
									foreach(var hunt in world.Value.EnumerateObject())
									{
										var latest=BearRefreshPolicy.MainSiteLatest(hunt.Value);
										if(latest.Timestamp>0)
										{
											string key=hunt.Name+"|"+world.Name;
											bag[key]=latest.Timestamp;
											if(latest.Maintenance)maintenance[key]=latest.Timestamp;
										}
									}
					}
					catch(OperationCanceledException){throw;}
					catch(Exception ex){log.Warning("[SrankFlag] 豆豆柴主站拉取失败："+ex.Message);}
					if(!bag.IsEmpty)return;
					if(DateTime.UtcNow-lastBearFallback<TimeSpan.FromMinutes(60))return;
					lastBearFallback=DateTime.UtcNow;
					pairs=pairs.Take(60).ToList();
					total=pairs.Count;
				}
				// Other three DCs reach this branch only after an explicit manual refresh.
				await Parallel.ForEachAsync(pairs,new ParallelOptions{MaxDegreeOfParallelism=4,CancellationToken=token},async(pair,ct)=>
				{
					try
					{
						Interlocked.Increment(ref historyQueries);
						var result=await BearQueryAsync(pair.h,pair.w,ct).ConfigureAwait(false);
						bag[result.hunt+"|"+result.world]=result.ts;
					}
					catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
					catch(Exception ex)
					{
						lastError=ex.Message;
						if(ex is HttpRequestException {StatusCode:HttpStatusCode.TooManyRequests})request.Cancel();
						if(Interlocked.Increment(ref failed)>=6&&bag.IsEmpty)request.Cancel();
					}
					int done=Interlocked.Increment(ref completed);
					if(done%8==0&&!cts.IsCancellationRequested&&!token.IsCancellationRequested)
						framework.RunOnTick(()=>
						{
							if(cts.IsCancellationRequested||generation!=bearRequestGeneration)return;
							ApplyBearResult(bag,target,true,maintenance);
							bearStatus=$"公开熊站 · {done}/{total} · {bag.Values.Count(value=>value>0)} 条记录 · {failed} 次失败";
							nativeHud?.ForceMainRefresh();
						});
				}).ConfigureAwait(false);
			}
			catch(OperationCanceledException){interrupted=true;}
			catch(Exception ex){failed++;log.Warning("[SrankFlag] 熊站加载失败："+ex.Message);}
			finally
			{
				if(!cts.IsCancellationRequested)
					framework.RunOnTick(()=>
					{
						if(cts.IsCancellationRequested||generation!=bearRequestGeneration)return;
						ApplyBearResult(bag,target,true,maintenance);
						bearFetching=false;
						bearNotify=false;
						string origin=mainSite?"主站":"公开熊站";
						bearStatus=bag.IsEmpty?$"{origin}暂不可用 · 点击刷新重试":$"{origin}已加载 {bag.Values.Count(value=>value>0)} 条记录"+((failed>0||interrupted)?$" · 部分未完成（{failed} 次失败）":"");
						log.Information($"[SrankFlag.Bear] 加载结束：{dc} · {bag.Count} 条 · {completed}/{total} · {failed} 次失败 · 批量 {batchCount} · 逐条 {historyQueries} · {elapsed.ElapsedMilliseconds}ms"+(lastError==null?"":" · "+lastError));
						if(notify)Print(BearRefreshPolicy.CompletionNotice(dc,origin,bag.Values.Count(value=>value>0),bag.Count,failed,interrupted)+$" · 耗时 {elapsed.Elapsed.TotalSeconds:F1} 秒");
						nativeHud?.ForceMainRefresh();
					});
			}
		});
	}

	private void MaybeStartPointsFetch(bool force)
	{
		if(!BearRefreshPolicy.UsesMainSite(currentDc))return;
		if (pointsFetching || (!force && DateTime.UtcNow - lastPointsFetch < PointsInterval))
		{
			return;
		}
		pointsFetching = true;
		lastPointsFetch = DateTime.UtcNow;
		Task.Run(async () =>
		{
			string err = null;
			Dictionary<string, LiveMap> maps = new Dictionary<string, LiveMap>(StringComparer.Ordinal);
			Dictionary<string, Dictionary<int, char>> states = new Dictionary<string, Dictionary<int, char>>(StringComparer.Ordinal);
			double updated = 0.0;
			try
			{
				using HttpResponseMessage resp = await BearHttp.Value.GetAsync("http://119.29.119.179/api/tracker-points/snapshot").ConfigureAwait(continueOnCapturedContext: false);
				resp.EnsureSuccessStatusCode();
				using JsonDocument jsonDocument = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false));
				JsonElement rootElement = jsonDocument.RootElement;
				if (rootElement.TryGetProperty("maps", out var value) && value.ValueKind == JsonValueKind.Object)
				{
					foreach (JsonProperty item in value.EnumerateObject())
					{
						LiveMap liveMap = new LiveMap();
						if (item.Value.TryGetProperty("pointIds", out var value2))
						{
							liveMap.ids = ReadInts(value2);
						}
						if (item.Value.TryGetProperty("pts", out var value3))
						{
							liveMap.pts = ReadPts(value3);
						}
						if (item.Value.TryGetProperty("bmob", out var value4))
						{
							liveMap.bmob = ReadInts(value4);
						}
						if (liveMap.ids.Length != 0)
						{
							maps[item.Name] = liveMap;
						}
					}
				}
				if (rootElement.TryGetProperty("rows", out var value5) && value5.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item2 in value5.EnumerateArray())
					{
						if (item2.ValueKind == JsonValueKind.Object)
						{
							string text = (item2.TryGetProperty("world", out var value6) ? (value6.GetString() ?? "") : "");
							string text2 = (item2.TryGetProperty("hunt", out var value7) ? (value7.GetString() ?? "") : "");
							int num = ((!item2.TryGetProperty("line", out var value8) || value8.ValueKind != JsonValueKind.Number) ? 1 : value8.GetInt32());
							if (MainSiteWorld(text) && text2.Length != 0)
							{
								Dictionary<int, char> dictionary = new Dictionary<int, char>();
								if (item2.TryGetProperty("points", out var value9) && value9.ValueKind == JsonValueKind.Object)
								{
									foreach (JsonProperty item3 in value9.EnumerateObject())
									{
										if (int.TryParse(item3.Name, out var result) && item3.Value.ValueKind == JsonValueKind.Object && item3.Value.TryGetProperty("state", out var value10) && value10.ValueKind == JsonValueKind.True)
										{
											bool flag = item3.Value.TryGetProperty("verified", out var value11) && value11.ValueKind == JsonValueKind.True;
											bool flag2 = item3.Value.TryGetProperty("admin", out var value12) && value12.ValueKind == JsonValueKind.True;
											Dictionary<int, char> dictionary2 = dictionary;
											int key = result;
											char value13 = (flag2 ? 'a' : (flag ? 'r' : 'y'));
											dictionary2[key] = value13;
										}
									}
								}
								states[text + "|" + text2 + "|" + num] = dictionary;
							}
						}
					}
				}
				if (rootElement.TryGetProperty("updatedAt", out var value14) && value14.ValueKind == JsonValueKind.Number)
				{
					updated = value14.GetDouble();
				}
			}
			catch (Exception ex)
			{
				err = ex.Message;
			}
			framework.RunOnTick(() =>
			{
				pointsFetching = false;
				if (err != null)
				{
					pointsStatus = "排点同步失败";
				}
				else
				{
					liveMaps.Clear();
					foreach (KeyValuePair<string, LiveMap> item4 in maps)
					{
						liveMaps[item4.Key] = item4.Value;
					}
					liveStates.Clear();
					foreach (KeyValuePair<string, Dictionary<int, char>> item5 in states)
					{
						liveStates[item5.Key] = item5.Value;
					}
					pointsUpdatedAt = ((updated > 0.0) ? updated : ((double)DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
					pointsStatus = "排点 " + DateTimeOffset.FromUnixTimeSeconds((long)pointsUpdatedAt).ToLocalTime().ToString("HH:mm");
					if (!pointsProbeLogged)
					{
						pointsProbeLogged = true;
						log.Info($"[SrankFlag] 实时排点已接入：{liveMaps.Count} 张图 / {liveStates.Count} 条服·线");
					}
				}
			});
		});
	}

	private static int[] ReadInts(JsonElement e)
	{
		if (e.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<int>();
		}
		List<int> list = new List<int>();
		foreach (JsonElement item in e.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Number)
			{
				list.Add(item.GetInt32());
			}
		}
		return list.ToArray();
	}

	private static float[][] ReadPts(JsonElement e)
	{
		if (e.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<float[]>();
		}
		List<float[]> list = new List<float[]>();
		foreach (JsonElement item in e.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Array)
			{
				continue;
			}
			JsonElement.ArrayEnumerator arrayEnumerator2 = item.EnumerateArray();
			if (arrayEnumerator2.MoveNext())
			{
				float single = arrayEnumerator2.Current.GetSingle();
				if (arrayEnumerator2.MoveNext())
				{
					list.Add(new float[2]
					{
						single,
						arrayEnumerator2.Current.GetSingle()
					});
				}
			}
		}
		return list.ToArray();
	}

	private List<Pt>? LiveGreens(string huntEn, string world)
	{
		if (liveMaps.Count == 0)
		{
			return null;
		}
		if (!liveMaps.TryGetValue(huntEn, out LiveMap value) || value.ids.Length == 0)
		{
			return null;
		}
		int num = BestPointLine(world);
		Dictionary<int, char> dictionary = null;
		Dictionary<int, char> value3;
		if (liveStates.TryGetValue(world + "|" + huntEn + "|" + num, out Dictionary<int, char> value2))
		{
			dictionary = value2;
		}
		else if (num != 1 && liveStates.TryGetValue(world + "|" + huntEn + "|1", out value3))
		{
			dictionary = value3;
		}
		if (dictionary == null)
		{
			return null;
		}
		HashSet<int> hashSet = new HashSet<int>(value.bmob);
		List<Pt> list = new List<Pt>();
		for (int i = 0; i < value.ids.Length; i++)
		{
			int num2 = value.ids[i];
			if (!hashSet.Contains(num2) && !dictionary.ContainsKey(num2))
			{
				float[] array = ((i < value.pts.Length) ? value.pts[i] : new float[2]);
				list.Add(new Pt(num2, array[0], array[1], 'g'));
			}
		}
		return list;
	}

	private int BestPointLine(string world)
	{
		try
		{
			if (!string.Equals(CurrentWorldName(), world, StringComparison.OrdinalIgnoreCase))
			{
				return 1;
			}
			int currentInstance = (int)GetCurrentInstance();
			return (currentInstance <= 0) ? 1 : currentInstance;
		}
		catch
		{
			return 1;
		}
	}

	private void ApplyBearResult(ConcurrentDictionary<string, double> map, SnapshotData? target=null,bool authoritative=false,IReadOnlyDictionary<string,double>? maintenance=null)
	{
		SnapshotData snapshotData = target??srankData?.data;
		if (snapshotData == null)
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		HuntData[] hunts = snapshotData.hunts;
		foreach (HuntData huntData in hunts)
		{
			if (huntData.worlds.Length == 0)
			{
				continue;
			}
			bool flag = false;
			WorldDeath[] worlds = huntData.worlds;
			foreach (WorldDeath worldDeath in worlds)
			{
				if (map.TryGetValue(huntData.en + "|" + worldDeath.world, out var value) && double.IsFinite(value)&&value>=0)
				{
					num++;
					double reported=value;
					sonarKills.TryGetValue(snapshotData.dc+"|"+huntData.en+"|"+worldDeath.world,out double sonar);
					value=BearRefreshPolicy.ResolveDeath(worldDeath.lastDeath,value,sonar,authoritative);
					if(authoritative||value==reported)worldDeath.isMaint=value==reported&&maintenance is not null&&maintenance.TryGetValue(huntData.en+"|"+worldDeath.world,out double maintTime)&&maintTime==value;
					if (value != worldDeath.lastDeath)
					{
						bool newer=value>worldDeath.lastDeath;
						worldDeath.lastDeath = value;
						flag = true;
						if(newer&&value>0)
						{
							if(!worldDeath.isMaint)history?.Add(huntData.cn,worldDeath.world,huntData.map,HistoryEventType.Kill,BearRefreshPolicy.UsesMainSite(snapshotData.dc)?HistorySource.MainSite:HistorySource.Bear,value);
							ClearTriggered(huntData.en,worldDeath.world,"击杀时间更新",value);
							hashSet.Add(TrigKey(huntData.en,worldDeath.world));
						}
					}
				}
			}
			if (flag)
			{
				double num3 = huntData.worlds.Max((WorldDeath x) => x.lastDeath);
				if (authoritative||num3 > huntData.lastDeath)
				{
					huntData.lastDeath = num3;
				}
				num2++;
			}
		}
		if(num>0){snapshotData.snapshot = DateTimeOffset.UtcNow.ToUnixTimeSeconds();lastBearOk = DateTime.UtcNow;}
		SaveBearCache();
		if(ReferenceEquals(snapshotData,srankData?.data))bearStatus = ((num == 0) ? $"暂未获取有效记录 · {DateTime.Now:HH:mm}" : $"已同步 {num} 条 · {DateTime.Now:HH:mm}");
		NoteMainSiteOpenStates(snapshotData);
		SweepTriggeredByDeath();
		if(BearRefreshPolicy.UsesMainSite(snapshotData.dc))ApplyRelayTriggered(hashSet);
		nativeHud?.ForceHistoryRefresh();
	}

	private void NoteMainSiteOpenStates(SnapshotData d)
	{
		if (history == null)
		{
			return;
		}
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		try
		{
			HuntData[] hunts = d.hunts;
			foreach (HuntData huntData in hunts)
			{
				WorldDeath[] worlds = huntData.worlds;
				foreach (WorldDeath worldDeath in worlds)
				{
					if (worldDeath.lastDeath <= 0.0)
					{
						continue;
					}
					double num2 = Math.Max(worldDeath.lastDeath, restartAt);
					int num3;
					double num4;
					if (restartAt > 0.0||worldDeath.isMaint)
					{
						num3 = ((restartAt >= worldDeath.lastDeath||worldDeath.isMaint) ? 1 : 0);
						if (num3 != 0 && huntData.mt.Length >= 1)
						{
							num4 = huntData.mt[0];
							goto IL_00ad;
						}
					}
					else
					{
						num3 = 0;
					}
					num4 = huntData.cd;
					goto IL_00ad;
					IL_00ad:
					double num5 = num4;
					double winH = ((num3 != 0 && huntData.mt.Length >= 2) ? huntData.mt[1] : huntData.win);
					string item = Calc(num2, num, num5, winH).State;
					double eventTime = num2 + ((num5 > 0.0) ? num5 : 84.0) * 3600.0;
					history.NoteOpenState(d.dc + "|" + huntData.en + "|" + worldDeath.world, item == "open", huntData.cn, worldDeath.world, huntData.map, eventTime,BearRefreshPolicy.UsesMainSite(d.dc)?HistorySource.MainSite:HistorySource.Bear);
				}
			}
		}
		catch (Exception ex)
		{
			try
			{
				log.Warning("[SrankFlag.History] 主站进闸记录失败：" + ex.Message);
			}
			catch
			{
			}
		}
	}

	private static (string State, double Eta, double Pct) Calc(double lastDeath, double now, double cdH = 0.0, double winH = 0.0)
	{
		return HuntWindowDisplay.Calculate(lastDeath,now,cdH,winH);
	}

	private static string Dur(double sec)
	{
		long num = Math.Max(0L, (long)sec);
		long num2 = num / 86400;
		long num3 = num % 86400 / 3600;
		long value = num % 3600 / 60;
		if (num2 <= 0)
		{
			if (num3 > 0)
			{
				return $"{num3}h{value:00}m";
			}
			return $"{value}m";
		}
		return $"{num2}d{num3:00}h";
	}

	private static Vector4 StateColor(string st)
	{
		return st switch
		{
			"open" => V1Lime,
			"cd" => V1Mint,
			"over" => V1Pink,
			_ => V1Dim,
		};
	}

	private static Vector4 Hex(string hex)
	{
		int num = Convert.ToInt32(hex.TrimStart('#'), 16);
		return new Vector4((float)((num >> 16) & 0xFF) / 255f, (float)((num >> 8) & 0xFF) / 255f, (float)(num & 0xFF) / 255f, 1f);
	}

	private static Vector4 PatchColor(string key)
	{
		return key switch
		{
			"HW" => Hex("#3E86D6"),
			"SB" => Hex("#C05B45"),
			"ShB" => Hex("#8B5BD6"),
			"EW" => Hex("#2FA3B5"),
			"DT" => Hex("#C6A046"),
			_ => Hex("#7D8894"),
		};
	}

	private static Vector4 Mix(Vector4 a, Vector4 b, float t)
	{
		return new Vector4(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, 1f);
	}

	private void PointTiles(string map, string huntEn, string world, string huntCn, List<Pt> greens, float gap, int perRow = 0, float tileH = 28f)
	{
		string text = huntEn + "|" + world;
		bool flag = tilesExpanded.Contains(text);
		float num = ImGui.GetWindowPos().X + ImGui.GetContentRegionMax().X;
		int num2 = ((perRow > 0) ? perRow : Math.Max(1, (int)Math.Floor((num - (ImGui.GetItemRectMax().X + gap) + 4f) / 46f)));
		bool flag2 = greens.Count > num2;
		int num3 = ((flag2 && !flag) ? Math.Max(1, num2 - 1) : greens.Count);
		for (int i = 0; i < num3; i++)
		{
			Pt pt = greens[i];
			ImU8String strId = new ImU8String(2, 3);
			strId.AppendFormatted(huntEn);
			strId.AppendLiteral("|");
			strId.AppendFormatted(world);
			strId.AppendLiteral("|");
			strId.AppendFormatted(pt.Id);
			ImGui.PushID(strId);
			if (perRow > 0)
			{
				if (i % perRow != 0)
				{
					ImGui.SameLine(0f, 4f);
				}
			}
			else if (i == 0)
			{
				ImGui.SameLine(0f, gap);
			}
			else if (ImGui.GetItemRectMax().X + 4f + 42f <= num + 0.5f)
			{
				ImGui.SameLine(0f, 4f);
			}
			Vector4 col = PtColor(pt.S);
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
			ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(col.X, col.Y, col.Z, 0.16f));
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(col.X, col.Y, col.Z, 0.35f));
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(col.X, col.Y, col.Z, 0.5f));
			ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(col.X, col.Y, col.Z, 0.4f));
			ImGui.PushStyleColor(ImGuiCol.Text, col);
			if (ImGui.Button(pt.Id.ToString(), new Vector2(42f, tileH)))
			{
				Flag(map, pt.X, pt.Y);
			}
			ImGui.PopStyleColor(5);
			ImGui.PopStyleVar();
			if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
			{
				NavTo(map, pt.X, pt.Y, "", huntCn);
			}
			if (ImGui.IsItemHovered())
			{
				ImU8String tooltip = new ImU8String(33, 4);
				tooltip.AppendFormatted(pt.Id);
				tooltip.AppendLiteral(" 号点 · ");
				tooltip.AppendFormatted(PtLabel(pt.S));
				tooltip.AppendLiteral(" · X ");
				tooltip.AppendFormatted(pt.X, "0.0");
				tooltip.AppendLiteral(" Y ");
				tooltip.AppendFormatted(pt.Y, "0.0");
				tooltip.AppendLiteral("\n左键=插旗 · 右键=插旗+飞行寻路");
				ImGui.SetTooltip(tooltip);
			}
			ImGui.PopID();
		}
		if (!flag2)
		{
			return;
		}
		bool flag3 = ((perRow > 0) ? (num3 % perRow == 0) : (ImGui.GetItemRectMax().X + 4f + 40f > num + 0.5f));
		if (num3 > 0 && !flag3)
		{
			ImGui.SameLine(0f, 4f);
		}
		ImGui.PushID(text + "|more");
		ImGui.PushStyleColor(ImGuiCol.Button, V1Bg2);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, V1Card);
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, V1Card);
		ImGui.PushStyleColor(ImGuiCol.Text, V1Dim);
		if (ImGui.Button(flag ? "-" : $"+{greens.Count - num3}", new Vector2(40f, tileH)))
		{
			if (flag)
			{
				tilesExpanded.Remove(text);
			}
			else
			{
				tilesExpanded.Add(text);
			}
		}
		ImGui.PopStyleColor(4);
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip(flag ? "收起全部绿点" : $"还有 {greens.Count - num3} 个绿点，点开全部显示");
		}
		ImGui.PopID();
	}

	private static Vector4 PtColor(char s)
	{
		Vector4 result = s switch
		{
			'g' => new Vector4(0.2f, 0.85f, 0.35f, 1f),
			'y' => new Vector4(0.9f, 0.8f, 0.25f, 1f),
			'r' => new Vector4(0.85f, 0.3f, 0.3f, 1f),
			'a' => new Vector4(0.55f, 0.55f, 0.55f, 1f),
			_ => new Vector4(0.35f, 0.35f, 0.35f, 1f),
		};
		if (!lightTheme)
		{
			return result;
		}
		return new Vector4(result.X * 0.62f, result.Y * 0.62f, result.Z * 0.62f, result.W);
	}

	private static string PtLabel(char s)
	{
		return s switch
		{
			'g' => "可踩",
			'y' => "玩家排除",
			'r' => "熊站排除",
			'a' => "管理员排除",
			_ => "B怪点",
		};
	}

	private static List<Pt> HuntPoints(PointSet? ps, string world)
	{
		List<Pt> list = new List<Pt>();
		if (ps == null)
		{
			return list;
		}
		PointFlags pointFlags = (ps.w.TryGetValue(world, out PointFlags value) ? value : null);
		for (int i = 0; i < ps.ids.Length; i++)
		{
			int num = ps.ids[i];
			float[] array = ((i < ps.pts.Length) ? ps.pts[i] : new float[2]);
			int num2;
			if (!ps.bm.Contains(num))
			{
				bool? flag;
				if (pointFlags == null)
				{
					flag = null;
				}
				else
				{
					int[] a = pointFlags.a;
					flag = ((a != null) ? new bool?(a.Contains(num)) : ((bool?)null));
				}
				bool? flag2 = flag;
				if (flag2 != true)
				{
					bool? flag3;
					if (pointFlags == null)
					{
						flag3 = null;
					}
					else
					{
						int[] r = pointFlags.r;
						flag3 = ((r != null) ? new bool?(r.Contains(num)) : ((bool?)null));
					}
					flag2 = flag3;
					if (flag2 != true)
					{
						bool? flag4;
						if (pointFlags == null)
						{
							flag4 = null;
						}
						else
						{
							int[] y = pointFlags.y;
							flag4 = ((y != null) ? new bool?(y.Contains(num)) : ((bool?)null));
						}
						flag2 = flag4;
						num2 = ((flag2 == true) ? 121 : 103);
					}
					else
					{
						num2 = 114;
					}
				}
				else
				{
					num2 = 97;
				}
			}
			else
			{
				num2 = 98;
			}
			char s = (char)num2;
			list.Add(new Pt(num, array[0], array[1], s));
		}
		return list;
	}

	private static string DecodeBuf(byte[] buf)
	{
		int num = Array.IndexOf(buf, (byte)0);
		return Encoding.UTF8.GetString(buf, 0, (num < 0) ? buf.Length : num);
	}

	private static void BarAt(ImDrawListPtr dl, Vector2 p, float w, float hgt, Vector4 c, float pct)
	{
		float rounding = Math.Max(2f, hgt * 0.5f);
		dl.AddRectFilled(p, p + new Vector2(w, hgt), ImGui.GetColorU32(V1Track), rounding);
		float num = w * Math.Clamp(pct / 100f, 0f, 1f);
		if (!(num > 1f))
		{
			return;
		}
		dl.AddRectFilled(p, p + new Vector2(num, hgt), ImGui.GetColorU32(c), rounding);
		if (num > 5f)
		{
			dl.AddRectFilledMultiColor(new Vector2(p.X + 1.5f, p.Y + 1f), new Vector2(p.X + num - 1.5f, p.Y + hgt * 0.55f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.2f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.2f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)));
		}
		for (int i = 1; i < 10; i++)
		{
			float num2 = p.X + w * (float)i / 10f;
			if (num2 < p.X + num - 1f)
			{
				dl.AddLine(new Vector2(num2, p.Y + 1f), new Vector2(num2, p.Y + hgt - 1f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.22f)));
			}
		}
	}

	private static void MiniBar(ImDrawListPtr dl, Vector2 p, float w, float hgt, Vector4 c, float pct)
	{
		float rounding = hgt * 0.5f;
		dl.AddRectFilled(p, p + new Vector2(w, hgt), ImGui.GetColorU32(V1Track), rounding);
		float num = w * Math.Clamp(pct / 100f, 0f, 1f);
		if (!(num <= 0.6f))
		{
			dl.AddRectFilled(p, p + new Vector2(num, hgt), ImGui.GetColorU32(c), rounding);
			if (num > 4f)
			{
				dl.AddRectFilledMultiColor(new Vector2(p.X + 1f, p.Y + 0.8f), new Vector2(p.X + num - 1f, p.Y + hgt * 0.55f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.18f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.18f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)));
			}
		}
	}

	private float DrawSearchBar(float width)
	{
		bool flag = expandedHunts.Count > 0;
		float num = Math.Max(BW(bearFetching ? "…  拉取中" : "刷新数据"), BW("…  拉取中"));
		float num2 = Math.Max(BW(flag ? "收起全部" : "展开全部"), BW("展开全部"));
		float num3 = Math.Max(BW("自动前往"), BW("自动"));
		float num4 = width - num - num2 - num3 - 9f - 16f;
		bool flag2 = num4 < 140f;
		if (flag2)
		{
			num = Math.Max(BW(bearFetching ? "…" : "刷新"), BW("刷新"));
			num2 = Math.Max(BW(flag ? "收起" : "展开"), BW("展开"));
			num4 = width - num - num2 - num3 - 9f - 16f;
		}
		byte[] bytes = Encoding.UTF8.GetBytes(searchText);
		Array.Clear(searchBuf);
		Array.Copy(bytes, searchBuf, Math.Min(bytes.Length, searchBuf.Length - 1));
		ImGui.SetNextItemWidth(Math.Max(96f, num4));
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(11f, 8f));
		ImGui.InputTextWithHint("##search", "搜索怪名 / 地图", searchBuf);
		ImGui.PopStyleVar();
		searchText = DecodeBuf(searchBuf);
		ImGui.SameLine(0f, 9f);
		string text;
		if (flag2)
		{
			text = (bearFetching ? "…" : "刷新");
		}
		else
		{
			text = (bearFetching ? "…  拉取中" : "刷新数据");
		}
		if (ImGui.Button(text + "##refresh", new Vector2(num, 32f)))
		{
			if (srankData?.data == null)
			{
				LoadSrankData();
			}
			MaybeStartBearFetch(force: true,notify:true);
			MaybeStartPointsFetch(force: true);
		}
		if (ImGui.IsItemHovered())
		{
			ImU8String tooltip = new ImU8String(20, 1);
			tooltip.AppendLiteral("每 15 分钟自动拉取熊站最新。\n状态：");
			tooltip.AppendFormatted(bearStatus);
			ImGui.SetTooltip(tooltip);
		}
		ImGui.SameLine(0f, 8f);
		string text2;
		if (flag)
		{
			text2 = (flag2 ? "收起" : "收起全部");
		}
		else
		{
			text2 = (flag2 ? "展开" : "展开全部");
		}
		if (ImGui.Button(text2 + "##expandall", new Vector2(num2, 32f)))
		{
			if (flag)
			{
				expandedHunts.Clear();
			}
			else
			{
				foreach (string item in lastListed)
				{
					expandedHunts.Add(item);
				}
			}
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("展开 / 收起所有怪的触发点明细");
		}
		ImGui.SameLine(0f, 8f);
		ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(V1Accent.X, V1Accent.Y, V1Accent.Z, 0.16f));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(V1Accent.X, V1Accent.Y, V1Accent.Z, 0.28f));
		ImGui.PushStyleColor(ImGuiCol.Text, V1Accent);
		if (ImGui.Button((flag2 ? "自动" : "自动前往") + "##openauto", new Vector2(num3, 32f)))
		{
			autoRunOpen = !autoRunOpen;
		}
		ImGui.PopStyleColor(3);
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("打开自动前往悬浮窗：勾选服务器和 S，一路跑完\n（也可以 /水桶 auto）");
		}
		return 32f;
		static float BW(string t)
		{
			return ImGui.CalcTextSize(t).X + 24f;
		}
	}

	private void DrawHuntList()
	{
		SnapshotData data = srankData?.data;
		if (data == null)
		{
			ImGui.TextDisabled("数据未加载：" + ((dataError.Length > 0) ? dataError : "未知原因"));
			if (ImGui.SmallButton("重试加载"))
			{
				LoadSrankData();
			}
			return;
		}
		long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		if (DateTime.UtcNow - lastRestartFetch > TimeSpan.FromSeconds(60L))
		{
			lastRestartFetch = DateTime.UtcNow;
			Task.Run(() => RefreshRestartAsync());
		}
		ImDrawListPtr dl = ImGui.GetWindowDrawList();
		Dictionary<string, int> pri = new Dictionary<string, int>
		{
			["open"] = 0,
			["cd"] = 1,
			["over"] = 2,
			["unknown"] = 3
		};
		List<HuntData> list = data.hunts.ToList();
		List<(HuntData, List<(WorldDeath, string, double, double, double)>, string, double, double, string)> list2 = new List<(HuntData, List<(WorldDeath, string, double, double, double)>, string, double, double, string)>();
		foreach (HuntData h in list)
		{
			if ((patchSel.Count > 0 && !patchSel.Contains(h.patch)) || (searchText.Length > 0 && !(h.cn + " " + h.en + " " + h.map).Contains(searchText, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			List<(WorldDeath, string, double, double, double)> list3 = (from tuple3 in h.worlds.Where((WorldDeath worldDeath) => worldDeath.lastDeath > 0.0 && worldFilter.Contains(worldDeath.world)).Select((WorldDeath worldDeath) =>
				{
					double lastDeath = Math.Max(worldDeath.lastDeath, restartAt);
					int num57;
					double num58;
					if (restartAt > 0.0||worldDeath.isMaint)
					{
						num57 = ((restartAt >= worldDeath.lastDeath||worldDeath.isMaint) ? 1 : 0);
						if (num57 != 0 && h.mt.Length >= 1)
						{
							num58 = h.mt[0];
							goto IL_007e;
						}
					}
					else
					{
						num57 = 0;
					}
					num58 = h.cd;
					goto IL_007e;
					IL_007e:
					(string, double, double) tuple3 = Calc(cdH: num58, winH: (num57 != 0 && h.mt.Length >= 2) ? h.mt[1] : h.win, lastDeath: lastDeath, now: now);
					return (w: worldDeath, State: tuple3.Item1, Eta: tuple3.Item2, Pct: tuple3.Item3, Math.Min(100.0, tuple3.Item3));
				})
				orderby pri.GetValueOrDefault(tuple3.State, 3), tuple3.Eta
				select tuple3).ToList();
			if (list3.Count != 0)
			{
				list2.Add((h, list3, list3[0].Item2, list3[0].Item3, list3[0].Item4, list3[0].Item1.world));
			}
		}
		list2.Sort(((HuntData h, List<(WorldDeath w, string st, double eta, double pct, double fill)> ws, string st, double eta, double pct, string bestWorld) a, (HuntData h, List<(WorldDeath w, string st, double eta, double pct, double fill)> ws, string st, double eta, double pct, string bestWorld) b) =>
		{
			int num57 = pri.GetValueOrDefault(a.st, 3) - pri.GetValueOrDefault(b.st, 3);
			return (num57 == 0) ? a.eta.CompareTo(b.eta) : num57;
		});
		List<(HuntData, List<(WorldDeath, string, double, double, double)>, string, double, double, string)> list4 = (onlyOpen ? list2.Where(((HuntData h, List<(WorldDeath w, string st, double eta, double pct, double fill)> ws, string st, double eta, double pct, string bestWorld) r) => r.st == "open").ToList() : list2);
		lastListed = list4.Select(((HuntData h, List<(WorldDeath w, string st, double eta, double pct, double fill)> ws, string st, double eta, double pct, string bestWorld) r) => r.h.en).ToList();
		int num = Enumerable.Count(list2, ((HuntData h, List<(WorldDeath w, string st, double eta, double pct, double fill)> ws, string st, double eta, double pct, string bestWorld) r) => r.st == "open");
		if (!searchInHeader)
		{
			DrawSearchBar(ImGui.GetContentRegionAvail().X);
		}
		List<SnapshotData> list5 = srankData?.dcs ?? new List<SnapshotData>();
		if (list5.Count > 0)
		{
			int num2 = list5.FindIndex((SnapshotData snapshotData) => snapshotData.dc == currentDc);
			if (num2 < 0)
			{
				num2 = 0;
			}
			string nextDc = list5[(num2 + 1) % list5.Count].dc;
			int value = list5[num2].hunts.Length;
			Chip(currentDc, V1AccentInk, true, () =>
			{
				SelectDc(nextDc);
			}, true, null, false, $"当前大区：{currentDc} · {list5[num2].worlds.Length} 服 · {value} 只 S\n点击切换到 {nextDc}");
		}
		Chip("全部", V1AccentInk, worldFilter.Count == data.worlds.Length, () =>
		{
			if (worldFilter.Count == data.worlds.Length)
			{
				worldFilter.Clear();
			}
			else
			{
				worldFilter.Clear();
				string[] worlds2 = data.worlds;
				foreach (string item8 in worlds2)
				{
					worldFilter.Add(item8);
				}
			}
		}, list5.Count == 0);
		string[] worlds = data.worlds;
		foreach (string w in worlds)
		{
			bool on = worldFilter.Contains(w);
			Chip(w, WorldColor(w), on, () =>
			{
				if (on)
				{
					worldFilter.Remove(w);
				}
				else
				{
					worldFilter.Add(w);
				}
			}, first: false, w, string.Equals(CurrentWorldName(), w, StringComparison.OrdinalIgnoreCase));
		}
		ImGui.Dummy(new Vector2(0f, 2f));
		Dictionary<string, int> dictionary = (from huntData in list
			group huntData by huntData.patch).ToDictionary((IGrouping<string, HuntData> g) => g.Key, (IGrouping<string, HuntData> g) => g.Count());
		bool flag = true;
		(string, string)[] array = new (string, string)[7]
		{
			("ALL", "全部"),
			("ARR", "版本 2.0"),
			("HW", "版本 3.0"),
			("SB", "版本 4.0"),
			("ShB", "版本 5.0"),
			("EW", "版本 6.0"),
			("DT", "版本 7.0")
		};
		for (int num3 = 0; num3 < array.Length; num3++)
		{
			(string, string) tuple = array[num3];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			bool flag2 = item == "ALL";
			bool flag3 = (flag2 ? (patchSel.Count == 0) : patchSel.Contains(item));
			int num4 = ((item == "ALL") ? list.Count : dictionary.GetValueOrDefault(item));
			string text = (flag2 ? "全部" : item2.Replace("版本 ", string.Empty));
			float num5 = TW(text, 15.5f);
			float num6 = 11.4f + num5;
			float num7 = num6 + 20f;
			if (!flag && ImGui.GetCursorPosX() + 7f + num7 <= ImGui.GetContentRegionMax().X + 0.5f)
			{
				ImGui.SameLine(0f, 7f);
			}
			flag = false;
			ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, V1Hover);
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, V1HoverMid);
			ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0f, 0f, 0f, 0f));
			ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0f, 0f, 0f));
			ImU8String label = new ImU8String(5, 1);
			label.AppendLiteral("##tab");
			label.AppendFormatted(item);
			bool flag4 = ImGui.Button(label, new Vector2(num7, 26f));
			ImGui.PopStyleColor(5);
			Vector2 itemRectMin = ImGui.GetItemRectMin();
			Vector2 itemRectMax = ImGui.GetItemRectMax();
			float num8 = itemRectMin.X + (num7 - num6) / 2f;
			float x = num8 + 3.2f;
			float x2 = num8 + 6.4f + 5f;
			Vector4 vector = (flag2 ? V1Accent : PatchColor(item));
			float t;
			if (flag3)
			{
				t = 0.34f;
			}
			else
			{
				t = ((num4 == 0) ? 0.1f : 0.17f);
			}
			Vector4 col = Mix(V1Card, vector, t);
			Vector4 vector2 = (lightTheme ? Mix(vector, V1Text, 0.32f) : vector);
			dl.AddRectFilled(itemRectMin, itemRectMax, ImGui.GetColorU32(col), 7f);
			dl.AddRect(itemRectMin + new Vector2(0.5f, 0.5f), itemRectMax - new Vector2(0.5f, 0.5f), ImGui.GetColorU32(flag3 ? vector2 : V1Border), 7f, ImDrawFlags.None, flag3 ? 1.6f : 1f);
			Vector4 col2 = (flag3 ? vector2 : Mix(V1Bg, vector, (num4 == 0) ? 0.42f : 0.72f));
			float y = (itemRectMin.Y + itemRectMax.Y) / 2f;
			dl.AddCircleFilled(new Vector2(x, y), 3.2f, ImGui.GetColorU32(col2), 14);
			if (flag3)
			{
				dl.AddCircle(new Vector2(x, y), 6.5f, ImGui.GetColorU32(new Vector4(vector2.X, vector2.Y, vector2.Z, 0.55f)), 18, 1.2f);
			}
			dl.AddText(FontFor(15.5f), 15.5f, new Vector2(x2, itemRectMin.Y + 2.9250002f), ImGui.GetColorU32(flag3 ? V1Text : Mix(V1Text, V1Bg, 0.25f)), text);
			if (flag4)
			{
				if (flag2)
				{
					patchSel.Clear();
				}
				else if (!patchSel.Remove(item))
				{
					patchSel.Add(item);
				}
			}
			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip(flag2 ? $"全部版本 · {num4} 只\n（点一下清空版本筛选）" : $"{item2} · {num4} 只\n左键多选：可同时看多个版本");
			}
		}
		float num9 = TW("只看开闸", 14f) + 48f;
		float num10 = TW("亮色", 14f) + 48f;
		float num11 = num9 + num10 + 7f;
		if (ImGui.GetContentRegionMax().X - ImGui.GetCursorPosX() >= num11 + 16f)
		{
			ImGui.SameLine(ImGui.GetContentRegionMax().X - num11);
		}
		Chip("只看开闸", onlyOpen ? V1AccentInk : V1Dim, onlyOpen, () =>
		{
			onlyOpen = !onlyOpen;
		}, first: true);
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip(onlyOpen ? $"只显示已进闸（有触发窗口开着）的 S：{num} 只\n已折叠未进闸的 {list2.Count - num} 只，点击显示全部" : "显示全部 S（含冷却中 / 已过窗的未进闸怪），点击只留已进闸的");
		}
		Chip(lightMode ? "暗色" : "亮色", lightMode ? V1AccentInk : V1Dim, lightMode, () =>
		{
			SetLightTheme(!lightMode);
		}, first: false);
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip(lightMode ? "切回近黑科技风" : "切换亮色主题（白天/深色游戏画面下更清楚）");
		}
		if (list4.Count == 0)
		{
			if (list2.Count > 0)
			{
				ImGui.Dummy(new Vector2(0f, 6f));
				ImGui.TextDisabled("NO READY");
				ImU8String text2 = new ImU8String(33, 1);
				text2.AppendLiteral("当前没有开闸的 S，已折叠 ");
				text2.AppendFormatted(list2.Count);
				text2.AppendLiteral(" 只未进闸的怪（冷却中 / 已过窗）。");
				ImGui.TextWrapped(text2);
				ImGui.Dummy(new Vector2(0f, 4f));
				if (ImGui.Button("显示全部 S", new Vector2(132f, 34f)))
				{
					onlyOpen = false;
				}
			}
			else
			{
				ImGui.TextDisabled("NO MATCH");
				ImGui.TextWrapped("没有匹配的 S 怪——试试清空搜索，或把服务器 chips 点回来。");
			}
			return;
		}
		float num12 = 0f;
		float num13 = 0f;
		foreach (HuntData item9 in list)
		{
			num12 = Math.Max(num12, TW(item9.cn, 19f));
			num13 = Math.Max(num13, TW(item9.map, 13f));
		}
		float x3 = ImGui.GetContentRegionAvail().X;
		float num14 = Math.Min(16f + num12 + 18f, x3 * 0.4f);
		float num15 = Math.Min(num13 + 12f, x3 * 0.18f);
		float num16 = TW("100%", 18f) + 26f;
		bool flag5 = x3 < 560f;
		ImGui.BeginChild("huntlist", new Vector2(0f, Math.Max(120f, ImGui.GetContentRegionAvail().Y - 40f)));
		for (int num17 = 0; num17 < list4.Count; ImGui.PopID(), num17++)
		{
			(HuntData, List<(WorldDeath, string, double, double, double)>, string, double, double, string) tuple2 = list4[num17];
			HuntData h2 = tuple2.Item1;
			List<(WorldDeath, string, double, double, double)> item3 = tuple2.Item2;
			string item4 = tuple2.Item3;
			double item5 = tuple2.Item5;
			string item6 = tuple2.Item6;
			bool flag6 = expandedHunts.Contains(h2.en);
			Vector4 col3 = StateColor(item4);
			string text3 = ((item4 == "unknown") ? "—" : $"{Math.Round(item5)}%");
			List<string> list6 = data.worlds.Where((string item8) => worldFilter.Contains(item8)).ToList();
			bool flag7 = list6.Count > 0;
			bool flag8 = list6.Count < data.worlds.Length;
			bool flag9 = list6.Count == 1;
			float num18 = (flag9 ? 12f : 7f);
			float num19 = (flag7 ? Math.Max(52f, (float)list6.Count * num18 + (float)(list6.Count - 1) * 3.5f + 16f) : 52f);
			ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
			float x4 = ImGui.GetContentRegionAvail().X;
			ImGui.PushID(h2.en);
			Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
			bool flag10 = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(cursorScreenPos, cursorScreenPos + new Vector2(x4, num19));
			if (flag6 | flag10)
			{
				windowDrawList.AddRectFilled(col: ImGui.GetColorU32(flag6 ? new Vector4(V1Sel.X, V1Sel.Y, V1Sel.Z, 0.07f) : V1Hover), pMin: cursorScreenPos + new Vector2(2f, 2f), pMax: cursorScreenPos + new Vector2(x4 - 6f, num19 - 2f), rounding: 8f);
			}
			if (ImGui.Selectable("##row", flag6, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(x4, num19)))
			{
				if (flag6)
				{
					expandedHunts.Remove(h2.en);
				}
				else
				{
					expandedHunts.Add(h2.en);
				}
			}
			float num20 = cursorScreenPos.X + x4 - 14f;
			Vector2 pMin = cursorScreenPos + new Vector2(3f, 6f);
			Vector2 pMax = cursorScreenPos + new Vector2(5.5f, num19 - 6f);
			windowDrawList.AddRectFilled(pMin, pMax, ImGui.GetColorU32(new Vector4(col3.X, col3.Y, col3.Z, 0.72f)), 1.25f);
			windowDrawList.AddRectFilled(pMin, new Vector2(pMax.X, pMin.Y + 12f), ImGui.GetColorU32(col3), 1.25f);
			float rowMidY = cursorScreenPos.Y + num19 * 0.5f;
			float num21 = (flag5 ? 12.5f : 13f);
			float num22 = (flag5 ? (rowMidY - (27.699999f + num21 * 1.3f) * 0.5f) : RowTextTop(19f));
			float y2 = (flag5 ? (num22 + 24.699999f + 3f) : RowTextTop(num21));
			windowDrawList.AddText(FontFor(19f), 19f, new Vector2(cursorScreenPos.X + 16f, num22), ImGui.GetColorU32(V1Text), h2.cn);
			float num23 = (flag5 ? 16f : num14);
			windowDrawList.AddText(FontFor(num21), num21, new Vector2(cursorScreenPos.X + num23, y2), ImGui.GetColorU32(V1Dim), h2.map);
			Dictionary<string, (WorldDeath w, string st, double eta, double pct, double fill)> wsMap = new Dictionary<string, (WorldDeath, string, double, double, double)>();
			foreach (var item10 in item3)
			{
				wsMap[item10.Item1.world] = item10;
			}
			float val = (flag5 ? Math.Max(16f + Math.Min(num13, x3 * 0.34f) + 14f, 16f + Math.Min(num12, x3 * 0.42f) + 12f) : (num14 + num15));
			val = Math.Min(val, Math.Max(flag5 ? 16f : num14, x4 - num16 - 80f));
			float num24 = Math.Max(60f, x4 - num16 - val);
			float num25 = (flag9 ? (Tw(list6[0], 12f) + 8f) : 0f);
			float num26 = Math.Max(36f, num24 - num25);
			float num27 = (float)list6.Count * num18 + (float)Math.Max(0, list6.Count - 1) * 3.5f;
			float num28 = cursorScreenPos.Y + Math.Max(2f, (num19 - num27) * 0.5f);
			if (flag7)
			{
				for (int num29 = 0; num29 < list6.Count; num29++)
				{
					string text4 = list6[num29];
					float num30 = num28 + (float)num29 * (num18 + 3.5f);
					float num31 = cursorScreenPos.X + val;
					float num32 = num26;
					if (wsMap.TryGetValue(text4, out (WorldDeath, string, double, double, double) value2))
					{
						MiniBar(windowDrawList, new Vector2(num31, num30), num32, num18, WorldColor(text4), (float)value2.Item5);
					}
					else
					{
						windowDrawList.AddRectFilled(new Vector2(num31, num30), new Vector2(num31 + num32, num30 + num18), ImGui.GetColorU32(new Vector4(V1Track.X, V1Track.Y, V1Track.Z, 0.55f)));
					}
				}
				if (flag9)
				{
					windowDrawList.AddText(FontFor(12f), 12f, new Vector2(cursorScreenPos.X + val + num26 + 8f, rowMidY - 7.7999997f), ImGui.GetColorU32(V1Dim), list6[0]);
				}
				Vector2 mousePos = ImGui.GetMousePos();
				if (ImGui.IsItemHovered() && mousePos.X >= cursorScreenPos.X + val && mousePos.X <= cursorScreenPos.X + val + num24)
				{
					ImGui.SetTooltip(string.Join("\n", list6.Select((string wn) => (!wsMap.TryGetValue(wn, out (WorldDeath, string, double, double, double) value6)) ? (wn + "：无记录") : ($"{wn}：{((value6.Item2 == "open") ? "可踩" : ((value6.Item2 == "cd") ? "冷却" : "过窗"))} · {Math.Round(value6.Item4)}%" + ((value6.Item2 == "cd") ? (" · " + Dur(value6.Item3) + "后") : "")))));
				}
			}
			if (flag8)
			{
				float x5 = num20 - 10f - Tw(text3, 17f);
				windowDrawList.AddText(FontFor(17f), 17f, new Vector2(x5, RowTextTop(17f)), ImGui.GetColorU32(col3), text3);
			}
			float num33 = cursorScreenPos.Y + num19 + 1f;
			float num34 = cursorScreenPos.X + 16f - 4f;
			float num35 = cursorScreenPos.X + Math.Max(60f, x4 - 12f);
			float x6 = (num34 + num35) * 0.5f;
			uint colorU = ImGui.GetColorU32(V1Border);
			uint colorU2 = ImGui.GetColorU32(new Vector4(V1Border.X, V1Border.Y, V1Border.Z, 0f));
			windowDrawList.AddRectFilledMultiColor(new Vector2(num34, num33), new Vector2(x6, num33 + 1f), colorU2, colorU, colorU, colorU2);
			windowDrawList.AddRectFilledMultiColor(new Vector2(x6, num33), new Vector2(num35, num33 + 1f), colorU, colorU2, colorU2, colorU);
			if (!flag6)
			{
				continue;
			}
			bool flag11 = list6.Count == 1;
			bool flag12 = PointHunts.Contains(h2.cn);
			bool flag13 = NoPointHunts.Contains(h2.cn);
			string text5;
			string value3;
			if (flag11)
			{
				text5 = list6[0];
			}
			else if (huntWorld.TryGetValue(h2.en, out value3))
			{
				text5 = value3;
			}
			else
			{
				text5 = ((item6.Length > 0) ? item6 : data.worlds[0]);
			}
			List<Pt> list7 = new List<Pt>();
			if (flag12 && !flag13)
			{
				list7 = LiveGreens(h2.en, text5) ?? (from q in HuntPoints(srankData.points.GetValueOrDefault(h2.en), text5)
					where q.S == 'g'
					select q).ToList();
			}
			bool flag14 = !flag13 && !flag11;
			bool flag15 = list7.Count > 0;
			ImGui.Indent(8f);
			ImDrawListPtr windowDrawList2 = ImGui.GetWindowDrawList();
			Vector2 cursorScreenPos2 = ImGui.GetCursorScreenPos();
			float x7 = ImGui.GetContentRegionAvail().X;
			windowDrawList2.ChannelsSplit(2);
			windowDrawList2.ChannelsSetCurrent(1);
			bool flag16 = DiscardMaps.ContainsKey(h2.map);
			bool flag17 = flag15;
			float x8 = ((flag16 && !flag17) ? 176f : 132f);
			bool flag18 = h2.cn == "顾尼图";
			string text6;
			if (flag18)
			{
				text6 = "黑风海触发";
			}
			else if (flag17)
			{
				text6 = "前往绿点";
			}
			else
			{
				text6 = (flag16 ? "前往大水晶+丢弃" : "前往大水晶");
			}
			if (ImGui.Button(text6 + "##ae" + h2.en, new Vector2(x8, 26f)))
			{
				if (flag18)
				{
					GounituTrigger(text5);
					Print("[" + h2.cn + "] 开始自动触发（切战士 → 传黑风海 → 飞触发点 → 水蛭飞斧 → 挑衅冰海天使 → 等巨大化/最大化/口锥）");
				}
				else if (flag17)
				{
					Pt pt = list7[0];
					GotoResult gotoResult = Goto(h2.map, pt.X, pt.Y, text5, HuntLineOverride(h2.en, text5), h2.cn, nav: true, list7);
					Print("[" + h2.cn + "] " + gotoResult.Message);
				}
				else
				{
					GotoResult gotoResult2 = GotoAetheryte(h2.map, text5, h2.cn);
					Print("[" + h2.cn + "] " + gotoResult2.Message);
					if (gotoResult2.Ok & flag16)
					{
						WaitUntil(() => InMap(h2.map), () =>
						{
							Sched(() =>
							{
								Print(DiscardTrigger().Message);
							}, TimeSpan.FromMilliseconds(2500L));
						}, 240);
					}
				}
			}
			if (ImGui.IsItemHovered())
			{
				if (flag18)
				{
					ImGui.SetTooltip("切战士 → 传黑风海大水晶 → 飞到固定点位 (29.8, 3.9) 自动下坐骑 → 对三个深海水蛭丢飞斧 → 挑衅冰海天使 → 等巨大化/最大化 → 受口锥伤害完成触发");
				}
				else
				{
					string text7;
					if (flag17)
					{
						text7 = $"开图 + 跨服到 {text5} + 传「{h2.map}」大水晶，落地先上坐骑再飞向绿点";
					}
					else
					{
						text7 = (string.IsNullOrWhiteSpace(text5) ? ("开图并传送到「" + h2.map + "」的大水晶" + (flag16 ? "，落地后自动丢弃触发道具" : "")) : ($"跨服到 {text5}，落地后传「{h2.map}」的大水晶" + (flag16 ? "并丢弃触发道具" : "")));
					}
					ImGui.SetTooltip(text7 + PrepHint(h2.cn) + (flag17 ? $"\n可踩绿点 {list7.Count} · {pointsStatus}" : ""));
				}
			}
			if (flag15 && !flag14)
			{
				PointTiles(h2.map, h2.en, text5, h2.cn, list7, 10f);
			}
			else
			{
				string text8 = h2.map + " · " + text5;
				if (h2.kind.Length > 0)
				{
					text8 = text8 + " · " + h2.kind;
				}
				string text9 = PrepHint(h2.cn);
				float num36 = ImGui.GetContentRegionAvail().X - 10f;
				if (text9.Length > 0 && Tw(text8 + text9, 13.5f) < num36)
				{
					text8 += text9;
				}
				if (Tw(text8, 13.5f) <= num36)
				{
					ImGui.SameLine(0f, 10f);
				}
				ImGui.TextDisabled(text8);
			}
			long num37;
			int num38;
			if (ConditionForecast.HasSpec(h2.en))
			{
				num37 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				ConditionForecast.Window? window = ConditionForecast.NextWindow(h2.en, num37);
				if (window.HasValue)
				{
					ConditionForecast.Window valueOrDefault = window.GetValueOrDefault();
					if (valueOrDefault.Start <= (double)num37)
					{
						num38 = (((double)num37 < valueOrDefault.End) ? 1 : 0);
						goto IL_1d86;
					}
				}
				num38 = 0;
				goto IL_1d86;
			}
			goto IL_1e1f;
			IL_1e1f:
			if (flag14)
			{
				ImGui.PushStyleColor(ImGuiCol.Text, V1Dim);
				ImGui.TextUnformatted(flag12 ? "服务器明细 · 点击行切换该服的触发点" : "服务器明细 · 点一行看该服");
				ImGui.PopStyleColor();
				float x9 = ImGui.GetContentRegionAvail().X;
				float num39 = (flag12 ? Math.Clamp(x9 * 0.58f, 236f, Math.Max(236f, x9 - 60f)) : x9);
				float num40 = Math.Max(0f, x9 - num39 - 8f);
				int num41 = Math.Max(1, (int)Math.Floor((num40 + 4f) / 46f));
				float num42 = Math.Clamp(num39 * 0.24f, 72f, 150f);
				float num43 = Math.Clamp(num39 * 0.26f, 84f, 150f);
				float num44 = Math.Max(24f, num39 - num42 - 40f - num43 - 10f);
				worlds = data.worlds;
				foreach (string text10 in worlds)
				{
					bool flag19 = wsMap.TryGetValue(text10, out (WorldDeath, string, double, double, double) value4);
					bool flag20 = text10 == text5;
					List<Pt> list8 = ((flag19 & flag12) ? GreensOf(text10) : new List<Pt>());
					bool flag21 = list8.Count > num41;
					int num45 = ((list8.Count != 0) ? ((!flag21 || tilesExpanded.Contains(h2.en + "|" + text10)) ? list8.Count : Math.Max(1, num41 - 1)) : 0);
					int num46 = ((num45 != 0) ? ((int)Math.Ceiling((double)(num45 + (flag21 ? 1 : 0)) / (double)num41)) : 0);
					float num47 = Math.Max(23f, (num46 > 0) ? ((float)num46 * 24f + (float)(num46 - 1) * 4f + 4f) : 0f);
					Vector2 cursorScreenPos3 = ImGui.GetCursorScreenPos();
					bool flag22 = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(cursorScreenPos3, cursorScreenPos3 + new Vector2(num39, num47));
					if (flag20 | flag22)
					{
						ImGui.GetWindowDrawList().AddRectFilled(cursorScreenPos3 + new Vector2(-2f, 1f), cursorScreenPos3 + new Vector2(num39 - 2f, num47 - 1f), ImGui.GetColorU32(flag20 ? V1Sel : V1Hover), 6f);
					}
					ImU8String label2 = new ImU8String(3, 2);
					label2.AppendLiteral("##");
					label2.AppendFormatted(h2.en);
					label2.AppendLiteral("|");
					label2.AppendFormatted(text10);
					bool flag23 = ImGui.Selectable(label2, flag20, ImGuiSelectableFlags.SpanAllColumns, new Vector2(num39, num47));
					if (flag19 && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
					{
						worldFilter.Clear();
						worldFilter.Add(text10);
						huntWorld[h2.en] = text10;
					}
					else if (flag19 & flag23)
					{
						huntWorld[h2.en] = text10;
					}
					ImDrawListPtr windowDrawList3 = ImGui.GetWindowDrawList();
					Vector4 vector3 = WorldColor(text10);
					float num48 = cursorScreenPos3.Y + num47 / 2f;
					ImFontPtr font = FontFor(15f);
					Vector2 pos = new Vector2(cursorScreenPos3.X + 2f, num48 - 10f);
					Vector4 col4;
					if (flag20)
					{
						col4 = vector3;
					}
					else
					{
						col4 = (flag19 ? V1Dim : V1Faint);
					}
					windowDrawList3.AddText(font, 15f, pos, ImGui.GetColorU32(col4), (flag20 ? "● " : "\u3000") + text10);
					if (!flag19)
					{
						windowDrawList3.AddText(FontFor(13.5f), 13.5f, new Vector2(cursorScreenPos3.X + num42, num48 - 8f), ImGui.GetColorU32(V1Faint), "无记录");
					}
					else
					{
						float num49 = cursorScreenPos3.X + num42;
						BarAt(windowDrawList3, new Vector2(num49, num48 - 4.5f), num44, 9f, vector3, (float)value4.Item5);
						ImFontPtr font2 = FontFor(15f);
						Vector2 pos2 = new Vector2(num49 + num44 + 10f, num48 - 10f);
						uint colorU3 = ImGui.GetColorU32(StateColor(value4.Item2));
						ImU8String text11 = new ImU8String(1, 1);
						text11.AppendFormatted(Math.Round(value4.Item4));
						text11.AppendLiteral("%");
						windowDrawList3.AddText(font2, 15f, pos2, colorU3, text11);
						string text12 = ((num43 >= 108f) ? (((value4.Item2 == "open") ? "可踩" : ((value4.Item2 == "cd") ? "冷却" : "过窗")) + " · " + Dur(value4.Item3)) : Dur(value4.Item3));
						windowDrawList3.AddText(FontFor(13f), 13f, new Vector2(num49 + num44 + 10f + 40f, num48 - 8f), ImGui.GetColorU32(V1Dim), text12);
					}
					if (list8.Count > 0)
					{
						float num50 = (float)num46 * 24f + (float)(num46 - 1) * 4f;
						ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos3.X + num39 + 8f, cursorScreenPos3.Y + (num47 - num50) / 2f));
						PointTiles(h2.map, h2.en, text10, h2.cn, list8, 0f, num41, 24f);
					}
					ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos3.X, cursorScreenPos3.Y + num47));
				}
				if (h2.tips.Length != 0)
				{
					ImGui.Dummy(new Vector2(0f, 2f));
					string item7 = h2.en + "|tips";
					bool flag24 = tipsOpen.Contains(item7);
					string value5 = (h2.counted ? " · 计数" : "") + (h2.prob ? " · 概率" : "");
					ImGui.PushStyleColor(ImGuiCol.Button, Transparent);
					ImGui.PushStyleColor(ImGuiCol.ButtonHovered, V1Hover);
					ImGui.PushStyleColor(ImGuiCol.ButtonActive, V1HoverMid);
					ImGui.PushStyleColor(ImGuiCol.Text, V1Dim);
					ImU8String label3 = new ImU8String(14, 4);
					label3.AppendFormatted(flag24 ? "-" : "+");
					label3.AppendLiteral(" 触发要点 ");
					label3.AppendFormatted(h2.tips.Length);
					label3.AppendLiteral(" 条");
					label3.AppendFormatted(value5);
					label3.AppendLiteral("##tips");
					label3.AppendFormatted(h2.en);
					if (ImGui.SmallButton(label3) && !tipsOpen.Remove(item7))
					{
						tipsOpen.Add(item7);
					}
					ImGui.PopStyleColor(4);
					if (ImGui.IsItemHovered())
					{
						ImGui.SetTooltip("整理自「S级恶名精英狩猎与触发指南」· 点击" + (flag24 ? "收起" : "展开"));
					}
					if (flag24)
					{
						ImGui.PushStyleColor(ImGuiCol.Text, V1Dim);
						ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(120f, ImGui.GetContentRegionAvail().X - 8f));
						worlds = h2.tips;
						foreach (string text13 in worlds)
						{
							ImGui.TextUnformatted("· " + text13);
						}
						ImGui.PopTextWrapPos();
						ImGui.PopStyleColor();
					}
				}
			}
			float y3 = ImGui.GetCursorScreenPos().Y;
			windowDrawList2.ChannelsSetCurrent(0);
			Vector2 vector4 = new Vector2(cursorScreenPos2.X - 10f, cursorScreenPos2.Y - 8f);
			Vector2 vector5 = new Vector2(cursorScreenPos2.X - 10f + x7 + 20f, y3 + 8f);
			windowDrawList2.AddRectFilled(vector4, vector5, ImGui.GetColorU32(V1Panel), 8f);
			windowDrawList2.AddRect(vector4 + new Vector2(0.5f, 0.5f), vector5 - new Vector2(0.5f, 0.5f), ImGui.GetColorU32(new Vector4(V1Border.X, V1Border.Y, V1Border.Z, 0.85f)), 8f);
			windowDrawList2.AddRectFilled(new Vector2(vector4.X + 3f, vector4.Y + 10f), new Vector2(vector4.X + 5.5f, vector5.Y - 10f), ImGui.GetColorU32(new Vector4(col3.X, col3.Y, col3.Z, 0.85f)), 1.25f);
			windowDrawList2.ChannelsMerge();
			ImGui.Indent(-8f);
			ImGui.SetCursorScreenPos(new Vector2(cursorScreenPos2.X - 8f, y3 + 8f));
			continue;
			IL_1d86:
			bool flag25 = (byte)num38 != 0;
			ImGui.Dummy(new Vector2(0f, 3f));
			ImGui.PushTextWrapPos(0f);
			ImGui.PushStyleColor(ImGuiCol.Text, StateColor(flag25 ? "open" : "cd"));
			ImGui.TextUnformatted("条件 · " + ConditionForecast.Describe(h2.en, num37));
			ImGui.PopStyleColor();
			ImGui.PopTextWrapPos();
			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip(ConditionForecast.LabelOf(h2.en) + "\n按本地现实时间推算，与 eorzea-weather / 熊站的条件计时器一致");
			}
			goto IL_1e1f;
			List<Pt> GreensOf(string world)
			{
				return LiveGreens(h2.en, world) ?? (from q in HuntPoints(srankData.points.GetValueOrDefault(h2.en), world)
					where q.S == 'g'
					select q).ToList();
			}
			float RowTextTop(float fs)
			{
				return rowMidY - fs * 0.65f;
			}
		}
		ImGui.EndChild();
		ImDrawListPtr windowDrawList4 = ImGui.GetWindowDrawList();
		float num51 = ImGui.GetCursorScreenPos().Y + 2f;
		float num52 = ImGui.GetWindowPos().X + 16f;
		float num53 = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - 34f;
		float x10 = (num52 + num53) * 0.5f;
		uint colorU4 = ImGui.GetColorU32(V1Border);
		uint colorU5 = ImGui.GetColorU32(new Vector4(V1Border.X, V1Border.Y, V1Border.Z, 0f));
		windowDrawList4.AddRectFilledMultiColor(new Vector2(num52, num51), new Vector2(x10, num51 + 1f), colorU5, colorU4, colorU4, colorU5);
		windowDrawList4.AddRectFilledMultiColor(new Vector2(x10, num51), new Vector2(num53, num51 + 1f), colorU4, colorU5, colorU5, colorU4);
		ImGui.Dummy(new Vector2(0f, 2f));
		ImDrawListPtr windowDrawList5 = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos4 = ImGui.GetCursorScreenPos();
		float textLineHeight = ImGui.GetTextLineHeight();
		float w2 = 0.45f + 0.55f * (float)Math.Sin(ImGui.GetTime() * 2.6);
		windowDrawList5.AddCircleFilled(cursorScreenPos4 + new Vector2(5f, textLineHeight / 2f + 1f), 3.5f, ImGui.GetColorU32(new Vector4(V1Mint.X, V1Mint.Y, V1Mint.Z, w2)), 12);
		string text14 = CurrentWorldName();
		uint currentInstance = GetCurrentInstance();
		string text15 = ((text14.Length > 0) ? ("GAME LINK · " + text14 + ((currentInstance != 0) ? $" · {currentInstance}线" : "")) : "GAME LINK · 未连游戏");
		float num54 = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - 34f - FT(text15);
		windowDrawList5.AddText(FontFor(13f), 13f, cursorScreenPos4 + new Vector2(16f, 1f), ImGui.GetColorU32(V1Mint), "LIVE");
		float num55 = 16f + FT("LIVE") + 12f;
		windowDrawList5.AddText(FontFor(13f), 13f, cursorScreenPos4 + new Vector2(num55, 1f), ImGui.GetColorU32(V1Dim), "BEAR TRACKER");
		float num56 = num55 + FT("BEAR TRACKER") + 10f;
		string text16 = $"· {list2.Count} 只 · 开闸 {num}";
		if (num56 + FT(text16) < num54 - 12f)
		{
			windowDrawList5.AddText(FontFor(13f), 13f, cursorScreenPos4 + new Vector2(num56, 1f), ImGui.GetColorU32((num > 0) ? V1Lime : V1Dim), text16);
			num56 += FT(text16) + 10f;
		}
		string text17 = $"· 快照 {DateTimeOffset.FromUnixTimeSeconds((long)data.snapshot).ToLocalTime():MM-dd HH:mm}";
		if (num56 + FT(text17) < num54 - 12f)
		{
			windowDrawList5.AddText(FontFor(13f), 13f, cursorScreenPos4 + new Vector2(num56, 1f), ImGui.GetColorU32(V1Dim), text17);
			num56 += FT(text17) + 10f;
		}
		if (searchInHeader)
		{
			DateTime utcDateTime = DateTimeOffset.FromUnixTimeSeconds((long)((double)DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1440.0 / 70.0)).UtcDateTime;
			string text18 = $"· {srankData?.data?.dc ?? "—"} · ET {utcDateTime:HH:mm} · LT {DateTime.Now:HH:mm}";
			if (num56 + FT(text18) < num54 - 12f)
			{
				windowDrawList5.AddText(FontFor(13f), 13f, cursorScreenPos4 + new Vector2(num56, 1f), ImGui.GetColorU32(V1Dim), text18);
			}
		}
		windowDrawList5.AddText(FontFor(13f), 13f, new Vector2(num54, cursorScreenPos4.Y + 1f), ImGui.GetColorU32(V1Dim), text15);
		ImGui.Dummy(new Vector2(0f, 19f));
		void Chip(string text19, Vector4 c, bool flag27, System.Action toggle, bool first, string? soloWorld = null, bool flag26 = false, string? tip = null)
		{
			float num57 = TW(text19, 13.5f) + 36f;
			if (!first && ImGui.GetCursorPosX() + 6f + num57 <= ImGui.GetContentRegionMax().X + 0.5f)
			{
				ImGui.SameLine(0f, 6f);
			}
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
			Vector4 col5;
			if (flag26)
			{
				col5 = new Vector4(c.X, c.Y, c.Z, 0.28f);
			}
			else
			{
				col5 = (flag27 ? new Vector4(c.X, c.Y, c.Z, 0.14f) : V1Card);
			}
			ImGui.PushStyleColor(ImGuiCol.Button, col5);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, flag27 ? new Vector4(c.X, c.Y, c.Z, 0.2f) : V1HoverMid);
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(c.X, c.Y, c.Z, 0.26f));
			ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0f, 0f, 0f));
			Vector4 col6;
			if (flag26)
			{
				col6 = new Vector4(c.X, c.Y, c.Z, 1f);
			}
			else
			{
				col6 = (flag27 ? new Vector4(c.X, c.Y, c.Z, 0.75f) : V1Border);
			}
			ImGui.PushStyleColor(ImGuiCol.Border, col6);
			ImU8String label4 = new ImU8String(6, 1);
			label4.AppendLiteral("##chip");
			label4.AppendFormatted(text19);
			bool flag28 = ImGui.Button(label4, new Vector2(num57, 27f));
			ImGui.PopStyleColor(5);
			ImGui.PopStyleVar();
			Vector2 itemRectMin2 = ImGui.GetItemRectMin();
			Vector2 itemRectMax2 = ImGui.GetItemRectMax();
			dl.AddCircleFilled(new Vector2(itemRectMin2.X + 13f, (itemRectMin2.Y + itemRectMax2.Y) / 2f), 3.6f, ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, (flag27 | flag26) ? 1f : 0.5f)), 12);
			if (flag26)
			{
				dl.AddCircle(new Vector2(itemRectMin2.X + 13f, (itemRectMin2.Y + itemRectMax2.Y) / 2f), 6.2f, ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, 0.95f)), 16, 1.5f);
				dl.AddRect(itemRectMin2 - new Vector2(1f, 1f), itemRectMax2 + new Vector2(1f, 1f), ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, 0.9f)), 7f, ImDrawFlags.None, 1.3f);
			}
			ImFontPtr font3 = FontFor(13.5f);
			Vector2 pos3 = new Vector2(itemRectMin2.X + 22f, itemRectMin2.Y + 4.8600006f);
			Vector4 col7;
			if (flag26)
			{
				col7 = V1Text;
			}
			else
			{
				col7 = (flag27 ? c : V1Dim);
			}
			dl.AddText(font3, 13.5f, pos3, ImGui.GetColorU32(col7), text19);
			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip(tip ?? (((soloWorld != null) ? (text19 + "：左键切换 · 右键只看这个服") : (flag27 ? (text19 + "：已启用，点击关闭") : (text19 + "：点击启用"))) + (flag26 ? "\n你现在在这个服务器" : "")));
			}
			if (flag28)
			{
				toggle();
			}
			if (soloWorld != null && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
			{
				worldFilter.Clear();
				worldFilter.Add(soloWorld);
			}
		}
		float FT(string text19)
		{
			return TW(text19, 13f);
		}
		float Tw(string text19, float size)
		{
			return TW(text19, size);
		}
	}

	private string TerritoryName(uint terr)
	{
		try
		{
			TerritoryType? territoryType = dataManager.GetExcelSheet<TerritoryType>()?.GetRow(terr);
			string result;
			if (territoryType.HasValue)
			{
				TerritoryType valueOrDefault = territoryType.GetValueOrDefault();
				if (valueOrDefault.PlaceName.RowId != 0)
				{
					result = valueOrDefault.PlaceName.Value.Name.ToString();
					goto IL_0084;
				}
			}
			result = terr.ToString();
			goto IL_0084;
			IL_0084:
			return result;
		}
		catch
		{
			return terr.ToString();
		}
	}

	private static bool ParseOn(string? s)
	{
		bool flag = !string.IsNullOrWhiteSpace(s);
		if (flag)
		{
			bool flag2;
			switch (s.Trim().ToLowerInvariant())
			{
			case "on":
			case "1":
			case "true":
			case "yes":
			case "开":
			case "开启":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		return flag;
	}

	private string CurrentWorldName()
	{
		try
		{
			return objectTable.LocalPlayer?.CurrentWorld.Value.Name.ToString() ?? "";
		}
		catch
		{
			return "";
		}
	}

	private unsafe uint GetCurrentInstance()
	{
		try
		{
			return UIState.Instance()->PublicInstance.InstanceId;
		}
		catch
		{
			return 0u;
		}
	}

	private void Sched(System.Action act, TimeSpan delay)
	{
		int gen = runGen;
		framework.RunOnTick(() =>
		{
			if (gen != runGen || halted)
			{
				return;
			}
			if(!EnsurePreparationLocation())return;
			try
			{
				act();
			}
			catch (Exception exception)
			{
				log.Error(exception, "[SrankFlag] 延时任务异常");
			}
		}, delay);
	}

	private void StopAll(bool quiet = false)
	{
		preparationLocationGuard=null;
		travelPreparationPending=false;
		if(ownsArtisanCraft){ArtisanSetEndurance(false);ownsArtisanCraft=false;}
		if(!quiet)
		{
			autoRunning=false;autoPaused=false;autoPauseReason="";autoGen++;autoToken++;autoJob=null;autoHit="";autoStatus="已停止";
		}
		halted = true;
		runGen++;
		Say("/vnav stop", force: true);
		if (quiet)
		{
			return;
		}
		string text = "";
		try
		{
			if (condition[ConditionFlag.Casting])
			{
				text = "；正在读条的这一下会照常放出来（读条打断不了），之后不会再有任何动作";
			}
			else if (condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BetweenAreas])
			{
				text = "；当前还在副本里，DailyRoutines 的进退副本会自己把这一轮走完，走完后就不会再动了";
			}
			else if (Dead())
			{
				text = "；你已经倒地，返回大水晶的弹窗留给你自己点";
			}
		}
		catch
		{
		}
		Print("已停止全部工作：路线、上坐骑、到点动作、进退副本都已取消" + text);
	}

	private void Resume()
	{
		halted = false;
	}

	private void Say(string text, bool force = false)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		int generation = runGen;
		try
		{
			framework.RunOnFrameworkThread(() =>
			{
				try
				{
					if (!force && (halted || generation != runGen || !EnsurePreparationLocation()))
					{
						log.Debug("[SrankFlag] 已停止，丢弃命令：" + text);
					}
					else if (!commandManager.ProcessCommand(text))
					{
						ExecuteGameCommand(text);
					}
				}
				catch (Exception exception2)
				{
					log.Error(exception2, "[SrankFlag] 命令执行异常：" + text);
				}
			});
		}
		catch (Exception exception)
		{
			log.Error(exception, "[SrankFlag] Say failed");
		}
	}

	private unsafe void ExecuteGameCommand(string command)
	{
		try
		{
			RaptureShellModule* ptr = RaptureShellModule.Instance();
			if (ptr == null)
			{
				log.Warning("[SrankFlag] RaptureShellModule 不可用，命令没发出去：" + command);
				return;
			}
			UIModule* uiModule = UIModule.Instance();
			Utf8String* ptr2 = Utf8String.FromString(command);
			if (ptr2 == null)
			{
				log.Warning("[SrankFlag] 构造命令字符串失败：" + command);
				return;
			}
			try
			{
				ptr->ExecuteCommandInner(ptr2, uiModule);
			}
			finally
			{
				ptr2->Dtor();
			}
			log.Debug("[SrankFlag] 游戏命令已执行：" + command);
		}
		catch (Exception exception)
		{
			log.Error(exception, "[SrankFlag] 执行游戏命令失败：" + command);
		}
	}

	private List<string> SearchZones(string keyword, int limit)
	{
		List<string> list = new List<string>();
		string value = Normalize(keyword);
		ExcelSheet<Lumina.Excel.Sheets.Map> excelSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
		if (excelSheet == null)
		{
			return list;
		}
		foreach (Lumina.Excel.Sheets.Map item in excelSheet)
		{
			string text = SafeMapName(item);
			if (text.Length != 0 && Normalize(text).Contains(value, StringComparison.Ordinal))
			{
				if (!list.Contains(text))
				{
					list.Add(text);
				}
				if (list.Count >= limit)
				{
					return list;
				}
			}
		}
		return list;
	}

	private static string SafeMapName(Lumina.Excel.Sheets.Map row)
	{
		try
		{
			return (row.PlaceName.RowId == 0) ? string.Empty : row.PlaceName.Value.Name.ToString();
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string SafePlaceName(TerritoryType row)
	{
		try
		{
			return (row.PlaceName.RowId == 0) ? string.Empty : row.PlaceName.Value.Name.ToString();
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string SafePlaceNameFrom(Lumina.Excel.Sheets.Aetheryte row)
	{
		try
		{
			return (row.PlaceName.RowId == 0) ? string.Empty : row.PlaceName.Value.Name.ToString();
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string Normalize(string s)
	{
		return (s ?? string.Empty).Replace(" ", string.Empty).Replace("\u3000", string.Empty).Trim();
	}

	private void Print(string msg)
	{
		try
		{
			framework.RunOnFrameworkThread(() =>
			{
				try
				{
					chatGui.Print("[魔法水桶] " + msg);
				}
				catch
				{
				}
			});
		}
		catch
		{
			log.Information("[SrankFlag] " + msg);
		}
	}

	private void StartServer()
	{
		try
		{
			listener = new TcpListener(IPAddress.Loopback, 18099);
			try
			{
				listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
			}
			catch
			{
			}
			listener.Start();
			Task.Run((Func<Task?>)AcceptLoopAsync);
			log.Information($"[SrankFlag] listening on 127.0.0.1:{18099}");
			Print($"已就绪，接口 127.0.0.1:{18099}（窗口里点触发点即可开图插旗）");
		}
		catch (Exception ex)
		{
			log.Error(ex, "[SrankFlag] listen failed");
			Print($"接口启动失败（端口 {18099} 被占用？）：{ex.Message}");
		}
	}

	private async Task AcceptLoopAsync()
	{
		while (!cts.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await listener.AcceptTcpClientAsync(cts.Token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch
			{
				continue;
			}
			Task.Run(() => HandleAsync(client));
		}
	}

	private async Task HandleAsync(TcpClient client)
	{
		using (client)
		{
			_ = 22;
			try
			{
				client.NoDelay = true;
				NetworkStream stream = client.GetStream();
				var (text, text2, body) = await ReadRequestAsync(stream);
				if (text == null)
				{
					return;
				}
				if (text == "OPTIONS")
				{
					await WriteAsync(stream, 204, "{}");
					return;
				}
				if (text2.StartsWith("/data", StringComparison.OrdinalIgnoreCase))
				{
					if (srankData == null)
					{
						await WriteAsync(stream, 404, Json(ok: false, "数据未加载"));
					}
					else
					{
						await WriteAsync(stream, 200, JsonSerializer.Serialize(srankData));
					}
					return;
				}
				if (text2.StartsWith("/ping", StringComparison.OrdinalIgnoreCase))
				{
					await WriteAsync(stream, 200, JsonSerializer.Serialize(new
					{
						ok = true,
						name = "SrankFlag",
						version = (typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "?"),
						world = CurrentWorldName(),
						instance = GetCurrentInstance(),
						territory = clientState.TerritoryType,
						autoSummon = autoSummonMinion,
						autoLeave = autoLeaveDuty,
						confirmReturn = autoConfirmReturn,
						port = 18099
					}));
					return;
				}
				if (text2.StartsWith("/maps", StringComparison.OrdinalIgnoreCase))
				{
					string text3 = QueryValue(text2, "q") ?? string.Empty;
					List<string> hits = ((text3.Length == 0) ? new List<string>() : SearchZones(text3, 20));
					await WriteAsync(stream, 200, JsonSerializer.Serialize(new
					{
						ok = true,
						q = text3,
						hits = hits
					}));
					return;
				}
				if (text2.StartsWith("/flag", StringComparison.OrdinalIgnoreCase))
				{
					var (text4, x, y) = ParseFlagArgs(text, text2, body);
					if (text4.Length == 0)
					{
						await WriteAsync(stream, 400, Json(ok: false, "map 不能为空"));
						return;
					}
					FlagResult flagResult = Flag(text4, x, y);
					await WriteAsync(stream, flagResult.Ok ? 200 : 404, Json(flagResult.Ok, flagResult.Message, flagResult.TerritoryId, flagResult.MapId, x, y));
					return;
				}
				if (text2.StartsWith("/goto", StringComparison.OrdinalIgnoreCase))
				{
					var (text5, x2, y2, world, line) = ParseGotoArgs(text, text2, body);
					if (text5.Length == 0)
					{
						await WriteAsync(stream, 400, Json(ok: false, "map 不能为空"));
						return;
					}
					GotoResult gotoResult = Goto(text5, x2, y2, world, line);
					await WriteAsync(stream, gotoResult.Ok ? 200 : 404, Json(gotoResult.Ok, gotoResult.Message));
					return;
				}
				if (text2.StartsWith("/telepo", StringComparison.OrdinalIgnoreCase))
				{
					var (text6, x3, y3, _, _) = ParseGotoArgs(text, text2, body);
					if (text6.Length == 0)
					{
						await WriteAsync(stream, 400, Json(ok: false, "map 不能为空"));
						return;
					}
					FlagResult flagResult2 = TeleportNearest(text6, x3, y3);
					await WriteAsync(stream, flagResult2.Ok ? 200 : 404, Json(flagResult2.Ok, flagResult2.Message));
					return;
				}
				if (text2.StartsWith("/worldtravel", StringComparison.OrdinalIgnoreCase))
				{
					string item = ParseGotoArgs(text, text2, body).world;
					FlagResult flagResult3 = WorldTravel(item);
					await WriteAsync(stream, flagResult3.Ok ? 200 : 404, Json(flagResult3.Ok, flagResult3.Message));
					return;
				}
				if (text2.StartsWith("/insc", StringComparison.OrdinalIgnoreCase))
				{
					int item2 = ParseGotoArgs(text, text2, body).line;
					FlagResult flagResult4 = ChangeInstance(item2);
					await WriteAsync(stream, flagResult4.Ok ? 200 : 404, Json(flagResult4.Ok, flagResult4.Message));
					return;
				}
				if (text2.StartsWith("/discard", StringComparison.OrdinalIgnoreCase))
				{
					DiscardResult discardResult = DiscardTrigger();
					await WriteAsync(stream, discardResult.Ok ? 200 : 404, Json(discardResult.Ok, discardResult.Message));
					return;
				}
				if (text2.StartsWith("/weeEa", StringComparison.OrdinalIgnoreCase))
				{
					WeeEaResult weeEaResult = QueryWeeEa();
					await WriteAsync(stream, 200, Json(weeEaResult.Ok, weeEaResult.Message, 0u, 0u, 0f, 0f, weeEaResult.Names));
					return;
				}
				if (text2.StartsWith("/nav", StringComparison.OrdinalIgnoreCase))
				{
					var (text7, x4, y4, world2, _) = ParseGotoArgs(text, text2, body);
					if (text7.Length == 0)
					{
						await WriteAsync(stream, 400, Json(ok: false, "map 不能为空"));
						return;
					}
					FlagResult flagResult5 = NavTo(text7, x4, y4, world2);
					await WriteAsync(stream, flagResult5.Ok ? 200 : 404, Json(flagResult5.Ok, flagResult5.Message));
					return;
				}
				if (text2.StartsWith("/vstop", StringComparison.OrdinalIgnoreCase))
				{
					StopNav();
					await WriteAsync(stream, 200, Json(ok: true, "已发送 /vnav stop"));
					return;
				}
				if (text2.StartsWith("/summonauto", StringComparison.OrdinalIgnoreCase))
				{
					string text8 = QueryValue(text2, "on");
					if (text8 != null)
					{
						autoSummonMinion = ParseOn(text8);
					}
					if (autoSummonMinion)
					{
						RefreshUnlockedMinions();
					}
					SaveAutoSettings();
					await WriteAsync(stream, 200, Json(ok: true, "自动召唤随从：" + (autoSummonMinion ? "已开启" : "已关闭")));
					return;
				}
				if (text2.StartsWith("/leaveauto", StringComparison.OrdinalIgnoreCase))
				{
					string text9 = QueryValue(text2, "on");
					if (text9 != null)
					{
						autoLeaveDuty = ParseOn(text9);
					}
					SaveAutoSettings();
					await WriteAsync(stream, 200, Json(ok: true, "独身自动退本：" + (autoLeaveDuty ? "已开启" : "已关闭")));
					return;
				}
				if (text2.StartsWith("/summon", StringComparison.OrdinalIgnoreCase))
				{
					string text10 = SummonForCurrentTerritory();
					await WriteAsync(stream, 200, Json(text10.StartsWith("已"), text10));
					return;
				}
				await WriteAsync(stream, 404, Json(ok: false, "unknown route"));
			}
			catch (Exception exception)
			{
				log.Warning(exception, "[SrankFlag] request failed");
			}
		}
	}

	private (string map, float x, float y, string world, int line) ParseGotoArgs(string method, string path, string body)
	{
		string item = "";
		string item2 = "";
		float item3 = 0f;
		float item4 = 0f;
		int item5 = 0;
		if (method == "GET")
		{
			item = QueryValue(path, "map") ?? "";
			item3 = ParseFloat(QueryValue(path, "x"));
			item4 = ParseFloat(QueryValue(path, "y"));
			item2 = QueryValue(path, "world") ?? "";
			item5 = (int)ParseFloat(QueryValue(path, "line"));
		}
		else
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
			JsonElement rootElement = jsonDocument.RootElement;
			item = Str(rootElement, "map");
			item2 = Str(rootElement, "world");
			item3 = Flt(rootElement, "x");
			item4 = Flt(rootElement, "y");
			item5 = (int)Flt(rootElement, "line");
		}
		return (map: item, x: item3, y: item4, world: item2, line: item5);
	}

	private (string map, float x, float y) ParseFlagArgs(string method, string path, string body)
	{
		(string, float, float, string, int) tuple = ParseGotoArgs(method, path, body);
		return (map: tuple.Item1, x: tuple.Item2, y: tuple.Item3);
	}

	private static string Str(JsonElement root, string k)
	{
		if (!root.TryGetProperty(k, out var value))
		{
			return "";
		}
		if (value.ValueKind != JsonValueKind.String)
		{
			return value.ToString();
		}
		return value.GetString() ?? "";
	}

	private static float Flt(JsonElement root, string k)
	{
		if (!root.TryGetProperty(k, out var value) || value.ValueKind != JsonValueKind.Number)
		{
			return 0f;
		}
		return value.GetSingle();
	}

	private static string Json(bool ok, string message, uint territory = 0u, uint mapId = 0u, float x = 0f, float y = 0f, List<string>? names = null)
	{
		return JsonSerializer.Serialize(new
		{
			ok = ok,
			message = message,
			territory = territory,
			mapId = mapId,
			x = x,
			y = y,
			names = (names ?? new List<string>())
		});
	}

	private static float ParseFloat(string? s)
	{
		if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0f;
		}
		return result;
	}

	private static async Task<(string? Method, string Path, string Body)> ReadRequestAsync(NetworkStream stream)
	{
		byte[] buf = new byte[8192];
		List<byte> acc = new List<byte>(8192);
		int num;
		while (true)
		{
			num = IndexOfHeaderEnd(acc);
			if (num >= 0)
			{
				break;
			}
			if (acc.Count > 1048576)
			{
				return (Method: null, Path: string.Empty, Body: string.Empty);
			}
			int num2 = await stream.ReadAsync(buf, 0, buf.Length);
			if (num2 <= 0)
			{
				return (Method: null, Path: string.Empty, Body: string.Empty);
			}
			for (int i = 0; i < num2; i++)
			{
				acc.Add(buf[i]);
			}
		}
		string text = Encoding.UTF8.GetString(acc.GetRange(0, num).ToArray());
		int num3 = num + 4;
		string[] array = text.Split("\r\n");
		string[] array2 = array[0].Split(' ');
		string method = ((array2.Length != 0) ? array2[0].ToUpperInvariant() : null);
		string path = ((array2.Length > 1) ? array2[1] : "/");
		int contentLength = 0;
		string[] array3 = array;
		foreach (string text2 in array3)
		{
			if (text2.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
			{
				int.TryParse(text2.Substring(15).Trim(), out contentLength);
			}
		}
		List<byte> bodyBytes = new List<byte>(Math.Max(contentLength, 0));
		if (acc.Count > num3)
		{
			bodyBytes.AddRange(acc.GetRange(num3, acc.Count - num3));
		}
		while (bodyBytes.Count < contentLength)
		{
			int num4 = await stream.ReadAsync(buf, 0, buf.Length);
			if (num4 <= 0)
			{
				break;
			}
			for (int k = 0; k < num4; k++)
			{
				bodyBytes.Add(buf[k]);
			}
		}
		string item = ((bodyBytes.Count == 0) ? string.Empty : Encoding.UTF8.GetString(bodyBytes.ToArray()));
		return (Method: method, Path: path, Body: item);
	}

	private static int IndexOfHeaderEnd(List<byte> b)
	{
		for (int i = 3; i < b.Count; i++)
		{
			if (b[i] == 10 && b[i - 1] == 13 && b[i - 2] == 10 && b[i - 3] == 13)
			{
				return i - 3;
			}
		}
		return -1;
	}

	private static async Task WriteAsync(NetworkStream stream, int status, string json)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(json);
		string value = status switch
		{
			200 => "OK",
			400 => "Bad Request",
			404 => "Not Found",
			204 => "No Content",
			_ => "OK",
		};
		string s = $"HTTP/1.1 {status} {value}\r\n" + "Content-Type: application/json; charset=utf-8\r\n" + $"Content-Length: {bytes.Length}\r\n" + "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: *\r\nAccess-Control-Allow-Methods: GET,POST,OPTIONS\r\nAccess-Control-Allow-Private-Network: true\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
		await stream.WriteAsync(Encoding.ASCII.GetBytes(s));
		await stream.WriteAsync(bytes);
		await stream.FlushAsync();
	}

	private static string? QueryValue(string path, string key)
	{
		int num = path.IndexOf('?');
		if (num < 0)
		{
			return null;
		}
		string[] array = path.Substring(num + 1).Split('&');
		foreach (string text in array)
		{
			int num2 = text.IndexOf('=');
			if (num2 > 0 && text.Substring(0, num2).Equals(key, StringComparison.OrdinalIgnoreCase))
			{
				return Uri.UnescapeDataString(text.Substring(num2 + 1).Replace('+', ' '));
			}
		}
		return null;
	}

	private void OnSonarKillChatMessage(IHandleableChatMessage msg)
	{
		try
		{
			string text = msg?.Message?.TextValue ?? string.Empty;
			int length = text.Length;
			bool flag = ((length < 24 || length > 200) ? true : false);
			if (flag || text.IndexOf("Rank S:", StringComparison.Ordinal) < 0)
			{
				return;
			}
			string text2 = CleanSonarText(text);
			bool flag2 = text2.IndexOf("was just killed", StringComparison.Ordinal) >= 0;
			bool isReset = false;
			Match match;
			if (flag2)
			{
				match = SonarKillRx.Match(text2);
			}
			else
			{
				match = SonarResetRx.Match(text2);
				isReset = match.Success;
				if (!match.Success)
				{
					match = SonarSpawnRx.Match(text2);
				}
			}
			double result;
			double result2;
			if (!match.Success)
			{
				if (flag2)
				{
					NoteSonarUnmatched(text2);
				}
			}
			else if (double.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out result2))
			{
				SonarHit hit = new SonarHit(match.Groups["head"].Value.Trim(), match.Groups["world"].Value.Trim(), result, result2, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), flag2, isReset);
				framework.RunOnTick(() =>
				{
					ApplySonarHit(hit);
				});
			}
		}
		catch (Exception ex)
		{
			try
			{
				log.Warning("[SrankFlag.Sonar] 聊天解析异常：" + ex.Message);
			}
			catch
			{
			}
		}
	}

	private static string CleanSonarText(string raw)
	{
		StringBuilder stringBuilder = new StringBuilder(raw.Length);
		foreach (char c in raw)
		{
			if (c < '\ue000' || c > '\uf8ff')
			{
				bool flag;
				switch (c)
				{
				case '\t':
				case '\n':
				case '\r':
				case '\u00a0':
				case '\u2007':
				case '\u202f':
				case '\u3000':
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (flag)
				{
					stringBuilder.Append(' ');
				}
				else
				{
					stringBuilder.Append(c);
				}
			}
		}
		return stringBuilder.ToString().Trim();
	}

	private void NoteSonarUnmatched(string text)
	{
		sonarUnmatched++;
		if (sonarUnmatchedSample.Length == 0)
		{
			sonarUnmatchedSample = ((text.Length > 160) ? text.Substring(0, 160) : text);
		}
		if (sonarUnmatched <= 3)
		{
			log.Information("[SrankFlag.Sonar] 疑似播报但未匹配（已忽略）：" + sonarUnmatchedSample);
		}
	}

	private void ApplySonarHit(SonarHit hit)
	{
		try
		{
			if (hit.Head.IndexOf("__PROBE__", StringComparison.Ordinal) >= 0)
			{
				sonarProbeOk = true;
				log.Information("[SrankFlag.Sonar] 链路自检通过：ChatGui.ChatMessage 能收到 Sonar 的聊天输出");
			}
			else
			{
				if (hit.X < 0.0 || hit.X > 42.0 || hit.Y < 0.0 || hit.Y > 42.0)
				{
					return;
				}
				int length = hit.World.Length;
				bool flag = ((length > 20 || length == 0) ? true : false);
				bool flag2 = flag;
				if (!flag2)
				{
					int length2 = hit.Head.Length;
					bool flag3 = ((length2 > 80 || length2 == 0) ? true : false);
					flag2 = flag3;
				}
				if (flag2)
				{
					return;
				}
				List<SnapshotData> list = srankData?.dcs;
				if (list == null || list.Count <= 0)
				{
					return;
				}
				string worldKey = Normalize(hit.World);
				List<SnapshotData> list2 = list.Where((SnapshotData d) => d.worlds.Any((string w) => string.Equals(Normalize(w), worldKey, StringComparison.OrdinalIgnoreCase))).ToList();
				if (list2.Count != 1)
				{
					log.Information("[SrankFlag.Sonar] 服名不在白名单或归属多区，忽略：" + hit.World);
					return;
				}
				SnapshotData snapshotData = list2[0];
				string headKey = Normalize(hit.Head);
				HuntData huntData = snapshotData.hunts.FirstOrDefault((HuntData h) => string.Equals(Normalize(h.cn + h.map), headKey, StringComparison.OrdinalIgnoreCase) || string.Equals(Normalize(h.en + h.map), headKey, StringComparison.OrdinalIgnoreCase));
				if (huntData == null)
				{
					sonarUnmatched++;
					log.Information("[SrankFlag.Sonar] 怪名/地图不在白名单，忽略：" + hit.Head);
					return;
				}
				WorldDeath worldDeath = huntData.worlds.FirstOrDefault((WorldDeath w) => string.Equals(Normalize(w.world), worldKey, StringComparison.OrdinalIgnoreCase));
				if (worldDeath == null)
				{
					return;
				}
				string key = snapshotData.dc + "|" + huntData.en + "|" + worldDeath.world;
				if (hit.IsReset)
				{
					if (!sonarResetSeen.TryGetValue(key, out var value) || !(hit.SeenAt - value < 30.0))
					{
						sonarResetSeen[key] = hit.SeenAt;
						bool flag4 = ClearTriggered(huntData.en, worldDeath.world, "Sonar 脱战/重置播报");
						nativeHud?.ForceMainRefresh();
						log.Information($"[SrankFlag.Sonar] 脱战/重置 {snapshotData.dc}/{worldDeath.world} {huntData.cn}({huntData.en}) → " + (flag4 ? "解除「已被触发」拉黑" : "本来就没拉黑，忽略"));
					}
				}
				else if (!hit.IsKill)
				{
					if (!sonarSpawnSeen.TryGetValue(key, out var value2) || !(hit.SeenAt - value2 < 30.0))
					{
						sonarSpawnSeen[key] = hit.SeenAt;
						history?.Add(huntData.cn, worldDeath.world, huntData.map, HistoryEventType.Spawn, HistorySource.Sonar, hit.SeenAt);
						nativeHud?.ForceHistoryRefresh();
						MarkTriggered(huntData.en, worldDeath.world, worldDeath.lastDeath, hit.SeenAt, "Sonar 出生播报");
						log.Information($"[SrankFlag.Sonar] 出生播报 {snapshotData.dc}/{worldDeath.world} {huntData.cn}({huntData.en}) · 记历史 + 拉黑，实时状态不变");
					}
				}
				else
				{
					if (sonarSeen.TryGetValue(key, out var value3) && hit.SeenAt - value3 < 30.0)
					{
						return;
					}
					sonarSeen[key] = hit.SeenAt;
					double lastDeath = worldDeath.lastDeath;
					if (hit.SeenAt <= lastDeath)
					{
						log.Information($"[SrankFlag.Sonar] 忽略较旧时间：{snapshotData.dc}/{worldDeath.world} {huntData.cn} 本地={hit.SeenAt:0} 现有={lastDeath:0}");
						return;
					}
					double seenAt = hit.SeenAt;
					double num = ((lastDeath > 0.0) ? Calc(lastDeath, seenAt, huntData.cd, huntData.win).Pct : 0.0);
					worldDeath.lastDeath = seenAt;
					worldDeath.isMaint = false;
					double item = Calc(seenAt, seenAt, huntData.cd, huntData.win).Pct;
					if (huntData.worlds.Length != 0)
					{
						double num2 = huntData.worlds.Max((WorldDeath w) => w.lastDeath);
						if (num2 > huntData.lastDeath)
						{
							huntData.lastDeath = num2;
						}
					}
					sonarKills[key] = seenAt;
					sonarRecent.Insert(0, new SonarKillRecord
					{
						at = seenAt,
						kill = seenAt,
						old = lastDeath,
						dc = snapshotData.dc,
						world = worldDeath.world,
						cn = huntData.cn,
						en = huntData.en,
						map = huntData.map,
						oldPct = num,
						newPct = item
					});
					if (sonarRecent.Count > 40)
					{
						sonarRecent.RemoveRange(40, sonarRecent.Count - 40);
					}
					SaveSonarKills();
					history?.Add(huntData.cn, worldDeath.world, huntData.map, HistoryEventType.Kill, HistorySource.Sonar, seenAt);
					nativeHud?.ForceHistoryRefresh();
					ClearTriggered(huntData.en, worldDeath.world, "Sonar 击杀播报", seenAt);
					nativeHud?.ForceMainRefresh();
					log.Information($"[SrankFlag.Sonar] 实时击杀 {snapshotData.dc}/{worldDeath.world} {huntData.cn}({huntData.en}) lastDeath {lastDeath:0} → {seenAt:0}（+{seenAt - lastDeath:0}s） 概率 {num:0.0}% → {item:0.0}% · 已强制刷新主窗");
				}
			}
		}
		catch (Exception ex)
		{
			try
			{
				log.Warning("[SrankFlag.Sonar] 写入实时击杀失败：" + ex.Message);
			}
			catch
			{
			}
		}
	}

	private void LoadSonarKills()
	{
		if (sonarKillsLoaded)
		{
			return;
		}
		sonarKillsLoaded = true;
		try
		{
			if (!File.Exists(SonarCachePath))
			{
				return;
			}
			SonarKillFile sonarKillFile = JsonSerializer.Deserialize<SonarKillFile>(File.ReadAllText(SonarCachePath));
			if (sonarKillFile == null)
			{
				return;
			}
			if (sonarKillFile.kills != null)
			{
				foreach (KeyValuePair<string, double> kill in sonarKillFile.kills)
				{
					if (kill.Value > 0.0)
					{
						sonarKills[kill.Key] = kill.Value;
					}
				}
			}
			if (sonarKillFile.recent != null)
			{
				sonarRecent.AddRange(sonarKillFile.recent.Take(40));
				sonarRecent.Sort((SonarKillRecord a, SonarKillRecord b) => b.at.CompareTo(a.at));
			}
		}
		catch (Exception ex)
		{
			log.Warning("[SrankFlag.Sonar] 读缓存失败：" + ex.Message);
		}
	}

	private void SaveSonarKills()
	{
		try
		{
			SonarKillFile value = new SonarKillFile
			{
				kills = sonarKills,
				recent = sonarRecent
			};
			File.WriteAllText(SonarCachePath, JsonSerializer.Serialize(value, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			log.Warning("[SrankFlag.Sonar] 写缓存失败：" + ex.Message);
		}
	}

	private void ApplySonarKills()
	{
		try
		{
			LoadSonarKills();
			if (sonarKills.Count == 0)
			{
				return;
			}
			List<SnapshotData> list = srankData?.dcs;
			if (list == null || list.Count <= 0)
			{
				return;
			}
			foreach (SnapshotData item in list)
			{
				HuntData[] hunts = item.hunts;
				foreach (HuntData huntData in hunts)
				{
					bool flag = false;
					WorldDeath[] worlds = huntData.worlds;
					foreach (WorldDeath worldDeath in worlds)
					{
						string key = item.dc + "|" + huntData.en + "|" + worldDeath.world;
						if (sonarKills.TryGetValue(key, out var value) && value > worldDeath.lastDeath)
						{
							worldDeath.lastDeath = value;
							worldDeath.isMaint = false;
							flag = true;
						}
					}
					if (flag && huntData.worlds.Length != 0)
					{
						double num = huntData.worlds.Max((WorldDeath w) => w.lastDeath);
						if (num > huntData.lastDeath)
						{
							huntData.lastDeath = num;
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			log.Warning("[SrankFlag.Sonar] 回放本地击杀缓存失败：" + ex.Message);
		}
	}

	private void SonarCommand(string sub)
	{
		LoadSonarKills();
		switch (sub)
		{
		case "probe":
			sonarProbeOk = false;
			sonarProbeAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			framework.RunOnFrameworkThread(() =>
			{
				try
				{
					chatGui.Print("(Sonar) Rank S: __PROBE__ 测试地图 ( 1.0 , 1.0 ) <水晶塔> was just killed");
				}
				catch
				{
				}
			});
			Print("已发出链路自检消息；2 秒后敲 /水桶 sonar 看「链路自检」。这条不会写入击杀数据。");
			return;
		case "test":
			Print("— Sonar 正则自检（只解析，不写任何数据）—");
			Print(SonarDryRun("Rank S: 盐和光 \ue0bb基拉巴尼亚湖区 ( 10.3 , 34.0 ) <晨曦王座> was just killed", "kill"));
			Print(SonarDryRun("(Sonar) Rank S: 戾虫 \ue0bb雷克兰德 ( 25.4  , 24.4 ) <\ue05d旅人栈桥>", "spawn"));
			Print(SonarDryRun("Rank S: 盐和光 \ue0bb基拉巴尼亚湖区 ( 10.3 , 34.0 ) <晨曦王座> despawned", "reset"));
			Print(SonarDryRun("Rank A: 戾虫 \ue0bb雷克兰德 ( 25.4 , 24.4 ) <旅人栈桥> was just killed", "drop"));
			Print(SonarDryRun("哈哈我刚才也看到 Rank S: 盐和光 <晨曦王座> was just killed 啦", "drop"));
			Print(SonarDryRun("Rank S: 盐和光 \ue0bb基拉巴尼亚湖区 ( 10.3 , 34.0 ) <晨曦王座> was just killed 快去", "drop"));
			return;
		case "clear":
			sonarKills.Clear();
			sonarRecent.Clear();
			SaveSonarKills();
			Print("已清空本地实时击杀缓存（不影响 srank_live.json / srank_data.json）");
			return;
		}
		Print($"— Sonar 实时击杀监听 — 缓存 {sonarKills.Count} 条 · 最近记录 {sonarRecent.Count} 条");
		string text = ((sonarProbeAt > 0.0) ? DateTimeOffset.FromUnixTimeSeconds((long)sonarProbeAt).ToLocalTime().ToString("MM-dd HH:mm:ss") : "未发过");
		Print("链路自检：" + (sonarProbeOk ? ("已收到 Chat.Print 输出，监听可用（" + text + "）") : "未验证（敲 /水桶 sonar probe 可验证 ChatMessage 是否收得到 Sonar 消息）"));
		if (sonarUnmatched > 0)
		{
			Print($"未匹配播报 {sonarUnmatched} 条，最近：{sonarUnmatchedSample}");
		}
		if (sonarRecent.Count == 0)
		{
			Print("还没有捕获到击杀播报。/水桶 sonar test 可验证正则，/水桶 sonar probe 可验证链路。");
			return;
		}
		foreach (SonarKillRecord item in sonarRecent.Take(8))
		{
			string value = DateTimeOffset.FromUnixTimeSeconds((long)item.at).ToLocalTime().ToString("MM-dd HH:mm:ss");
			Print($"{value} {item.dc}/{item.world} {item.cn}：站点 {item.old:0} → 本地 {item.kill:0}（差 {item.kill - item.old:0}s） 概率 {item.oldPct:0.0}% → {item.newPct:0.0}%");
		}
	}

	private string SonarDryRun(string raw, string expect)
	{
		string input = CleanSonarText(raw);
		Match match = SonarKillRx.Match(input);
		Match match2 = SonarResetRx.Match(input);
		Match match3 = SonarSpawnRx.Match(input);
		string text;
		if (match.Success)
		{
			text = "kill";
		}
		else if (match2.Success)
		{
			text = "reset";
		}
		else
		{
			text = (match3.Success ? "spawn" : "drop");
		}
		string value = ((text == expect) ? "[OK]" : "[NG]");
		Match match4;
		if (match.Success)
		{
			match4 = match;
		}
		else
		{
			match4 = (match2.Success ? match2 : match3);
		}
		string value2 = (match4.Success ? $"{text} head=[{match4.Groups["head"].Value.Trim()}] world=[{match4.Groups["world"].Value.Trim()}] ({match4.Groups["x"].Value},{match4.Groups["y"].Value})" : "已丢弃");
		string value3 = expect switch
		{
			"kill" => "击杀",
			"spawn" => "出生",
			"reset" => "脱战/重置",
			_ => "丢弃",
		};
		return $"{value} 期望{value3} · {value2}";
	}

	internal static string TrigKey(string en, string world)
	{
		return en + "|" + world;
	}

	private static string MainModeText(MainTriggerMode m)
	{
		if (m != MainTriggerMode.Disabled)
		{
			return "on";
		}
		return "off";
	}

	private static string SourceText(string? src)
	{
		if (src == "sonar")
		{
			return "Sonar出生";
		}
		return "看板在场";
	}

	private static bool IsMainSide(string? src)
	{
		return !string.Equals(src, "sonar", StringComparison.Ordinal);
	}

	private void LoadTriggered()
	{
		if (triggeredLoaded)
		{
			return;
		}
		triggeredLoaded = true;
		try
		{
			if (!File.Exists(TriggeredCachePath))
			{
				return;
			}
			TriggeredFile triggeredFile = JsonSerializer.Deserialize<TriggeredFile>(File.ReadAllText(TriggeredCachePath));
			if (triggeredFile == null)
			{
				return;
			}
			mainTriggerMode = (((triggeredFile.mainMode ?? "").Trim().ToLowerInvariant() == "off") ? MainTriggerMode.Disabled : MainTriggerMode.Enabled);
			if (triggeredFile.items != null)
			{
				foreach (KeyValuePair<string, double> item in triggeredFile.items)
				{
					if (item.Key.Length != 0 && !(item.Value <= 0.0))
					{
						triggeredMonsters[item.Key] = item.Value;
						triggeredFloor[item.Key] = ((triggeredFile.floors != null && triggeredFile.floors.TryGetValue(item.Key, out var value)) ? value : item.Value);
						triggeredSource[item.Key] = ((triggeredFile.src != null && triggeredFile.src.TryGetValue(item.Key, out string value2) && string.Equals(value2, "sonar", StringComparison.Ordinal)) ? "sonar" : "relay");
						triggeredSeen[item.Key] = ((triggeredFile.seen != null && triggeredFile.seen.TryGetValue(item.Key, out var value3)) ? value3 : item.Value);
					}
				}
			}
			if (triggeredFile.killed == null)
			{
				return;
			}
			foreach (KeyValuePair<string, double> item2 in triggeredFile.killed)
			{
				if (item2.Key.Length > 0 && item2.Value > 0.0)
				{
					killedWatermark[item2.Key] = item2.Value;
				}
			}
		}
		catch (Exception ex)
		{
			log.Warning("[SrankFlag.Trigger] 读黑名单失败：" + ex.Message);
		}
	}

	private void SaveTriggered()
	{
		try
		{
			TriggeredFile value = new TriggeredFile
			{
				v = 8,
				mainMode = MainModeText(mainTriggerMode),
				items = triggeredMonsters,
				floors = triggeredFloor,
				killed = killedWatermark,
				seen = triggeredSeen,
				src = triggeredSource
			};
			File.WriteAllText(TriggeredCachePath, JsonSerializer.Serialize(value, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			log.Warning("[SrankFlag.Trigger] 写黑名单失败：" + ex.Message);
		}
	}

	internal bool IsTriggered(string en, string world)
	{
		if (en.Length == 0 || world.Length == 0)
		{
			return false;
		}
		LoadTriggered();
		string key=TrigKey(en,world);
		if(!MainSiteWorld(world)&&triggeredSource.TryGetValue(key,out var source)&&IsMainSide(source))return false;
		return triggeredMonsters.ContainsKey(key);
	}

	private bool MarkTriggered(string en, string world, double deathFloor, double at, string reason, string source = "relay")
	{
		if (en.Length == 0 || world.Length == 0)
		{
			return false;
		}
		LoadTriggered();
		string key = TrigKey(en, world);
		double num = ((at > 0.0) ? at : ((double)DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
		if (triggeredMonsters.ContainsKey(key))
		{
			triggeredSeen[key] = Math.Max(triggeredSeen.TryGetValue(key, out var value) ? value : 0.0, num);
			return false;
		}
		triggeredMonsters[key] = num;
		triggeredFloor[key] = ((deathFloor > 0.0) ? deathFloor : 0.0);
		triggeredSource[key] = source;
		triggeredSeen[key] = num;
		SaveTriggered();
		log.Information($"[SrankFlag.Trigger] 拉黑 {world} · {en}（{reason}，{source}，解锁水位 {deathFloor:F0}）");
		nativeHud?.ForceMainRefresh();
		return true;
	}

	private bool ClearTriggered(string en, string world, string reason, double killedAt = 0.0, string? onlyIfSource = null)
	{
		if (en.Length == 0 || world.Length == 0)
		{
			return false;
		}
		LoadTriggered();
		string key = TrigKey(en, world);
		if (onlyIfSource != null && triggeredSource.TryGetValue(key, out string value) && !string.Equals(value, onlyIfSource, StringComparison.Ordinal))
		{
			return false;
		}
		bool flag = triggeredMonsters.Remove(key);
		bool flag2 = triggeredFloor.Remove(key);
		triggeredSource.Remove(key);
		triggeredSeen.Remove(key);
		bool flag3 = false;
		if (killedAt > 0.0)
		{
			killedWatermark.TryGetValue(key, out var value2);
			if (killedAt > value2)
			{
				killedWatermark[key] = killedAt;
				flag3 = true;
			}
		}
		if (!flag && !flag2 && !flag3)
		{
			return false;
		}
		SaveTriggered();
		if (flag)
		{
			log.Information($"[SrankFlag.Trigger] 解锁 {world} · {en}（{reason}）");
			nativeHud?.ForceMainRefresh();
		}
		return flag;
	}

	private void SeedKilledWatermarkFromKnownDeaths()
	{
		List<SnapshotData> list = srankData?.dcs;
		if (list == null || list.Count <= 0)
		{
			return;
		}
		foreach (SnapshotData item in list)
		{
			HuntData[] hunts = item.hunts;
			foreach (HuntData huntData in hunts)
			{
				WorldDeath[] worlds = huntData.worlds;
				foreach (WorldDeath worldDeath in worlds)
				{
					if (!(worldDeath.lastDeath <= 0.0))
					{
						string key = TrigKey(huntData.en, worldDeath.world);
						killedWatermark.TryGetValue(key, out var value);
						if (worldDeath.lastDeath > value)
						{
							killedWatermark[key] = worldDeath.lastDeath;
						}
					}
				}
			}
		}
	}

	private void SweepTriggeredByDeath()
	{
		if (triggeredMonsters.Count == 0)
		{
			return;
		}
		List<SnapshotData> list = srankData?.dcs;
		if (list == null || list.Count <= 0)
		{
			return;
		}
		List<(string, double)> list2 = null;
		foreach (SnapshotData item in list)
		{
			HuntData[] hunts = item.hunts;
			foreach (HuntData huntData in hunts)
			{
				WorldDeath[] worlds = huntData.worlds;
				foreach (WorldDeath worldDeath in worlds)
				{
					string text = TrigKey(huntData.en, worldDeath.world);
					if (triggeredMonsters.ContainsKey(text))
					{
						triggeredFloor.TryGetValue(text, out var value);
						if (!(worldDeath.lastDeath <= 0.0) && !(worldDeath.lastDeath <= value))
						{
							(list2 ?? (list2 = new List<(string, double)>())).Add((text, worldDeath.lastDeath));
						}
					}
				}
			}
		}
		if (list2 == null)
		{
			return;
		}
		foreach (var (key, num) in list2)
		{
			triggeredMonsters.Remove(key);
			triggeredFloor.Remove(key);
			triggeredSource.Remove(key);
			triggeredSeen.Remove(key);
			killedWatermark.TryGetValue(key, out var value2);
			if (num > value2)
			{
				killedWatermark[key] = num;
			}
		}
		SaveTriggered();
		log.Information($"[SrankFlag.Trigger] 击杀时间越过水位，解锁 {list2.Count} 只");
		nativeHud?.ForceMainRefresh();
	}

	private void MaybeFetchRelay(bool force)
	{
		if(!BearRefreshPolicy.UsesMainSite(currentDc))return;
		if (!relayFetching && (force || !(DateTime.UtcNow - lastRelayFetch < RelayInterval)))
		{
			relayFetching = true;
			lastRelayFetch = DateTime.UtcNow;
			Task.Run(() => FetchRelayAsync());
		}
	}

	private async Task FetchRelayAsync()
	{
		try
		{
			using HttpResponseMessage resp = await BearHttp.Value.GetAsync("http://119.29.119.179/srank/bot_relay.json").ConfigureAwait(continueOnCapturedContext: false);
			if (!resp.IsSuccessStatusCode)
			{
				relayStatus = $"看板拉取失败 HTTP {(int)resp.StatusCode}";
				return;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false));
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("relay", out var value) || value.ValueKind != JsonValueKind.Object)
			{
				relayStatus = "看板返回格式异常";
				return;
			}
			double gen = 0.0;
			if (rootElement.TryGetProperty("generated", out var value2) && value2.ValueKind == JsonValueKind.Number)
			{
				value2.TryGetDouble(out gen);
			}
			Dictionary<string, Dictionary<string, double[]>> parsed = new Dictionary<string, Dictionary<string, double[]>>(StringComparer.Ordinal);
			int total = 0;
			foreach (JsonProperty item in value.EnumerateObject())
			{
				if (!MainSiteWorld(item.Name) || item.Value.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				Dictionary<string, double[]> dictionary = new Dictionary<string, double[]>(StringComparer.Ordinal);
				foreach (JsonProperty item2 in item.Value.EnumerateObject())
				{
					if (item2.Value.ValueKind == JsonValueKind.Array)
					{
						JsonElement value3 = item2.Value;
						if (value3.GetArrayLength() >= 3 && value3[0].ValueKind == JsonValueKind.Number && value3[0].TryGetDouble(out var value4) && !(value4 <= 0.0))
						{
							double num = ((value3[1].ValueKind == JsonValueKind.Number && value3[1].TryGetDouble(out var value5)) ? value5 : 0.0);
							double num2 = ((value3[2].ValueKind == JsonValueKind.Number && value3[2].TryGetDouble(out var value6)) ? value6 : 0.0);
							dictionary[item2.Name] = new double[3] { value4, num, num2 };
							total++;
						}
					}
				}
				if (dictionary.Count > 0)
				{
					parsed[item.Name] = dictionary;
				}
			}
			framework.RunOnTick(() =>
			{
				try
				{
					relayLive = parsed;
					relayGenerated = gen;
					lastRelayOk = DateTime.UtcNow;
					relayStatus = $"看板已同步 {parsed.Count} 服 / {total} 条 · {DateTime.Now:HH:mm}";
					ApplyRelayTriggered(null);
				}
				catch (Exception ex2)
				{
					log.Warning("[SrankFlag.Trigger] relay 写入失败：" + ex2.Message);
				}
				finally
				{
					relayFetching = false;
				}
			});
		}
		catch (Exception ex)
		{
			relayFetching = false;
			relayStatus = "看板拉取异常：" + ex.Message;
			log.Warning("[SrankFlag.Trigger] 看板 relay 拉取失败：" + ex.Message);
		}
	}

	private double DeathOf(string en,string world)=>AllSnapshots().SelectMany(dc=>dc.hunts)
		.Where(hunt=>string.Equals(hunt.en,en,StringComparison.Ordinal)).SelectMany(hunt=>hunt.worlds)
		.Where(death=>string.Equals(death.world,world,StringComparison.Ordinal)).Select(death=>death.lastDeath).DefaultIfEmpty(0).Max();

	private void ApplyRelayTriggered(HashSet<string>? justDied)
	{
		LoadTriggered();
		if (mainTriggerMode == MainTriggerMode.Disabled)
		{
			ReleaseAllMainSide("看板源已关闭");
		}
		else
		{
			if (relayLive.Count == 0)
			{
				return;
			}
			long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			Dictionary<string, double> dictionary = new Dictionary<string, double>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, Dictionary<string, double[]>> item in relayLive)
			{
				string key = item.Key;
				if(!MainSiteWorld(key))continue;
				foreach (KeyValuePair<string, double[]> item2 in item.Value)
				{
					double[] value = item2.Value;
					if (value != null && value.Length >= 1 && !(value[0] <= 0.0) && !((double)num - value[0] > 72000.0))
					{
						string text = TrigKey(item2.Key, key);
						if ((justDied == null || !justDied.Contains(text)) && !(DeathOf(item2.Key, key) >= value[0] - 30.0))
						{
							hashSet.Add(text);
							dictionary[text] = value[0];
						}
					}
				}
			}
			int num2 = 0;
			foreach (string item3 in hashSet)
			{
				string[] array = item3.Split('|');
				if (array.Length == 2)
				{
					if (MarkTriggered(array[0], array[1], DeathOf(array[0], array[1]), dictionary[item3], "看板显示已触发（当前在场）"))
					{
						num2++;
					}
					triggeredSeen[item3] = num;
				}
			}
			List<(string, string)> list = null;
			foreach (string item4 in triggeredMonsters.Keys.ToList())
			{
				if (hashSet.Contains(item4))
				{
					continue;
				}
				triggeredSource.TryGetValue(item4, out string value2);
				if (IsMainSide(value2))
				{
					string[] array2 = item4.Split('|');
					if (array2.Length == 2)
					{
						(list ?? (list = new List<(string, string)>())).Add((array2[0], array2[1]));
					}
				}
			}
			int num3 = 0;
			if (list != null)
			{
				foreach (var (en, world) in list)
				{
					if (ClearTriggered(en, world, "看板已不再显示该怪在场"))
					{
						num3++;
					}
				}
			}
			if (num2 > 0 || num3 > 0)
			{
				log.Information($"[SrankFlag.Trigger] 看板同步：新增 {num2} 只 / 放行 {num3} 只，当前共 {triggeredMonsters.Count} 只（看板在场 {hashSet.Count}）");
			}
		}
	}

	private void ReleaseAllMainSide(string reason)
	{
		List<string> list = triggeredMonsters.Keys.ToList();
		if (list.Count == 0)
		{
			return;
		}
		int num = 0;
		foreach (string item in list)
		{
			triggeredSource.TryGetValue(item, out string value);
			if (IsMainSide(value))
			{
				string[] array = item.Split('|');
				if (array.Length == 2 && ClearTriggered(array[0], array[1], reason))
				{
					num++;
				}
			}
		}
		if (num > 0)
		{
			log.Information($"[SrankFlag.Trigger] {reason}：放行 {num} 只");
		}
	}

	private void SweepStaleByTimeout()
	{
		if (triggeredMonsters.Count == 0 || !RelayStale)
		{
			return;
		}
		long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		List<string> list = triggeredMonsters.Keys.Where((string k) => (double)now - ((triggeredSeen.TryGetValue(k, out var value) && value > 0.0) ? value : 0.0) > TriggeredTimeout.TotalSeconds).ToList();
		if (list.Count == 0)
		{
			return;
		}
		foreach (string item in list)
		{
			triggeredMonsters.Remove(item);
			triggeredFloor.Remove(item);
			triggeredSource.Remove(item);
			triggeredSeen.Remove(item);
		}
		SaveTriggered();
		log.Warning($"[SrankFlag.Trigger] 看板数据陈旧，超时兜底解锁 {list.Count} 只");
		nativeHud?.ForceMainRefresh();
	}

	private void TickTriggeredSweep()
	{
		if (lastRelayOk == DateTime.MinValue && lastRelayFetch == DateTime.MinValue)
		{
			MaybeFetchRelay(force: true);
		}
		if (!(DateTime.UtcNow - lastTriggeredSweepUtc < TriggeredSweepInterval))
		{
			lastTriggeredSweepUtc = DateTime.UtcNow;
			try
			{
				ApplyRelayTriggered(null);
			}
			catch (Exception ex)
			{
				log.Warning("[SrankFlag.Trigger] 看板同步失败：" + ex.Message);
			}
			try
			{
				SweepStaleByTimeout();
			}
			catch (Exception ex2)
			{
				log.Warning("[SrankFlag.Trigger] 超时兜底失败：" + ex2.Message);
			}
			MaybeFetchRelay(force: false);
		}
	}

	private void TriggerCommand(string[] parts)
	{
		LoadTriggered();
		switch ((parts.Length > 1) ? parts[1].ToLowerInvariant() : "")
		{
		case "main":
		case "stuck":
		{
			string text = ((parts.Length > 2) ? parts[2].ToLowerInvariant() : "");
			mainTriggerMode = ((text == "off") ? MainTriggerMode.Disabled : MainTriggerMode.Enabled);
			SaveTriggered();
			string value = ((mainTriggerMode == MainTriggerMode.Disabled) ? "关闭：主站/看板不再参与判定，只认 Sonar 出生播报" : "开启：跟看板「已触发（当前在场）」完全一致（默认）");
			Print($"看板源 = {MainModeText(mainTriggerMode)}（{value}）。" + "3 分钟内自动生效（/水桶 trigger scan 可立刻对账）。");
			return;
		}
		case "scan":
			PrintTriggerScan();
			return;
		case "clear":
		{
			int count = triggeredMonsters.Count;
			triggeredMonsters.Clear();
			triggeredFloor.Clear();
			triggeredSource.Clear();
			triggeredSeen.Clear();
			SeedKilledWatermarkFromKnownDeaths();
			SaveTriggered();
			nativeHud?.ForceMainRefresh();
			Print($"已清空「已被触发」黑名单（{count} 条）。" + "注意：看板仍显示在场的 S 会在下一轮同步（≤3 分钟）重新标红 —— 以看板为准。");
			return;
		}
		}
		Print($"— 已被他人触发的 S — 共 {triggeredMonsters.Count} 条（看板 {MainModeText(mainTriggerMode)} · {relayStatus}）");
		if (triggeredMonsters.Count == 0)
		{
			Print("当前没有拉黑任何 S。/水桶 trigger scan 可与看板对账。");
			return;
		}
		foreach (KeyValuePair<string, double> item in triggeredMonsters.OrderByDescending((KeyValuePair<string, double> x) => x.Value).Take(12))
		{
			string value2 = DateTimeOffset.FromUnixTimeSeconds((long)item.Value).ToLocalTime().ToString("MM-dd HH:mm:ss");
			string[] array = item.Key.Split('|');
			triggeredSource.TryGetValue(item.Key, out string value3);
			Print($"  {value2}  {((array.Length > 1) ? array[1] : "")} · {((array.Length != 0) ? array[0] : item.Key)}（{SourceText(value3)}）");
		}
		if (triggeredMonsters.Count > 12)
		{
			Print($"  …另有 {triggeredMonsters.Count - 12} 条");
		}
	}

	private void PrintTriggerScan()
	{
		Print($"— 看板「已触发（当前在场）」对账（{DateTime.Now:HH:mm:ss}，只读）—");
		Print("  " + relayStatus + ((relayGenerated > 0.0) ? $"，快照生成 {Math.Round(((double)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - relayGenerated) / 60.0)} 分钟前" : ""));
		if (relayLive.Count == 0)
		{
			Print("  还没有拉到看板数据（检查网络 / 站点是否在线）。");
		}
		else
		{
			long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			foreach (KeyValuePair<string, Dictionary<string, double[]>> item in relayLive)
			{
				foreach (KeyValuePair<string, double[]> item2 in item.Value)
				{
					double[] value = item2.Value;
					if (value != null && value.Length >= 1 && !(value[0] <= 0.0))
					{
						num2++;
						if ((double)num - value[0] > 72000.0)
						{
							num3++;
						}
						else if (DeathOf(item2.Key, item.Key) >= value[0] - 30.0)
						{
							num4++;
						}
						else
						{
							num5++;
						}
					}
				}
			}
			Print($"  relay 共 {num2} 条 → 超 20h 过期 {num3} · 之后已击杀 {num4} ⇒ 看板在场 {num5} 只");
		}
		Dictionary<string, int> dictionary = (from k in triggeredMonsters.Keys
			group k by SourceText(triggeredSource.TryGetValue(k, out string value3) ? value3 : null)).ToDictionary((IGrouping<string, string> g) => g.Key, (IGrouping<string, string> g) => g.Count());
		Print($"  本地黑名单 {triggeredMonsters.Count} 只" + ((dictionary.Count > 0) ? ("（" + string.Join(" · ", dictionary.Select((KeyValuePair<string, int> kv) => kv.Key + " " + kv.Value)) + "）") : ""));
		HashSet<string> expect = new HashSet<string>(StringComparer.Ordinal);
		long num6 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		foreach (KeyValuePair<string, Dictionary<string, double[]>> item3 in relayLive)
		{
			foreach (KeyValuePair<string, double[]> item4 in item3.Value)
			{
				double[] value2 = item4.Value;
				if (value2 != null && value2.Length >= 1 && !(value2[0] <= 0.0) && !((double)num6 - value2[0] > 72000.0) && !(DeathOf(item4.Key, item3.Key) >= value2[0] - 30.0))
				{
					expect.Add(TrigKey(item4.Key, item3.Key));
				}
			}
		}
		List<string> list = expect.Where((string k) => !triggeredMonsters.ContainsKey(k)).ToList();
		List<string> list2 = triggeredMonsters.Keys.Where((string k) => !expect.Contains(k) && IsMainSide(triggeredSource.TryGetValue(k, out string value3) ? value3 : null)).ToList();
		Print($"  对账：看板在场但未锁 {list.Count} 只 · 看板不在场却锁着 {list2.Count} 只" + ((list.Count == 0 && list2.Count == 0) ? "（完全一致）" : ""));
		foreach (string item5 in list.Take(3))
		{
			Print("   · 待锁 " + item5);
		}
		foreach (string item6 in list2.Take(3))
		{
			Print("   · 待放 " + item6);
		}
	}
}
