using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchConsole;

/// <summary>
/// Builds and executes interactive Graph queries for the Search Console tool.
/// The query is composed as a string so users can copy-paste the exact text we
/// sent to Graph (returned alongside the hits) — that's the "debugging window"
/// pattern from <c>relevancy-optimization.md</c> §9.
/// </summary>
public sealed class SearchConsoleService
{
    private static readonly HashSet<string> AllowedRankings = new(StringComparer.OrdinalIgnoreCase)
    {
        "RELEVANCE", "SEMANTIC", "BOOST_ONLY", "DOC"
    };

    private readonly HttpClient _http;
    private readonly IGraphCredentialsResolver _credentials;
    private readonly JsonSerializerOptions _serializerOptions;

    public SearchConsoleService(HttpClient http, IGraphCredentialsResolver credentials)
    {
        _http = http;
        _credentials = credentials;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<SearchResult> RunAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph query settings (GatewayAddress, SingleKey) are not configured.");
        }
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new SearchResult(0, 0, string.Empty, Array.Empty<SearchHit>());
        }

        var ranking = AllowedRankings.Contains(request.Ranking) ? request.Ranking.ToUpperInvariant() : "RELEVANCE";
        var weight = Math.Clamp(request.SemanticWeight, -1.0, 1.0);
        var limit = Math.Clamp(request.Limit, 1, 100);

        var queryDocument = BuildQuery(ranking, weight, request.MinimumScore);
        var locales = !string.IsNullOrWhiteSpace(request.Locale) ? new[] { request.Locale } : Array.Empty<string>();
        var graphqlRequest = new
        {
            query = queryDocument,
            variables = new
            {
                q = request.Query,
                limit,
                locale = locales.Length > 0 ? (object)locales : null
            }
        };

        var json = JsonSerializer.Serialize(graphqlRequest, _serializerOptions);
        var endpoint = $"{creds.GatewayAddress.TrimEnd('/')}/content/v2?auth={creds.SingleKey}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            throw new GraphSearchApiException(response.StatusCode, body);
        }

        return ParseResult(body, queryDocument, sw.ElapsedMilliseconds);
    }

    private static string BuildQuery(string ranking, double weight, double? minimumScore)
    {
        var orderBy = new StringBuilder();
        orderBy.Append("orderBy: { _ranking: ").Append(ranking);
        orderBy.Append(", _semanticWeight: ").Append(weight.ToString("0.###", CultureInfo.InvariantCulture));
        if (minimumScore.HasValue)
        {
            orderBy.Append(", _minimumScore: ").Append(minimumScore.Value.ToString("0.###", CultureInfo.InvariantCulture));
        }
        orderBy.Append(" }");

        return $@"
query SearchConsole($q: String!, $limit: Int!, $locale: [Locales!]) {{
    Content(
        limit: $limit
        locale: $locale
        where: {{ _fulltext: {{ match: $q }} }}
        {orderBy}
    ) {{
        total
        items {{
            Name
            ContentType
            Language {{ Name }}
            ContentLink {{ Id GuidValue }}
            _score
            _fulltext
        }}
    }}
}}";
    }

    private static SearchResult ParseResult(string body, string queryDocument, long durationMs)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("Content", out var contentElement))
        {
            return new SearchResult(0, durationMs, queryDocument, Array.Empty<SearchHit>());
        }

        int total = contentElement.TryGetProperty("total", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number
            ? totalEl.GetInt32()
            : 0;

        var hits = new List<SearchHit>();
        if (contentElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                hits.Add(new SearchHit(
                    Name: GetString(item, "Name") ?? string.Empty,
                    ContentType: GetFirstContentType(item) ?? "Content",
                    Language: GetNestedString(item, "Language", "Name") ?? string.Empty,
                    ContentId: GetNestedInt(item, "ContentLink", "Id"),
                    ContentGuid: GetNestedString(item, "ContentLink", "GuidValue") ?? string.Empty,
                    Score: GetDouble(item, "_score"),
                    FullTextSnippet: TrimSnippet(GetString(item, "_fulltext"))));
            }
        }

        return new SearchResult(total, durationMs, queryDocument, hits);
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? GetNestedString(JsonElement el, string a, string b)
        => el.TryGetProperty(a, out var inner) && inner.ValueKind == JsonValueKind.Object ? GetString(inner, b) : null;

    private static int? GetNestedInt(JsonElement el, string a, string b)
    {
        if (el.TryGetProperty(a, out var inner) && inner.ValueKind == JsonValueKind.Object
            && inner.TryGetProperty(b, out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetInt32(out var n)) return n;
        return null;
    }

    private static double GetDouble(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0d;

    private static string? GetFirstContentType(JsonElement item)
    {
        if (!item.TryGetProperty("ContentType", out var ct) || ct.ValueKind != JsonValueKind.Array) return null;
        foreach (var t in ct.EnumerateArray())
        {
            var s = t.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static string? TrimSnippet(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return snippet;
        const int maxLength = 240;
        return snippet.Length <= maxLength ? snippet : snippet[..maxLength] + "…";
    }
}
