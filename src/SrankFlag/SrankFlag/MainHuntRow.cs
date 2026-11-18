using System.Collections.Generic;

namespace SrankFlag;

internal sealed class MainHuntRow
{
	internal string Key = "";

	internal string En = "";

	internal string World = "";

	internal string Cn = "";

	internal string Map = "";

	internal string RowLabel = "";

	internal string ProgressLabel = "";

	internal double PctValue;
	internal double EtaSeconds;

	internal string State = "";

	internal string Pct = "";

	internal string Patch = "";

	internal string Eta = "";

	internal string Clock = "";

	internal string BestWorld = "";

	internal string PrimaryLabel = "";

	internal string Condition = "";
	internal string ConditionRange = "";
	internal ConditionPhase ConditionPhase;

	internal string Tips = "";

	internal string PointsHint = "";

	internal bool Expanded;
	internal bool Watched;

	internal bool HasSpecial;

	internal bool Triggered;

	internal readonly List<MainPointRow> Points = new List<MainPointRow>();
}
