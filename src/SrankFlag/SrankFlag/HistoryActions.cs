using System;

namespace SrankFlag;

internal sealed class HistoryActions
{
	internal Action<HistoryFilter> SetFilter = (HistoryFilter _) =>
	{
	};

	internal Action<int> PageBy = (int _) =>
	{
	};

	internal Action Clear = () =>
	{
	};

	internal Action Closed = () =>
	{
	};
}
