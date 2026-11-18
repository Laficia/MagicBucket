using System;
using System.Collections.Generic;

namespace SrankFlag;

internal sealed class ConditionActions
{
    internal Action<string> Search = _ => { };
    internal Action<string> Toggle = _ => { };
    internal Action<bool> SelectAll = _ => { };
    internal Action ToggleView = () => { };
    internal Action ToggleFloating = () => { };
    internal Action Closed = () => { };
}
internal sealed class ConditionWorld
{
    internal string Name = "", Detail = "";
    internal bool Ready;
}
internal sealed class ConditionEntry
{
    internal string En = "", Name = "", Method = "", Timer = "", Rule = "", PinnedWorld = "";
    internal string Key => PinnedWorld.Length == 0 ? En : En + "\u0001" + PinnedWorld;
    internal bool Subscribed;
    internal ConditionPhase Phase;
    internal double Seconds;
    internal readonly List<ConditionWorld> Worlds = [];
}
internal sealed class ConditionSnapshot
{
    internal string Dc = "", Search = "";
    internal bool OnlySubscribed;
    internal readonly List<ConditionEntry> Rows = [];
    internal int Total, Subscribed;
}
