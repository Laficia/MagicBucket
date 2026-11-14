using System;
using System.Collections.Generic;
using System.Linq;

namespace SrankFlag;

internal static class ConditionForecast
{
	public readonly record struct Window(double Start, double End, string Name);

	private sealed class Spec
	{
		public string Kind = "";

		public string Zone = "";

		public string ZoneCn = "";

		public double Hold;

		public string[] Include = Array.Empty<string>();

		public string[] Exclude = Array.Empty<string>();

		public (double From, double To)[] Hours = Array.Empty<(double, double)>();

		public (double From, double To)[] FirstDayHours = Array.Empty<(double, double)>();

		public Func<int, bool> DayFilter = (int _) => true;

		public string[] Weather = Array.Empty<string>();

		public string Label = "";

		public double Horizon = 57600.0;
	}

	public const double EtHour = 175.0;

	public const double EtDay = 4200.0;

	public const double WeatherPeriod = 1400.0;

	private const double HorizonDefault = 57600.0;

	private const double HorizonMoon = 1300000.0;

	private const double HorizonWeather = 700000.0;

	private const int DaysAhead = 320;

	private const int MoonEpochOffset = 0;

	private static readonly Dictionary<string, (int Bound, string Name)[]> Zones = new Dictionary<string, (int, string)[]>(StringComparer.Ordinal)
	{
		["Central Shroud"] = new (int, string)[7]
		{
			(5, "Thunder"),
			(20, "Rain"),
			(30, "Fog"),
			(40, "Clouds"),
			(55, "Fair Skies"),
			(85, "Clear Skies"),
			(100, "Fair Skies")
		},
		["Eastern La Noscea"] = new (int, string)[6]
		{
			(5, "Fog"),
			(50, "Clear Skies"),
			(80, "Fair Skies"),
			(90, "Clouds"),
			(95, "Rain"),
			(100, "Showers")
		},
		["Western Thanalan"] = new (int, string)[5]
		{
			(40, "Clear Skies"),
			(60, "Fair Skies"),
			(85, "Clouds"),
			(95, "Fog"),
			(100, "Rain")
		},
		["Labyrinthos"] = new (int, string)[4]
		{
			(15, "Clear Skies"),
			(60, "Fair Skies"),
			(85, "Clouds"),
			(100, "Rain")
		},
		["Urqopacha"] = new (int, string)[6]
		{
			(20, "Clear Skies"),
			(50, "Fair Skies"),
			(70, "Clouds"),
			(80, "Fog"),
			(90, "Wind"),
			(100, "Snow")
		}
	};

	private static readonly Dictionary<string, string> WeatherCn = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["Clear Skies"] = "碧空",
		["Fair Skies"] = "晴朗",
		["Clouds"] = "阴云",
		["Fog"] = "薄雾",
		["Rain"] = "小雨",
		["Showers"] = "暴雨",
		["Thunder"] = "打雷",
		["Wind"] = "微风",
		["Snow"] = "小雪"
	};

	private static readonly string[] MonthNames = new string[12]
	{
		"星1月", "灵1月", "星2月", "灵2月", "星3月", "灵3月", "星4月", "灵4月", "星5月", "灵5月",
		"星6月", "灵6月"
	};

	private static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.Ordinal)
	{
		["Laideronnette"] = new Spec
		{
			Kind = "weather_hold",
			Zone = "Central Shroud",
			ZoneCn = "黑衣森林中央林区",
			Hold = 1800.0,
			Include = new string[1] { "Rain" },
			Horizon = 700000.0,
			Label = "连续 30 分钟小雨"
		},
		["The Garlok"] = new Spec
		{
			Kind = "weather_hold",
			Zone = "Eastern La Noscea",
			ZoneCn = "东拉诺西亚",
			Hold = 12000.0,
			Exclude = new string[2] { "Rain", "Showers" },
			Horizon = 700000.0,
			Label = "连续 200 分钟无雨"
		},
		["Zona Seeker"] = new Spec
		{
			Kind = "weather",
			Zone = "Western Thanalan",
			ZoneCn = "西萨纳兰",
			Include = new string[2] { "Clear Skies", "Fair Skies" },
			Horizon = 700000.0,
			Label = "西萨纳兰碧空/晴朗"
		},
		["Croque-Mitaine"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (19.0, 22.0) },
			Label = "ET 19:00–22:00"
		},
		["Bonnacon"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (8.0, 11.0) },
			Label = "ET 8:00–11:00"
		},
		["Thousand-cast Theda"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (17.0, 21.0) },
			Label = "ET 17:00–21:00"
		},
		["Gamma"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (17.0, 32.0) },
			Label = "ET 17:00–次日 08:00"
		},
		["Mindflayer"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (17.0, 27.0) },
			FirstDayHours = new (double, double)[1] { (0.0, 27.0) },
			DayFilter = NewMoon,
			Horizon = 1300000.0,
			Label = "新月：日1 00:00–次日03:00；日2–4 17:00–次日03:00"
		},
		["Croakadile"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (17.0, 27.0) },
			DayFilter = FullMoon16to19,
			Horizon = 1300000.0,
			Label = "满月（ET 日 16–19）"
		},
		["Okina"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (12.0, 108.0) },
			DayFilter = FullMoon16,
			Horizon = 1300000.0,
			Label = "满月（ET 日 16）"
		},
		["Burfurlur the Canny"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (9.0, 17.0) },
			Zone = "Labyrinthos",
			ZoneCn = "迷津",
			Weather = new string[2] { "Clear Skies", "Fair Skies" },
			Label = "ET 9:00–17:00 且迷津碧空/晴朗"
		},
		["Kirlirger the Abhorrent"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[1] { (0.0, 8.0) },
			DayFilter = NewMoon,
			Zone = "Urqopacha",
			ZoneCn = "奥阔帕恰山",
			Weather = new string[1] { "Fog" },
			Horizon = 1300000.0,
			Label = "新月 ET 日 1–4 的 00:00–08:00 且薄雾"
		},
		["Gandarewa"] = new Spec
		{
			Kind = "et_window",
			Hours = new (double, double)[2]
			{
				(2.0, 6.0),
				(14.0, 18.0)
			},
			Label = "翻云雾海采集点 ET 2:00–6:00 / 14:00–18:00"
		}
	};

	private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
	private static readonly Dictionary<string, (double At, Window? Value)> WindowCache = new(StringComparer.Ordinal);

	private static long cacheBucket = long.MinValue;

	private static int MoonDay(int dayCount)
	{
		return ((dayCount + MoonEpochOffset) % 32 + 32) % 32 + 1;
	}

	private static bool NewMoon(int dayCount)
	{
		int num = MoonDay(dayCount);
		if (num >= 1)
		{
			return num <= 4;
		}
		return false;
	}

	private static bool FullMoon16to19(int dayCount)
	{
		int num = MoonDay(dayCount);
		if (num >= 16)
		{
			return num <= 19;
		}
		return false;
	}

	private static bool FullMoon16(int dayCount)
	{
		return MoonDay(dayCount) == 16;
	}

	public static bool HasSpec(string en)
	{
		return Specs.ContainsKey(en);
	}

	public static string Describe(string en, double now)
	{
		if (!Specs.TryGetValue(en, out Spec value))
		{
			return "";
		}
		long num = (long)now / 20;
		if (num != cacheBucket)
		{
			Cache.Clear();
			cacheBucket = num;
		}
		if (Cache.TryGetValue(en, out string value2))
		{
			return value2;
		}
		string text = Build(value, now);
		Cache[en] = text;
		return text;
	}

	public static string LabelOf(string en)
	{
		if (!Specs.TryGetValue(en, out Spec value))
		{
			return "";
		}
		return value.Label;
	}

	public static Window? NextWindow(string en, double now)
	{
		if (!Specs.TryGetValue(en, out Spec value))
		{
			return null;
		}
		if (WindowCache.TryGetValue(en, out var cached) && now >= cached.At && (cached.Value.HasValue ? now < cached.Value.Value.End : now < cached.At + 20)) return cached.Value;
		List<Window> list = Windows(value, now);
		foreach (Window item in list)
		{
			if (item.Start <= now && now < item.End)
			{
				WindowCache[en] = (now, item);
				return item;
			}
		}
		if (list.Count <= 0)
		{
			WindowCache[en] = (now, null);
			return null;
		}
		WindowCache[en] = (now, list[0]);
		return list[0];
	}

	private static string Build(Spec sp, double now)
	{
		List<Window> list = Windows(sp, now);
		string text = ((sp.Label.Length > 0) ? ("（" + sp.Label + "）") : "");
		if (list.Count == 0)
		{
			return "条件窗口：" + HorizonText(sp.Horizon) + "内没有" + text;
		}
		int num = -1;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].Start <= now && now < list[i].End)
			{
				num = i;
				break;
			}
		}
		if (num >= 0)
		{
			Window window = list[num];
			return "条件窗口 进行中 · 剩 " + FmtDur(window.End - now) + " · " + Range(window.Start, window.End);
		}
		Window window2 = list[0];
		return "条件窗口 " + FmtDur(window2.Start - now) + "后 · " + Range(window2.Start, window2.End);
	}

	private static List<Window> Windows(Spec sp, double now)
	{
		switch (sp.Kind)
		{
		case "weather_hold":
		case "weather":
			return WeatherWindows(sp, now);
		case "et_window":
			return EtWindows(sp, now);
		default:
			return new List<Window>();
		}
	}

	private static List<Window> WeatherWindows(Spec sp, double now)
	{
		if (!Zones.TryGetValue(sp.Zone, out (int, string)[] value))
		{
			return new List<Window>();
		}
		int num = (int)(sp.Horizon / 1400.0) + 8;
		int num2 = num * 2;
		double[] array = new double[num2];
		bool[] array2 = new bool[num2];
		double num3 = CurrentPeriodStart(now);
		for (int i = 0; i < num2; i++)
		{
			string value2 = WeatherAt(t: array[i] = num3 + (double)(i - num) * 1400.0, rate: value);
			array2[i] = ((sp.Include.Length != 0) ? (Array.IndexOf(sp.Include, value2) >= 0) : (sp.Exclude.Length == 0 || Array.IndexOf(sp.Exclude, value2) < 0));
		}
		List<Window> list = new List<Window>();
		int num4 = 0;
		while (num4 < num2)
		{
			if (!array2[num4])
			{
				num4++;
				continue;
			}
			int j;
			for (j = num4; j + 1 < num2 && array2[j + 1]; j++)
			{
			}
			double num5 = array[num4];
			double num6 = array[j] + 1400.0;
			double num7 = num5 + sp.Hold;
			if (num6 > num7 && num6 > now)
			{
				list.Add(new Window(num7, num6, sp.ZoneCn));
			}
			num4 = j + 1;
		}
		list.Sort((Window a, Window b) => a.Start.CompareTo(b.Start));
		return list;
	}

	private static List<Window> EtWindows(Spec sp, double now)
	{
		long num = (long)(now / 4200.0);
		List<Window> list = new List<Window>();
		int lookback = Math.Max(1, (int)Math.Ceiling(sp.Hours.Select(h => h.To).DefaultIfEmpty(24).Max() / 24));
		for (long num2 = num - lookback; num2 < num + 320; num2++)
		{
			if (!sp.DayFilter((int)num2))
			{
				continue;
			}
			int num3 = MoonDay((int)num2);
			(double, double)[] array = ((sp.FirstDayHours.Length != 0 && num3 == 1) ? sp.FirstDayHours : sp.Hours);
			for (int i = 0; i < array.Length; i++)
			{
				(double, double) tuple = array[i];
				double item = tuple.Item1;
				double item2 = tuple.Item2;
				double num4 = (double)num2 * 4200.0 + item * 175.0;
				double num5 = (double)num2 * 4200.0 + item2 * 175.0;
				if (!(num5 <= now))
				{
					if (sp.Zone.Length > 0 && sp.Weather.Length != 0 && Zones.TryGetValue(sp.Zone, out (int, string)[] value))
					{
						list.AddRange(IntersectWeather(num4, num5, value, sp.Weather));
					}
					else
					{
						list.Add(new Window(num4, num5, sp.ZoneCn));
					}
				}
			}
		}
		list.Sort((Window a, Window b) => a.Start.CompareTo(b.Start));
		return list.Where((Window w) => w.Start < now + sp.Horizon).ToList();
	}

	private static IEnumerable<Window> IntersectWeather(double s, double e, (int Bound, string Name)[] rate, string[] weather)
	{
		for (double p = CurrentPeriodStart(s); p < e; p += 1400.0)
		{
			string text = WeatherAt(rate, p);
			if (Array.IndexOf(weather, text) >= 0)
			{
				double num = Math.Max(s, p);
				double num2 = Math.Min(e, p + 1400.0);
				if (num2 > num)
				{
					yield return new Window(num, num2, WeatherCn.GetValueOrDefault(text, text));
				}
			}
		}
	}

	private static string WeatherAt((int Bound, string Name)[] rate, double t)
	{
		int num = ForecastSeed(t);
		for (int i = 0; i < rate.Length; i++)
		{
			var (num2, result) = rate[i];
			if (num < num2)
			{
				return result;
			}
		}
		return rate[^1].Name;
	}

	public static int ForecastSeed(double unixSeconds)
	{
		long num = (long)unixSeconds;
		long num2 = num / 175;
		long num3 = (num2 + 8 - num2 % 8) % 24;
		uint num4 = (uint)(num / 4200 * 100 + num3);
		uint num5 = (num4 << 11) ^ num4;
		return (int)(((num5 >> 8) ^ num5) % 100);
	}

	public static double CurrentPeriodStart(double unixSeconds)
	{
		return (double)((long)(unixSeconds / 175.0) / 8 * 8) * 175.0;
	}

	public static (long DayCount, int Hour, int Minute) EtParts(double unixSeconds)
	{
		double num = unixSeconds / 175.0;
		long item = (long)(num / 24.0);
		double num2 = num % 24.0;
		int num3 = (int)num2;
		int item2 = (int)Math.Round((num2 - (double)num3) * 60.0) % 60;
		return (DayCount: item, Hour: num3, Minute: item2);
	}

	private static string Range(double s, double e)
	{
		(long DayCount, int Hour, int Minute) tuple = EtParts(s);
		long item = tuple.DayCount;
		int item2 = tuple.Hour;
		int item3 = tuple.Minute;
		(long DayCount, int Hour, int Minute) tuple2 = EtParts(e);
		long item4 = tuple2.DayCount;
		int item5 = tuple2.Hour;
		int item6 = tuple2.Minute;
		string text = $"{MonthNames[(int)(item / 32 % 12)]}{item % 32 + 1}日 {item2:00}:{item3:00}";
		string text2 = ((item == item4) ? $"{item5:00}:{item6:00}" : $"{MonthNames[(int)(item4 / 32 % 12)]}{item4 % 32 + 1}日 {item5:00}:{item6:00}");
		return "ET " + text + "–" + text2;
	}

	private static string FmtDur(double seconds)
	{
		int num = (int)Math.Max(0.0, seconds);
		if (num < 60)
		{
			return num + " 秒";
		}
		int num2 = num / 60;
		if (num2 < 60)
		{
			return num2 + " 分" + ((num % 60 > 0) ? (" " + num % 60 + " 秒") : "");
		}
		int num3 = num2 / 60;
		if (num3 < 24)
		{
			return num3 + " 小时" + ((num2 % 60 > 0) ? (" " + num2 % 60 + " 分") : "");
		}
		return num3 / 24 + " 天" + ((num3 % 24 > 0) ? (" " + num3 % 24 + " 小时") : "");
	}

	private static string HorizonText(double horizon)
	{
		if (!(horizon >= 86400.0))
		{
			return (int)(horizon / 3600.0) + " 小时";
		}
		return (int)(horizon / 86400.0) + " 天";
	}
}
