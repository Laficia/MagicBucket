using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SrankFlag;

internal readonly record struct BearTimer(double Timestamp, bool Maintenance);

internal sealed class BearTraffic
{
    internal int SessionRequests, TimerRequests, UploadBytes, DecodedBytes;
    internal TimeSpan? RetryAfter;
}

// The public Bear website requests all selected worlds through this endpoint.
// This client creates its own anonymous CSRF session; it never uses browser login.
internal sealed class BearBulkClient : IDisposable
{
    private static readonly Uri Root = new("https://tracker.beartoolkit.com/");
    private readonly HttpClient http;
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private readonly bool ownsHttp;
    private string? csrfToken;

    internal BearBulkClient(HttpClient? client = null)
    {
        ownsHttp = client is null;
        http = client ?? new HttpClient(new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        http.BaseAddress = Root;
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.Referrer = new Uri(Root, "srank");
        if (http.DefaultRequestHeaders.UserAgent.Count == 0)
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
    }

    internal async Task<Dictionary<string, BearTimer>> FetchAsync(
        IReadOnlyCollection<string> hunts, IReadOnlyCollection<string> worlds, CancellationToken token, BearTraffic? traffic = null)
    {
        if (hunts.Count == 0 || worlds.Count == 0) return new(StringComparer.Ordinal);
        var knownHunts = hunts.ToHashSet(StringComparer.Ordinal);
        var knownWorlds = worlds.ToHashSet(StringComparer.Ordinal);
        string body = JsonSerializer.Serialize(new { QueryDeathTimers = worlds, RankType = "sRank" });
        await EnsureSessionAsync(token, traffic).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/lastDeathTimers");
        request.Headers.Add("X-CSRFToken", csrfToken!);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (traffic != null) { traffic.TimerRequests++; traffic.UploadBytes += Encoding.UTF8.GetByteCount(body); }
        using var response = await http.SendAsync(request, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden) csrfToken = null;
        byte[] bytes = await ReadBody(response, token, traffic).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        return Parse(document.RootElement, knownHunts, knownWorlds);
    }

    private async Task EnsureSessionAsync(CancellationToken token, BearTraffic? traffic)
    {
        if (csrfToken is not null) return;
        await sessionGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (csrfToken is not null) return;
            if (traffic != null) traffic.SessionRequests++;
            using var response = await http.GetAsync("", token).ConfigureAwait(false);
            string html = Encoding.UTF8.GetString(await ReadBody(response, token, traffic).ConfigureAwait(false));
            var match = Regex.Match(html,
                """<input\b(?=[^>]*\bname\s*=\s*["']csrfmiddlewaretoken["'])(?=[^>]*\bvalue\s*=\s*["'](?<token>[^"']+)["'])[^>]*>""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success) throw new HttpRequestException("公开熊站未返回匿名会话信息");
            csrfToken = WebUtility.HtmlDecode(match.Groups["token"].Value);
        }
        finally { sessionGate.Release(); }
    }

    private static async Task<byte[]> ReadBody(HttpResponseMessage response, CancellationToken token, BearTraffic? traffic)
    {
        if (traffic != null)
        {
            var retry = response.Headers.RetryAfter;
            traffic.RetryAfter = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        }
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        if (traffic != null) traffic.DecodedBytes += bytes.Length;
        response.EnsureSuccessStatusCode();
        return bytes;
    }

    internal static Dictionary<string, BearTimer> Parse(JsonElement root,
        IReadOnlySet<string> hunts, IReadOnlySet<string> worlds)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("timers", out var timers) || timers.ValueKind != JsonValueKind.Array)
            throw new JsonException("公开熊站批量计时格式无效");
        var result = new Dictionary<string, BearTimer>(StringComparer.Ordinal);
        foreach (var timer in timers.EnumerateArray())
        {
            if (timer.ValueKind != JsonValueKind.Object ||
                !timer.TryGetProperty("huntName", out var hunt) || hunt.ValueKind != JsonValueKind.String || !hunts.Contains(hunt.GetString()!) ||
                !timer.TryGetProperty("worldName", out var world) || world.ValueKind != JsonValueKind.String || !worlds.Contains(world.GetString()!) ||
                !timer.TryGetProperty("lastDeathTime", out var time) || time.ValueKind != JsonValueKind.Number ||
                !time.TryGetDouble(out double timestamp) || !double.IsFinite(timestamp) || timestamp < 0) continue;
            bool maintenance = timer.TryGetProperty("isMaint", out var flag) && flag.ValueKind == JsonValueKind.True;
            string key = hunt.GetString() + "|" + world.GetString();
            if (!result.TryGetValue(key, out var previous) || timestamp > previous.Timestamp)
                result[key] = new(timestamp, maintenance);
            else if (timestamp == previous.Timestamp && maintenance)
                result[key] = previous with { Maintenance = true };
        }
        return result;
    }

    public void Dispose()
    {
        if (ownsHttp) http.Dispose();
        // A canceled bootstrap may still be releasing this managed semaphore.
        // It owns no WaitHandle and can be collected after those continuations.
    }
}
