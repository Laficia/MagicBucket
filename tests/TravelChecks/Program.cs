using SrankFlag;

int checks = 0;
const string World = "白金幻象";
const uint Destination = 1191;
TeleportArrivalState Read(TeleportArrivalGate gate, double seconds, uint territory = Destination,
    string world = World, bool zoning = false, bool ready = true)
    => gate.Observe(world, territory, zoning, ready, TimeSpan.FromSeconds(seconds));
void Check(bool ok, string name)
{
    checks++;
    if (!ok) throw new Exception(name);
}

// Screenshot regression: cross-world succeeds, map teleport never happens.
// Timeout must never grant permission to craft or run a trigger.
var stalled = new TeleportArrivalGate(World, Destination, false);
int preparationCalls = 0;
for (int seconds = 0; seconds <= 125; seconds++)
    if (Read(stalled, seconds, territory: 960) == TeleportArrivalState.Arrived) preparationCalls++;
Check(preparationCalls == 0, "A failed teleport continued into preparation");
Check(Read(stalled, 126) == TeleportArrivalState.TimedOut, "A timed-out request revived after later map arrival");

var wrongWorld = new TeleportArrivalGate(World, Destination, false);
Check(Read(wrongWorld, 0, world: "神意之地") == TeleportArrivalState.Waiting, "Wrong world accepted");
Check(Read(wrongWorld, 10, world: "神意之地") == TeleportArrivalState.Waiting, "Matching map in another world accepted");
Check(Read(wrongWorld, 11) == TeleportArrivalState.Waiting, "Arrival was not stabilized");
Check(Read(wrongWorld, 11.5) == TeleportArrivalState.Arrived, "Valid cross-world arrival refused");

var transition = new TeleportArrivalGate(World, Destination, false);
Check(Read(transition, 0, zoning: true, ready: false) == TeleportArrivalState.Waiting, "Loading accepted as arrival");
Check(Read(transition, 1, ready: false) == TeleportArrivalState.Waiting, "Missing/unready player accepted");
Check(Read(transition, 2) == TeleportArrivalState.Waiting, "First loaded snapshot accepted");
Check(Read(transition, 2.25, zoning: true) == TeleportArrivalState.Waiting, "A second loading phase was ignored");
Check(Read(transition, 3) == TeleportArrivalState.Waiting, "Stability survived a loading phase");
Check(Read(transition, 3.5) == TeleportArrivalState.Arrived, "Stable landed player refused");

var sameZone = new TeleportArrivalGate(World, Destination, true);
Check(Read(sameZone, 0) == TeleportArrivalState.Waiting, "Same-zone request immediately counted as arrival");
Check(Read(sameZone, 4, ready: false) == TeleportArrivalState.Waiting, "A cast counted as zoning");
Check(Read(sameZone, 8) == TeleportArrivalState.Waiting, "Canceled cast counted as arrival");
Check(Read(sameZone, 89) == TeleportArrivalState.Waiting, "Same-zone timeout continued without a zone transition");
Check(Read(sameZone, 90) == TeleportArrivalState.TimedOut, "Same-zone failure did not stop");
var sameZoneSuccess = new TeleportArrivalGate(World, Destination, true);
Read(sameZoneSuccess, 3, zoning: true, ready: false);
Check(Read(sameZoneSuccess, 6) == TeleportArrivalState.Waiting, "Same-zone arrival was not stabilized");
Check(Read(sameZoneSuccess, 6.5) == TeleportArrivalState.Arrived, "Actual same-zone teleport failed");

var invalid = new TeleportArrivalGate(World, 0, false);
Check(Read(invalid, 1, territory: 0) == TeleportArrivalState.Waiting, "Unknown territory counted as arrival");
Console.WriteLine($"PASS: {checks} travel regressions (stalled, wrong world/map, loading, canceled cast, same-zone arrival, timeout).");
