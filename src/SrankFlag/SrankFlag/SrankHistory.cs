using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrankFlag;

internal sealed class SrankHistory
{
	internal const int MaxRecords = 500;

	internal const int PageSize = 24;

	private const string FileName = "srank_history.json";

	private const double DedupeSeconds = 15.0;

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		WriteIndented = true,
		Converters = { (JsonConverter)new JsonStringEnumConverter() }
	};

	private readonly object gate = new object();

	private readonly List<HistoryRecord> records = new List<HistoryRecord>();

	private readonly Dictionary<string, bool> openStates = new Dictionary<string, bool>(StringComparer.Ordinal);

	private readonly string path;

	private readonly Action<string> log;

	internal SrankHistory(string pluginDirectory, Action<string> log)
	{
		path = Path.Combine(pluginDirectory, "srank_history.json");
		this.log = log;
		Load();
	}

	internal bool Add(string monster, string server, string map, HistoryEventType type, HistorySource source, double eventTime)
	{
		if (monster.Length == 0 || server.Length == 0 || eventTime <= 0.0)
		{
			return false;
		}
		try
		{
			lock (gate)
			{
				return AddLocked(monster, server, map, type, source, eventTime);
			}
		}
		catch (Exception ex)
		{
			try
			{
				log("[SrankFlag.History] 写入失败：" + ex.Message);
			}
			catch
			{
			}
			return false;
		}
	}

	internal void NoteOpenState(string key, bool openNow, string monster, string server, string map, double eventTime)
	{
		if (key.Length == 0)
		{
			return;
		}
		try
		{
			lock (gate)
			{
				if (!openStates.TryGetValue(key, out var value))
				{
					openStates[key] = openNow;
				}
				else if (value != openNow)
				{
					openStates[key] = openNow;
					if (openNow)
					{
						AddLocked(monster, server, map, HistoryEventType.Spawn, HistorySource.MainSite, eventTime);
					}
				}
			}
		}
		catch (Exception ex)
		{
			try
			{
				log("[SrankFlag.History] 进闸状态跟踪失败：" + ex.Message);
			}
			catch
			{
			}
		}
	}

	private bool AddLocked(string monster, string server, string map, HistoryEventType type, HistorySource source, double eventTime)
	{
		if (monster.Length == 0 || server.Length == 0 || eventTime <= 0.0)
		{
			return false;
		}
		foreach (HistoryRecord record in records)
		{
			if (!(Math.Abs(record.EventTime - eventTime) > 15.0) && record.EventType == type && record.Source == source && string.Equals(record.MonsterName, monster, StringComparison.Ordinal) && string.Equals(record.ServerName, server, StringComparison.Ordinal))
			{
				return false;
			}
		}
		records.Add(new HistoryRecord
		{
			MonsterName = monster,
			ServerName = server,
			MapName = (map ?? ""),
			EventType = type,
			Source = source,
			EventTime = eventTime
		});
		records.Sort((HistoryRecord a, HistoryRecord b) => b.EventTime.CompareTo(a.EventTime));
		if (records.Count > 500)
		{
			records.RemoveRange(500, records.Count - 500);
		}
		Save();
		return true;
	}

	internal HistorySnapshot Snapshot(HistoryFilter filter, int page)
	{
		HistorySnapshot historySnapshot = new HistorySnapshot
		{
			Filter = filter
		};
		lock (gate)
		{
			historySnapshot.AllTotal = records.Count;
			IEnumerable<HistoryRecord> source = records;
			switch (filter)
			{
			case HistoryFilter.KillOnly:
				source = source.Where((HistoryRecord r) => r.EventType == HistoryEventType.Kill);
				break;
			case HistoryFilter.SpawnOnly:
				source = source.Where((HistoryRecord r) => r.EventType == HistoryEventType.Spawn);
				break;
			case HistoryFilter.SonarOnly:
				source = source.Where((HistoryRecord r) => r.Source == HistorySource.Sonar);
				break;
			case HistoryFilter.MainSiteOnly:
				source = source.Where((HistoryRecord r) => r.Source == HistorySource.MainSite);
				break;
			}
			List<HistoryRecord> list = source.ToList();
			historySnapshot.Total = list.Count;
			historySnapshot.PageCount = Math.Max(1, (historySnapshot.Total + 24 - 1) / 24);
			historySnapshot.Page = Math.Clamp(page, 0, historySnapshot.PageCount - 1);
			foreach (HistoryRecord item in list.Skip(historySnapshot.Page * 24).Take(24))
			{
				historySnapshot.Rows.Add(item);
			}
			historySnapshot.Status = ((historySnapshot.Total == 0) ? $"共 {historySnapshot.AllTotal} 条 · 当前筛选无记录 · 上限 {500} 条" : $"共 {historySnapshot.AllTotal} 条 · 筛选后 {historySnapshot.Total} 条 · 第 {historySnapshot.Page + 1}/{historySnapshot.PageCount} 页 · 上限 {500} 条");
			return historySnapshot;
		}
	}

	internal void Clear()
	{
		lock (gate)
		{
			records.Clear();
			Save();
		}
	}

	private void Load()
	{
		try
		{
			if (!File.Exists(path))
			{
				return;
			}
			List<HistoryRecord> list = JsonSerializer.Deserialize<List<HistoryRecord>>(File.ReadAllText(path), JsonOpts);
			if (list == null)
			{
				return;
			}
			foreach (HistoryRecord item in list)
			{
				if (item != null)
				{
					HistoryRecord historyRecord = item;
					if (historyRecord.MonsterName == null)
					{
						string text = (historyRecord.MonsterName = "");
					}
					historyRecord = item;
					if (historyRecord.ServerName == null)
					{
						string text = (historyRecord.ServerName = "");
					}
					historyRecord = item;
					if (historyRecord.MapName == null)
					{
						string text = (historyRecord.MapName = "");
					}
					if (!(item.EventTime <= 0.0))
					{
						records.Add(item);
					}
				}
			}
			records.Sort((HistoryRecord a, HistoryRecord b) => b.EventTime.CompareTo(a.EventTime));
			if (records.Count > 500)
			{
				records.RemoveRange(500, records.Count - 500);
			}
		}
		catch (Exception ex)
		{
			try
			{
				log("[SrankFlag.History] 读历史失败（忽略，从空列表开始）：" + ex.Message);
			}
			catch
			{
			}
			records.Clear();
		}
	}

	private void Save()
	{
		try
		{
			File.WriteAllText(path, JsonSerializer.Serialize(records, JsonOpts));
		}
		catch (Exception ex)
		{
			try
			{
				log("[SrankFlag.History] 写历史失败：" + ex.Message);
			}
			catch
			{
			}
		}
	}
}
