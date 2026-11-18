using System;

namespace SrankFlag;

internal enum TeleportArrivalState { Waiting, Arrived, TimedOut }

// A cast request is not arrival. Same-zone teleports must actually zone before
// becoming ready; cross-zone travel must reach the expected world AND territory.
internal sealed class TeleportArrivalGate(string world, uint territory, bool startedInDestination)
{
    private bool sawZoning;
    private TimeSpan? stableSince;
    private TeleportArrivalState state;

    internal static bool Matches(string expectedWorld, uint expectedTerritory,
        string currentWorld, uint currentTerritory, bool zoning, bool playerReady)
        => expectedTerritory != 0 && currentTerritory == expectedTerritory &&
           string.Equals(expectedWorld, currentWorld, StringComparison.OrdinalIgnoreCase) &&
           !zoning && playerReady;

    internal TeleportArrivalState Observe(string currentWorld, uint currentTerritory,
        bool zoning, bool playerReady, TimeSpan elapsed)
    {
        if (state != TeleportArrivalState.Waiting) return state;
        if (elapsed >= TimeSpan.FromSeconds(90)) return state = TeleportArrivalState.TimedOut;
        sawZoning |= zoning;
        if (Matches(world, territory, currentWorld, currentTerritory, zoning, playerReady) &&
            (!startedInDestination || sawZoning))
        {
            stableSince ??= elapsed;
            if (elapsed - stableSince.Value >= TimeSpan.FromMilliseconds(500))
                return state = TeleportArrivalState.Arrived;
        }
        else stableSince = null;
        return TeleportArrivalState.Waiting;
    }
}
