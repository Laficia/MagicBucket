using System.Collections.Generic;

namespace SrankFlag;

internal sealed class AutoWindowSnapshot
{
	internal bool Ready;

	internal bool ShowHitBar;

	internal string Hit1 = "";

	internal string Hit2 = "";

	internal bool StyleGroup;

	internal bool StyleCard;

	internal bool OnlyOpen;

	internal bool AllWorlds;

	internal bool HudOpen;

	internal string WorldCaption = "";

	internal readonly List<string> Worlds = new List<string>();

	internal readonly List<bool> WorldOn = new List<bool>();

	internal string QueueTitle = "";

	internal readonly List<string> QueueLines = new List<string>();

	internal readonly List<AutoHuntRow> Hunts = new List<AutoHuntRow>();

	internal int Page;

	internal int PageCount;

	internal string Hints = "";

	internal string RunLabel = "";

	internal string Counts = "";

	internal string Status = "";

	internal string Skipped = "";
}
