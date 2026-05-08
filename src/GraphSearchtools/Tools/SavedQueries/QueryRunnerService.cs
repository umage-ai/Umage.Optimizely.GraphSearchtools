using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

/// <summary>
/// Builds and executes interactive Graph queries for the Saved Queries
/// runner. The query is composed as a string so users can copy-paste the exact
/// text we sent to Graph (returned alongside the hits) — that's the
/// "debugging window" pattern from <c>relevancy-optimization.md</c> §9.
///
/// Two modes:
///   • Built-in: a generic <c>Content { ... }</c> query whose ranking knobs
///     are driven by the UI controls (ranking mode, semantic weight, min score).
///   • Configured default query: when
///     <see cref="SavedQueriesOptions.DefaultQuery"/> is set, that query is
///     sent verbatim with the standard variables ($q/$query/$limit/$locale/$today)
///     plus any extras from <see cref="SavedQueriesOptions.DefaultQueryVariables"/>.
///     UI knobs are ignored in this mode — the query owns its own ranking.
/// </summary>
public sealed class QueryRunnerService
{
    private static readonly HashSet<string> AllowedRankings = new(StringComparer.OrdinalIgnoreCase)
    {
        "RELEVANCE", "SEMANTIC", "BOOST_ONLY", "DOC"
    };

    private readonly HttpClient _http;
    private readonly IGraphCredentialsResolver _credentials;
    private readonly IOptions<GraphSearchtoolsOptions> _options;
    private readonly JsonSerializerOptions _serializerOptions;

    public QueryRunnerService(
        HttpClient http,
        IGraphCredentialsResolver credentials,
        IOptions<GraphSearchtoolsOptions> options)
    {
        _http = http;
        _credentials = credentials;
        _options = options;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>True when an admin has configured a default GraphQL query that
    /// replaces the built-in template. The UI uses this to lock the ranking knobs.</summary>
    public bool IsDefaultQueryActive
        => !string.IsNullOrWhiteSpace(_options.Value.SavedQueries?.DefaultQuery);

    public async Task<RunnerResult> RunAsync(RunnerRequest request, CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph query settings (GatewayAddress, SingleKey) are not configured.");
        }
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new RunnerResult(0, 0, string.Empty, Array.Empty<RunnerHit>());
        }

        var limit = Math.Clamp(request.Limit, 1, 100);
        var locales = !string.IsNullOrWhiteSpace(request.Locale) ? new[] { request.Locale } : Array.Empty<string>();
        var sq = _options.Value.SavedQueries ?? new SavedQueriesOptions();
        var useCustomQuery = !string.IsNullOrWhiteSpace(sq.DefaultQuery);

        string queryDocument;
        Dictionary<string, object?> variables;

        if (useCustomQuery)
        {
            queryDocument = sq.DefaultQuery!;
            variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["q"] = request.Query,
                ["query"] = request.Query,
                ["limit"] = limit,
                ["locale"] = locales.Length > 0 ? (object?)locales : null,
                ["today"] = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            foreach (var kvp in sq.DefaultQueryVariables ?? new())
            {
                variables[kvp.Key] = kvp.Value;
            }
        }
        else
        {
            var ranking = AllowedRankings.Contains(request.Ranking) ? request.Ranking.ToUpperInvariant() : "RELEVANCE";
            var weight = Math.Clamp(request.SemanticWeight, -1.0, 1.0);
            queryDocument = BuildQuery(ranking, weight, request.MinimumScore);
            variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["q"] = request.Query,
                ["limit"] = limit,
                ["locale"] = locales.Length > 0 ? (object?)locales : null
            };
        }

        return await SendAsync(queryDocument, variables, cancellationToken);
    }

    /// <summary>
    /// Sends a fully-formed GraphQL document to Graph as-is. Used by callers
    /// that already produced an executable query (e.g. the Profiles preview,
    /// which substitutes placeholders into the registered profile's document)
    /// and don't want the runner's <c>SavedQueries.DefaultQuery</c> /
    /// built-in template fallbacks.
    /// </summary>
    public Task<RunnerResult> RunRawAsync(string queryDocument, IDictionary<string, object?>? variables, CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph query settings (GatewayAddress, SingleKey) are not configured.");
        }
        if (string.IsNullOrWhiteSpace(queryDocument))
        {
            return Task.FromResult(new RunnerResult(0, 0, string.Empty, Array.Empty<RunnerHit>()));
        }
        return SendAsync(queryDocument, variables ?? new Dictionary<string, object?>(StringComparer.Ordinal), cancellationToken);
    }

    private async Task<RunnerResult> SendAsync(string queryDocument, IDictionary<string, object?> variables, CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        var graphqlRequest = new { query = queryDocument, variables };
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
query SavedQueriesRunner($q: String!, $limit: Int!, $locale: [Locales!]) {{
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

    private static RunnerResult ParseResult(string body, string queryDocument, long durationMs)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new RunnerResult(0, durationMs, queryDocument, Array.Empty<RunnerHit>());
        }

        if (!TryFindResultBlock(data, out var contentElement))
        {
            return new RunnerResult(0, durationMs, queryDocument, Array.Empty<RunnerHit>());
        }

        var total = ReadTotal(contentElement);
        var hits = new List<RunnerHit>();
        if (contentElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                hits.Add(new RunnerHit(
                    Name: GetString(item, "Name", "name", "Title", "title") ?? string.Empty,
                    ContentType: GetFirstContentType(item) ?? "Content",
                    Language: GetNestedString(item, "Language", "Name") ?? string.Empty,
                    ContentId: GetNestedInt(item, "ContentLink", "Id"),
                    ContentGuid: GetNestedString(item, "ContentLink", "GuidValue") ?? string.Empty,
                    Score: GetDouble(item, "_score", "score"),
                    FullTextSnippet: TrimSnippet(GetString(item, "_fulltext", "GetExcerpt", "Excerpt", "Description")),
                    Url: GetString(item, "Url", "url", "RelativePath", "relativePath", "Path", "path", "Slug", "slug"),
                    Raw: PrettyJson(item),
                    Pinned: false));
            }
        }

        return new RunnerResult(total, durationMs, queryDocument, hits);
    }

    /// <summary>
    /// Walks <c>data</c>'s direct children and returns the first object that
    /// looks like a result block (has an <c>items</c> array). This lets a
    /// configured default query use any root field name (e.g. <c>Content</c>,
    /// <c>ISearchableContent</c>, <c>ProductPage</c>).
    /// </summary>
    private static bool TryFindResultBlock(JsonElement data, out JsonElement block)
    {
        foreach (var prop in data.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            if (prop.Value.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                block = prop.Value;
                return true;
            }
        }
        block = default;
        return false;
    }

    private static int ReadTotal(JsonElement block)
    {
        if (!block.TryGetProperty("total", out var total)) return 0;
        return total.ValueKind switch
        {
            JsonValueKind.Number => total.TryGetInt32(out var n) ? n : 0,
            // total(all: true) returns a scalar number; some shapes return an object
            // like { all: 12 } — pick the first number we find.
            JsonValueKind.Object => FirstNumberInObject(total),
            _ => 0
        };
    }

    private static int FirstNumberInObject(JsonElement obj)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n)) return n;
        }
        return 0;
    }

    private static string? GetString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
            {
                var s = p.GetString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        return null;
    }

    private static string? GetNestedString(JsonElement el, string a, string b)
        => el.TryGetProperty(a, out var inner) && inner.ValueKind == JsonValueKind.Object ? GetString(inner, b) : null;

    private static int? GetNestedInt(JsonElement el, string a, string b)
    {
        if (el.TryGetProperty(a, out var inner) && inner.ValueKind == JsonValueKind.Object
            && inner.TryGetProperty(b, out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetInt32(out var n)) return n;
        return null;
    }

    private static double GetDouble(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number) return p.GetDouble();
        }
        return 0d;
    }

    private static string? GetFirstContentType(JsonElement item)
    {
        if (!item.TryGetProperty("ContentType", out var ct)) return null;
        if (ct.ValueKind == JsonValueKind.String) return ct.GetString();
        if (ct.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in ct.EnumerateArray())
            {
                var s = t.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    private static string? TrimSnippet(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return snippet;
        const int maxLength = 240;
        return snippet.Length <= maxLength ? snippet : snippet[..maxLength] + "…";
    }

    private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

    /// <summary>
    /// Returns a pretty-printed copy of the source <see cref="JsonElement"/>,
    /// or null if serialization fails. Used to feed the SERP preview's
    /// "show JSON" detail toggle so editors can inspect every field the
    /// registered profile projects, not just the heuristic-selected ones.
    /// </summary>
    private static string? PrettyJson(JsonElement element)
    {
        try { return JsonSerializer.Serialize(element, PrettyOptions); }
        catch { return null; }
    }
}
