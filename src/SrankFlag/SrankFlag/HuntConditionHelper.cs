using System;
using System.Collections.Generic;

namespace SrankFlag;

internal static class HuntConditionHelper
{
	public enum Kind
	{
		None,
		Weather,
		WeatherHold,
		Time,
		Moon,
		Prereq
	}

	public sealed record HuntCondition(string Text, bool? Met, bool Live, Kind Type);

	private sealed record Entry(string En, string Text, Kind Type);

	private static readonly Dictionary<string, Entry> Conditions = new Dictionary<string, Entry>(StringComparer.Ordinal)
	{
		["Okina"] = new Entry("Okina", "月相：满月", Kind.Moon),
		["Gamma"] = new Entry("Gamma", "时间：ET17-次日08", Kind.Time),
		["The Garlok"] = new Entry("The Garlok", "天气：无雨≥200分", Kind.WeatherHold),
		["Croque-Mitaine"] = new Entry("Croque-Mitaine", "时间：ET19-22", Kind.Time),
		["Thousand-cast Theda"] = new Entry("Thousand-cast Theda", "时间：ET17-21", Kind.Time),
		["Zona Seeker"] = new Entry("Zona Seeker", "天气：碧空/晴朗", Kind.Weather),
		["Bonnacon"] = new Entry("Bonnacon", "时间：ET8-11", Kind.Time),
		["Croakadile"] = new Entry("Croakadile", "月相：满月", Kind.Moon),
		["Laideronnette"] = new Entry("Laideronnette", "天气：连续小雨", Kind.WeatherHold),
		["Kirlirger the Abhorrent"] = new Entry("Kirlirger the Abhorrent", "天气：薄雾(新月)", Kind.Moon),
		["Burfurlur the Canny"] = new Entry("Burfurlur the Canny", "天气：碧空/晴朗", Kind.Weather)
	};

	private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["巨大鳐"] = "Okina",
		["伽马"] = "Gamma",
		["伽洛克"] = "The Garlok",
		["护土精灵"] = "Croque-Mitaine",
		["千竿口花希达"] = "Thousand-cast Theda",
		["虚无探索者"] = "Zona Seeker",
		["火愤牛"] = "Bonnacon",
		["咕尔呱洛斯"] = "Croakadile",
		["雷德罗巨蛇"] = "Laideronnette",
		["布弗鲁"] = "Burfurlur the Canny",
		["奥阔帕恰山S"] = "Kirlirger the Abhorrent",
		["厌忌之人 奇里格"] = "Kirlirger the Abhorrent"
	};

	private static readonly Dictionary<string, (long Ts, HuntCondition Val)> Cache = new Dictionary<string, (long, HuntCondition)>(StringComparer.Ordinal);

	private const long CacheMs = 4000L;

	public static HuntCondition? GetCondition(string? id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return null;
		}
		string text;
		if (Conditions.ContainsKey(id))
		{
			text = id;
		}
		else
		{
			text = (Aliases.TryGetValue(id, out string value) ? value : "");
		}
		if (text.Length == 0 || !Conditions.TryGetValue(text, out Entry value2))
		{
			var definition = ConditionCatalog.Find(id);
			if (definition == null) return null;
			if (!ConditionForecast.HasSpec(definition.En)) return new HuntCondition(definition.Method, null, false, Kind.Prereq);
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			var window = ConditionForecast.NextWindow(definition.En, now);
			bool active = window.HasValue && window.Value.Start <= now && now < window.Value.End;
			return new HuntCondition(ConditionForecast.LabelOf(definition.En) + (active ? " ✔" : " ✘"), active, true, Kind.Time);
		}
		bool? met = null;
		if (value2.Type != Kind.Prereq && ConditionForecast.HasSpec(text))
		{
			long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			ConditionForecast.Window? window = ConditionForecast.NextWindow(text, num);
			met = window.HasValue && window.Value.Start <= (double)num && (double)num < window.Value.End;
		}
		string text2;
		if (met.HasValue)
		{
			text2 = ((met != true) ? " ✘" : " ✔");
		}
		else
		{
			text2 = "";
		}
		string text3 = text2;
		return new HuntCondition("[" + value2.Text + text3 + "]", met, met.HasValue, value2.Type);
	}

	public static string? GetConditionText(string? id)
	{
		return GetCondition(id)?.Text;
	}
}
