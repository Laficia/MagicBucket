using System;
using System.Collections.Generic;

namespace SrankFlag;

public sealed partial class Plugin
{
    private readonly List<Action> pendingWindowCloses = [];
    private DateTime windowCloseAtUtc;
    private DateTime windowRecoveryUntilUtc;
    private uint lastWindowTerritory;

    private void CancelPendingWindowClose() => pendingWindowCloses.Clear();

    private void ClientState_OnLogout(int type, int code)
    {
        // Window requests belong to the current login session. Zoning keeps them.
        mainWindowOpen = false;
        settingsWindowOpen = false;
        historyWindowOpen = false;
        autoRunOpen = false;
        autoHudOpen = false;
        conditionWindowOpen = false;
        conditionFloatingOpen = false;
        hiddenHuntsOpen = false;
        CancelPendingWindowClose();
    }

    private bool IsWindowTransition()
        => IsZoning() || !clientState.IsLoggedIn || objectTable.LocalPlayer == null ||
           (lastWindowTerritory != 0 && lastWindowTerritory != clientState.TerritoryType);

    private void QueueNativeWindowClose(Action close)
    {
        var now = DateTime.UtcNow;
        if (IsWindowTransition() || now < windowRecoveryUntilUtc) return;

        // The client can hide addons before it raises the loading flags. Defer
        // interpreting that callback until the territory has stayed stable.
        pendingWindowCloses.Add(close);
        windowCloseAtUtc = now.AddMilliseconds(500);
    }

    private bool NativeWindowsReady()
    {
        var now = DateTime.UtcNow;
        bool transitioning = IsWindowTransition();
        lastWindowTerritory = clientState.TerritoryType;
        if (transitioning)
        {
            CancelPendingWindowClose();
            windowRecoveryUntilUtc = now.AddMilliseconds(750);
            return false;
        }
        if (now < windowRecoveryUntilUtc) return false;
        if (pendingWindowCloses.Count == 0) return true;
        if (now < windowCloseAtUtc) return false;

        foreach (var close in pendingWindowCloses) close();
        pendingWindowCloses.Clear();
        return true;
    }
}
