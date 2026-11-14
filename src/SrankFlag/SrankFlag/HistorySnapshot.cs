using System.Collections.Generic;

namespace SrankFlag;

internal sealed class HistorySnapshot
{
	internal readonly List<HistoryRecord> Rows = new List<HistoryRecord>();

	internal int AllTotal;

	internal int Total;

	internal int Page;

	internal int PageCount = 1;

	internal HistoryFilter Filter;

	internal string Status = "";
}
