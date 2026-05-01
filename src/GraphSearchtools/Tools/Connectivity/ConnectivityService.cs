using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Connectivity;

/// <summary>
/// Runs four independent probes against Optimizely Graph and returns a
/// red/amber/green status per probe so support can screenshot a single page
/// when a customer reports a search issue.
///
/// Probes:
///  1. <c>Gateway</c>           — TLS handshake + HTTP response from the gateway host (any non-network status proves reachability).
///  2. <c>Admin auth</c>        — Basic-auth GET against <c>api/pinned/collections</c>; 2xx = valid, 401/403 = bad credentials.
///  3. <c>Content query</c>     — small GraphQL <c>Content { total }</c> using SingleKey; 2xx with parsable JSON = working.
///  4. <c>Index population</c>  — derived from probe 3's <c>total</c>: zero items = red, otherwise green.
/// </summary>
public sealed class ConnectivityService
{
    private readonly HttpClient _http;
    private readonly IGraphCredentialsResolver _credentials;

    public ConnectivityService(HttpClient http, IGraphCredentialsResolver credentials)
    {
        _http = http;
        _credentials = credentials;
    }

    public async Task<ConnectivityResult> CheckAsync(CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        var probes = new List<ConnectivityProbeResult>();

        if (string.IsNullOrWhiteSpace(creds.GatewayAddress))
        {
            probes.Add(new ConnectivityProbeResult("Gateway", ConnectivityStatus.Red, "GatewayAddress is not configured."));
            return Build(creds, probes);
        }

        probes.Add(await ProbeGatewayAsync(creds.GatewayAddress, cancellationToken));

        probes.Add(creds.IsAdminConfigured
            ? await ProbeAdminAuthAsync(creds, cancellationToken)
            : new ConnectivityProbeResult("Admin auth", ConnectivityStatus.Red, "AppKey/Secret are not configured."));

        if (creds.IsQueryConfigured)
        {
            var (queryProbe, indexProbe) = await ProbeContentQueryAsync(creds, cancellationToken);
            probes.Add(queryProbe);
            probes.Add(indexProbe);
        }
        else
        {
            probes.Add(new ConnectivityProbeResult("Content query", ConnectivityStatus.Red, "SingleKey is not configured."));
            probes.Add(new ConnectivityProbeResult("Index population", ConnectivityStatus.Unknown, "Skipped — query probe not available."));
        }

        return Build(creds, probes);
    }

    private async Task<ConnectivityProbeResult> ProbeGatewayAsync(string gatewayAddress, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, gatewayAddress.TrimEnd('/') + "/");
            using var response = await _http.SendAsync(request, ct);
            // Any HTTP response — even 401/404 — proves we reached the gateway.
            return new ConnectivityProbeResult("Gateway", ConnectivityStatus.Green, $"Reachable (HTTP {(int)response.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ConnectivityProbeResult("Gateway", ConnectivityStatus.Red, $"Cannot reach {gatewayAddress}: {ex.GetType().Name}.");
        }
    }

    private async Task<ConnectivityProbeResult> ProbeAdminAuthAsync(GraphCredentials creds, CancellationToken ct)
    {
        try
        {
            var uri = new Uri(new Uri(creds.GatewayAddress.TrimEnd('/') + "/", UriKind.Absolute), "api/pinned/collections");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuth(creds));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return new ConnectivityProbeResult("Admin auth", ConnectivityStatus.Green, "AppKey/Secret accepted.");
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new ConnectivityProbeResult("Admin auth", ConnectivityStatus.Red, $"Rejected with HTTP {(int)response.StatusCode} — check AppKey/Secret.");
            }
            return new ConnectivityProbeResult("Admin auth", ConnectivityStatus.Amber, $"Unexpected HTTP {(int)response.StatusCode} from admin endpoint.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ConnectivityProbeResult("Admin auth", ConnectivityStatus.Red, $"Request failed: {ex.GetType().Name}.");
        }
    }

    private async Task<(ConnectivityProbeResult Query, ConnectivityProbeResult Index)> ProbeContentQueryAsync(
        GraphCredentials creds,
        CancellationToken ct)
    {
        try
        {
            var endpoint = $"{creds.GatewayAddress.TrimEnd('/')}/content/v2?auth={creds.SingleKey}";
            const string queryDocument = "query ConnectivityProbe { Content { total } }";
            var payload = JsonSerializer.Serialize(new { query = queryDocument });
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "SingleKey rejected.",
                    _ => $"HTTP {(int)response.StatusCode} from content endpoint."
                };
                return (
                    new ConnectivityProbeResult("Content query", ConnectivityStatus.Red, message),
                    new ConnectivityProbeResult("Index population", ConnectivityStatus.Unknown, "Skipped — query did not return data.")
                );
            }

            int? total = TryReadTotal(body);
            if (total == null)
            {
                return (
                    new ConnectivityProbeResult("Content query", ConnectivityStatus.Amber, "Endpoint returned 200 but no Content.total field."),
                    new ConnectivityProbeResult("Index population", ConnectivityStatus.Unknown, "Skipped — total not parseable.")
                );
            }

            var queryResult = new ConnectivityProbeResult("Content query", ConnectivityStatus.Green, $"Returned Content.total = {total}.");
            var indexResult = total > 0
                ? new ConnectivityProbeResult("Index population", ConnectivityStatus.Green, $"{total} items indexed.")
                : new ConnectivityProbeResult("Index population", ConnectivityStatus.Red, "Index is empty.");
            return (queryResult, indexResult);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (
                new ConnectivityProbeResult("Content query", ConnectivityStatus.Red, $"Request failed: {ex.GetType().Name}."),
                new ConnectivityProbeResult("Index population", ConnectivityStatus.Unknown, "Skipped — query did not run.")
            );
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

    private static string BuildBasicAuth(GraphCredentials creds)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.AppKey}:{creds.Secret}"));

    private static ConnectivityResult Build(GraphCredentials creds, IReadOnlyList<ConnectivityProbeResult> probes)
        => new(creds.GatewayAddress, creds.IsAdminConfigured, creds.IsQueryConfigured, probes, DateTime.UtcNow);
}
