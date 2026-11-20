using System.Collections.Generic;

namespace SrankFlag;

internal readonly record struct MainHuntFilter(string Key, string Label, bool IsChecked);

internal sealed class MainSnapshot
{
	internal bool Ready;

	internal bool OnlyOpen;

	internal bool Fetching;

	internal string DataError = "";

	internal string Dc = "";

	internal string DcNext = "";

	internal string Search = "";

	internal string WorldMenuLabel = "";

	internal string Status = "";

	internal string BearStatus = "";

	internal string Counts = "";

	internal string CurrentWorld = "";

	internal bool NeedsManualRefresh;
	internal int HiddenCount;

	internal readonly List<string> Worlds = new List<string>();

	internal readonly List<bool> WorldOn = new List<bool>();

	internal readonly List<MainHuntFilter> HuntFilters = new();

	internal readonly List<string> Dcs = new List<string>();

	internal string DcSelected = "";

	internal readonly List<string> PatchKeys = new List<string>();

	internal readonly List<string> PatchLabels = new List<string>();

	internal readonly List<bool> PatchOn = new List<bool>();

	internal readonly List<MainHuntRow> Hunts = new List<MainHuntRow>();
}
