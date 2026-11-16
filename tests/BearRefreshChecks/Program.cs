using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SrankFlag;

int checks = 0;
var hunts = new[] { "The Garlok", "Nandi" };
var worlds = new[] { "白银乡" };
const string html = "<input value='anonymous-test' type='hidden' name='csrfmiddlewaretoken'>";
const string payload = """
{"timers":[
{"huntName":"The Garlok","worldName":"白银乡","lastDeathTime":100,"isMaint":true},
{"huntName":"The Garlok","worldName":"白银乡","lastDeathTime":200,"isMaint":false},
{"huntName":"Nandi","worldName":"白银乡","lastDeathTime":0,"isMaint":false},
{"huntName":"The Garlok","worldName":"水晶塔","lastDeathTime":999},
{"huntName":"SS Minion","worldName":"白银乡","lastDeathTime":999},
{"huntName":"Nandi","worldName":"白银乡","lastDeathTime":"bad"}]}
""";

using (var handler = new FakeHandler(async (request, token) =>
{
    if (request.Method == HttpMethod.Get) return Response(html);
    Check(request.RequestUri?.AbsolutePath == "/api/lastDeathTimers", "Unexpected endpoint");
    Check(request.Headers.GetValues("X-CSRFToken").Single() == "anonymous-test", "Anonymous CSRF session missing");
    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
    Check(body.RootElement.GetProperty("RankType").GetString() == "sRank", "RankType is case sensitive");
    Check(body.RootElement.GetProperty("QueryDeathTimers").EnumerateArray().Select(x => x.GetString()).SequenceEqual(worlds), "Query escaped selected worlds");
    return Response(payload);
}))
using (var http = new HttpClient(handler))
using (var client = new BearBulkClient(http))
{
    var first = await client.FetchAsync(hunts, worlds, default);
    Check(first.Count == 2, "Unrelated worlds or SS minions entered the S list");
    Check(first["The Garlok|白银乡"] == new BearTimer(200, false), "A newer kill retained old maintenance");
    Check(first["Nandi|白银乡"].Timestamp == 0, "Explicit unknown record was dropped");
    await client.FetchAsync(hunts, worlds, default);
    Check(handler.GetCount == 1 && handler.PostCount == 2, "Refresh did not reuse its anonymous session");
}

using (var document = JsonDocument.Parse("""{"timers":[{"huntName":"Nandi","worldName":"白银乡","lastDeathTime":100,"isMaint":true}]}"""))
{
    var partial = BearBulkClient.Parse(document.RootElement, hunts.ToHashSet(), worlds.ToHashSet());
    Check(partial.Count == 1 && partial["Nandi|白银乡"].Maintenance, "Partial batch lost its maintenance baseline");
    var missing = hunts.Select(h => h + "|白银乡").Where(key => !partial.ContainsKey(key)).ToArray();
    Check(missing.SequenceEqual(["The Garlok|白银乡"]), "Fallback would repeat completed batch entries");
}

using (var handler = new FakeHandler((request, _) => Task.FromResult(
    request.Method == HttpMethod.Get ? Response(html) : Response("", HttpStatusCode.Forbidden))))
using (var http = new HttpClient(handler))
using (var client = new BearBulkClient(http))
{
    await Throws<HttpRequestException>(() => client.FetchAsync(hunts, worlds, default));
    Check(handler.GetCount == 2 && handler.PostCount == 2, "Forbidden response caused an unbounded session retry");
}

using (var handler = new FakeHandler((request, _) => Task.FromResult(
    request.Method == HttpMethod.Get ? Response(html) : Response("", HttpStatusCode.TooManyRequests))))
using (var http = new HttpClient(handler))
using (var client = new BearBulkClient(http))
{
    await Throws<HttpRequestException>(() => client.FetchAsync(hunts, worlds, default));
    Check(handler.PostCount == 1, "Rate limit was retried immediately");
}

using (var handler = new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response(""); }))
using (var http = new HttpClient(handler))
using (var client = new BearBulkClient(http))
using (var cancel = new CancellationTokenSource(25))
{
    await Throws<OperationCanceledException>(() => client.FetchAsync(hunts, worlds, cancel.Token));
    Check(handler.PostCount == 0, "Canceled DC switch continued loading");
}

Console.WriteLine($"PASS: {checks} checks for batch contract, completeness, session reuse, cancellation and bounded errors.");

if (args.Contains("--live"))
{
    using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "srank_data.json")));
    using var client = new BearBulkClient();
    using var legacy = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    legacy.DefaultRequestHeaders.Referrer = new Uri("https://tracker.beartoolkit.com/srank");
    foreach (var dc in data.RootElement.GetProperty("dcs").EnumerateArray())
    {
        string name = dc.GetProperty("dc").GetString()!;
        if (name == "豆豆柴") continue;
        var known = dc.GetProperty("hunts").EnumerateArray().Select(x => x.GetProperty("en").GetString()!).Distinct().ToArray();
        var selectedWorlds = dc.GetProperty("worlds").EnumerateArray().Select(x => x.GetString()!).ToArray();
        var watch = Stopwatch.StartNew();
        var result = await client.FetchAsync(known, selectedWorlds, default);
        long milliseconds = watch.ElapsedMilliseconds;
        int expected = known.Length * selectedWorlds.Length;
        Check(result.Count == expected, $"Incomplete live batch for {name}: {result.Count}/{expected}");
        var normal = result.Where(x => !x.Value.Maintenance && x.Value.Timestamp > 0).Take(4).ToArray();
        watch.Restart();
        await Parallel.ForEachAsync(normal, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (entry, token) =>
        {
            string[] pair = entry.Key.Split('|');
            using var body = new StringContent(JsonSerializer.Serialize(new { HuntName = pair[0], WorldName = pair[1] }), Encoding.UTF8, "application/json");
            using var response = await legacy.PostAsync("https://tracker.beartoolkit.com/api/getHuntInfo", body, token);
            response.EnsureSuccessStatusCode();
            using var history = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            double latest = BearRefreshPolicy.LatestKill(history.RootElement);
            if (Math.Abs(latest - entry.Value.Timestamp) > 1) throw new Exception($"Batch/history disagree for {name}/{entry.Key}");
        });
        Console.WriteLine($"LIVE: {name}: {result.Count}/{expected} pairs in {milliseconds}ms; 4 legacy samples (2 concurrent) in {watch.ElapsedMilliseconds}ms; all timestamps match.");
    }
}

void Check(bool success, string message)
{
    checks++;
    if (!success) throw new Exception(message);
}
async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { checks++; return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
static HttpResponseMessage Response(string content, HttpStatusCode status = HttpStatusCode.OK)
    => new(status) { Content = new StringContent(content) };

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    internal int GetCount, PostCount;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get) GetCount++; else PostCount++;
        return send(request, cancellationToken);
    }
}
