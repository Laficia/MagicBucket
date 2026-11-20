using System;

namespace SrankFlag;

internal sealed class MainActions
{
	internal Action<string> Search = (string _) =>
	{
	};

	internal Action NextDc = () =>
	{
	};

	internal Action ToggleOnlyOpen = () =>
	{
	};

	internal Action Refresh = () =>
	{
	};

	internal Action OpenAuto = () =>
	{
	};

	internal Action Legacy = () =>
	{
	};

	internal Action Mini = () =>
	{
	};

	internal Action StopAll = () =>
	{
	};

	internal Action AllWorlds = () =>
	{
	};

	internal Action ClearWorlds = () =>
	{
	};

	internal Action CurrentWorldOnly = () =>
	{
	};

	internal Action<string> ToggleWorld = (string _) =>
	{
	};

	internal Action<string> TogglePatch = (string _) =>
	{
	};

	internal Action<string> SelectDc = (string _) =>
	{
	};

	internal Action<string> SelectPatch = (string _) =>
	{
	};

	internal Action OpenSettings = () =>
	{
	};

	internal Action OpenHistory = () =>
	{
	};
	internal Action OpenConditions = () => { };
	internal Action OpenWatch = () => { };
	internal Action OpenHidden = () => { };
	internal Action<string> ToggleHuntFilter = _ => { };
	internal Action<string, string> ToggleWatch = (_, _) => { };

	internal Action<string, string> Primary = (string _, string _) =>
	{
	};

	internal Action<string, string, float, float> GotoPoint = (string _, string _, float _, float _) =>
	{
	};

	internal Action<string, string> GotoAetheryte = (string _, string _) =>
	{
	};

	internal Action<string, float, float> Flag = (string _, float _, float _) =>
	{
	};

	internal Action<string, string> FlagAetheryte = (string _, string _) =>
	{
	};

	internal Action<string> Expand = (string _) =>
	{
	};

	internal Action<string, string> SelectHuntWorld = (string _, string _) =>
	{
	};

	internal Action Closed = () =>
	{
	};
}
