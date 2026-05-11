using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Health;

/// <summary>
/// Runs four independent probes against Optimizely Graph and surfaces them as
/// the live "Health" dashboard. Each probe is timed so the UI can plot a
/// session-latency sparkline and call out slow links without help from a
/// background service.
///
/// Probes:
///  1. <c>Gateway</c>             — HTTP reach to the gateway host (any non-network status proves reachability).
///  2. <c>Admin credentials</c>   — Basic-auth GET against <c>api/pinned/collections</c>; 2xx valid, 401/403 bad.
///  3. <c>Single key</c>          — tiny <c>__schema { queryType { name } }</c> introspection authed with SingleKey;
///                                  isolates "is the key valid" from "does the index have data".
///  4. <c>Index population</c>    — <c>Content { total(all: true) }</c>; 0 items = red, otherwise green. <c>all: true</c> counts every locale/version, not just the caller's default branch.
/// </summary>
public sealed class HealthService
{
    private readonly HttpClient _http;
    private readonly IGraphCredentialsResolver _credentials;

    public HealthService(HttpClient http, IGraphCredentialsResolver credentials)
    {
        _http = http;
        _credentials = credentials;
    }

    public async Task<HealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        var probes = new List<HealthProbeResult>();
        var overall = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(creds.GatewayAddress))
        {
            probes.Add(new HealthProbeResult("Gateway", HealthStatus.Red, "GatewayAddress is not configured.", "—", 0));
            overall.Stop();
            return Build(creds, probes, overall.ElapsedMilliseconds);
        }

        probes.Add(await ProbeGatewayAsync(creds.GatewayAddress, cancellationToken));

        probes.Add(creds.IsAdminConfigured
            ? await ProbeAdminAuthAsync(creds, cancellationToken)
            : new HealthProbeResult("Admin credentials", HealthStatus.Red, "AppKey/Secret are not configured.", AdminTarget(creds), 0));

        if (!string.IsNullOrWhiteSpace(creds.SingleKey))
        {
            probes.Add(await ProbeSingleKeyAsync(creds, cancellationToken));
            probes.Add(await ProbeIndexPopulationAsync(creds, cancellationToken));
        }
        else
        {
            probes.Add(new HealthProbeResult("Single key", HealthStatus.Red, "SingleKey is not configured.", QueryTarget(creds), 0));
            probes.Add(new HealthProbeResult("Index population", HealthStatus.Unknown, "Skipped — SingleKey is required.", QueryTarget(creds), 0));
        }

        overall.Stop();
        return Build(creds, probes, overall.ElapsedMilliseconds);
    }

    private async Task<HealthProbeResult> ProbeGatewayAsync(string gatewayAddress, CancellationToken ct)
    {
        var target = gatewayAddress.TrimEnd('/') + "/";
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            using var response = await _http.SendAsync(request, ct);
            sw.Stop();
            // Any HTTP response — even 401/404 — proves we reached the gateway.
            return new HealthProbeResult("Gateway", HealthStatus.Green, $"Reachable (HTTP {(int)response.StatusCode}).", target, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            sw.Stop();
            return new HealthProbeResult("Gateway", HealthStatus.Red, $"Cannot reach gateway: {ex.GetType().Name}.", target, sw.ElapsedMilliseconds);
        }
    }

    private async Task<HealthProbeResult> ProbeAdminAuthAsync(GraphCredentials creds, CancellationToken ct)
    {
        var target = AdminTarget(creds);
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuth(creds));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, ct);
            sw.Stop();
            if (response.IsSuccessStatusCode)
            {
                return new HealthProbeResult("Admin credentials", HealthStatus.Green, "AppKey/Secret accepted.", target, sw.ElapsedMilliseconds);
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new HealthProbeResult("Admin credentials", HealthStatus.Red, $"Rejected with HTTP {(int)response.StatusCode} — check AppKey/Secret.", target, sw.ElapsedMilliseconds);
            }
            return new HealthProbeResult("Admin credentials", HealthStatus.Amber, $"Unexpected HTTP {(int)response.StatusCode} from admin endpoint.", target, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            sw.Stop();
            return new HealthProbeResult("Admin credentials", HealthStatus.Red, $"Request failed: {ex.GetType().Name}.", target, sw.ElapsedMilliseconds);
        }
    }

    private async Task<HealthProbeResult> ProbeSingleKeyAsync(GraphCredentials creds, CancellationToken ct)
    {
        var target = QueryTarget(creds);
        var endpoint = QueryEndpoint(creds);
        var sw = Stopwatch.StartNew();
        try
        {
            // Schema introspection is the cheapest possible authed call: it
            // proves the SingleKey is accepted regardless of whether any
            // content has been indexed yet.
            const string probe = "{ __schema { queryType { name } } }";
            var payload = JsonSerializer.Serialize(new { query = probe });
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            sw.Stop();
            if (response.IsSuccessStatusCode)
            {
                return new HealthProbeResult("Single key", HealthStatus.Green, "SingleKey accepted by the query endpoint.", target, sw.ElapsedMilliseconds);
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new HealthProbeResult("Single key", HealthStatus.Red, $"Rejected with HTTP {(int)response.StatusCode} — check SingleKey.", target, sw.ElapsedMilliseconds);
            }
            return new HealthProbeResult("Single key", HealthStatus.Amber, $"Unexpected HTTP {(int)response.StatusCode} from query endpoint.", target, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            sw.Stop();
            return new HealthProbeResult("Single key", HealthStatus.Red, $"Request failed: {ex.GetType().Name}.", target, sw.ElapsedMilliseconds);
        }
    }

    private async Task<HealthProbeResult> ProbeIndexPopulationAsync(GraphCredentials creds, CancellationToken ct)
    {
        var target = QueryTarget(creds);
        var endpoint = QueryEndpoint(creds);
        var sw = Stopwatch.StartNew();
        try
        {
            const string queryDocument = "query HealthIndex { Content { total(all: true) } }";
            var payload = JsonSerializer.Serialize(new { query = queryDocument });
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new HealthProbeResult("Index population", HealthStatus.Amber, $"HTTP {(int)response.StatusCode} from content endpoint.", target, sw.ElapsedMilliseconds);
            }

            int? total = TryReadTotal(body);
            if (total == null)
            {
                return new HealthProbeResult("Index population", HealthStatus.Amber, "200 returned but Content.total was not readable.", target, sw.ElapsedMilliseconds);
            }

            return total > 0
                ? new HealthProbeResult("Index population", HealthStatus.Green, $"{total:N0} items indexed.", target, sw.ElapsedMilliseconds)
                : new HealthProbeResult("Index population", HealthStatus.Red, "Index is empty.", target, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            sw.Stop();
            return new HealthProbeResult("Index population", HealthStatus.Red, $"Request failed: {ex.GetType().Name}.", target, sw.ElapsedMilliseconds);
        }
    }

    private static int? TryReadTotal(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("Content", out var content)
                && content.TryGetProperty("total", out var total)
                && total.ValueKind == JsonValueKind.Number)
            {
                return total.GetInt32();
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static string AdminTarget(GraphCredentials creds)
        => string.IsNullOrWhiteSpace(creds.GatewayAddress)
            ? "—"
            : new Uri(new Uri(creds.GatewayAddress.TrimEnd('/') + "/", UriKind.Absolute), "api/pinned/collections").ToString();

    private static string QueryTarget(GraphCredentials creds)
        => string.IsNullOrWhiteSpace(creds.GatewayAddress)
            ? "—"
            : creds.GatewayAddress.TrimEnd('/') + "/content/v2";

    /// <summary>
    /// The actual URL the probes POST to — same as <see cref="QueryTarget"/>
    /// but with the SingleKey appended as a query parameter. We split the two
    /// so the UI can show editors a clean URL without leaking the key.
    /// </summary>
    private static string QueryEndpoint(GraphCredentials creds)
        => $"{creds.GatewayAddress.TrimEnd('/')}/content/v2?auth={creds.SingleKey}";

    private static string BuildBasicAuth(GraphCredentials creds)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.AppKey}:{creds.Secret}"));

    private static HealthResult Build(GraphCredentials creds, IReadOnlyList<HealthProbeResult> probes, long elapsedMs)
        => new(creds.GatewayAddress, creds.IsAdminConfigured, creds.IsQueryConfigured, probes, elapsedMs, DateTime.UtcNow);
}
