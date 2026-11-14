namespace SrankFlag;

public sealed class HistoryRecord
{
	public string MonsterName { get; set; } = "";

	public string ServerName { get; set; } = "";

	public HistoryEventType EventType { get; set; }

	public HistorySource Source { get; set; }

	public double EventTime { get; set; }

	public string MapName { get; set; } = "";
}
