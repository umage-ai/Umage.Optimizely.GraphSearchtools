using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// Default <see cref="IGraphAdminClient"/>: talks to the Optimizely Graph admin
/// REST API via HttpClient with Basic auth (AppKey:Secret), and to the content
/// GraphQL endpoint with a <c>?auth={SingleKey}</c> query parameter. The
/// content-type allow-list is supplied per call by the calling tool service so
/// vanilla CMS installs work out of the box without any configuration.
/// </summary>
internal sealed class GraphAdminClient : IGraphAdminClient
{
    private readonly HttpClient _httpClient;
    private readonly IGraphCredentialsResolver _credentials;
    private readonly JsonSerializerOptions _serializerOptions;

    public GraphAdminClient(HttpClient httpClient, IGraphCredentialsResolver credentials)
    {
        _httpClient = httpClient;
        _credentials = credentials;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<IReadOnlyList<PinnedCollectionResult>> GetCollectionsAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "api/pinned/collections");
        var result = await SendJsonAsync<List<PinnedCollectionResult>>(request, cancellationToken);
        return result ?? new List<PinnedCollectionResult>();
    }

    public async Task<PinnedCollectionResult> CreateCollectionAsync(PinnedCollectionPayload payload, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, "api/pinned/collections", payload);
        return await SendJsonAsync<PinnedCollectionResult>(request, cancellationToken)
            ?? throw new InvalidOperationException("Graph API returned an empty collection response.");
    }

    public async Task<PinnedCollectionResult> UpdateCollectionAsync(string collectionId, PinnedCollectionUpdatePayload payload, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Put, $"api/pinned/collections/{Uri.EscapeDataString(collectionId)}", payload);
        return await SendJsonAsync<PinnedCollectionResult>(request, cancellationToken)
            ?? throw new InvalidOperationException("Graph API returned an empty collection response.");
    }

    public async Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"api/pinned/collections/{Uri.EscapeDataString(collectionId)}");
        await SendNoContentAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<PinnedItemResult>> GetItemsAsync(string collectionId, CancellationToken cancellationToken, int offset = 0)
    {
        var path = $"api/pinned/collections/{Uri.EscapeDataString(collectionId)}/items";
        if (offset > 0)
        {
            path += $"?offset={offset}";
        }
        using var request = CreateRequest(HttpMethod.Get, path);
        var result = await SendJsonAsync<List<PinnedItemResult>>(request, cancellationToken);
        return result ?? new List<PinnedItemResult>();
    }

    public async Task<PinnedItemResult> CreateItemAsync(string collectionId, PinnedItemPayload payload, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, $"api/pinned/collections/{Uri.EscapeDataString(collectionId)}/items", payload);
        return await SendJsonAsync<PinnedItemResult>(request, cancellationToken)
            ?? throw new InvalidOperationException("Graph API returned an empty pinned item response.");
    }

    public async Task<PinnedItemResult> UpdateItemAsync(string collectionId, string id, PinnedItemPayload payload, CancellationToken cancellationToken)
    {
        var path = $"api/pinned/collections/{Uri.EscapeDataString(collectionId)}/items/{Uri.EscapeDataString(id)}";
        using var request = CreateJsonRequest(HttpMethod.Put, path, payload);
        return await SendJsonAsync<PinnedItemResult>(request, cancellationToken)
            ?? throw new InvalidOperationException("Graph API returned an empty pinned item response.");
    }

    public async Task DeleteItemAsync(string collectionId, string id, CancellationToken cancellationToken)
    {
        var path = $"api/pinned/collections/{Uri.EscapeDataString(collectionId)}/items/{Uri.EscapeDataString(id)}";
        using var request = CreateRequest(HttpMethod.Delete, path);
        await SendNoContentAsync(request, cancellationToken);
    }

    public async Task<string> GetSynonymsAsync(SynonymsQuery query, CancellationToken cancellationToken)
    {
        var path = BuildSynonymsPath(query);
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GraphSearchApiException(response.StatusCode, content);
        }
        return content;
    }

    public async Task UpdateSynonymsAsync(SynonymsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = BuildSynonymsPath(new SynonymsQuery
        {
            LanguageRouting = request.LanguageRouting,
            SourceRouting = request.SourceRouting,
            Slot = request.Slot
        });

        using var httpRequest = CreateRequest(HttpMethod.Put, path);
        // text/plain is required by the upstream synonyms endpoint — pinned upstream contract.
        httpRequest.Content = new StringContent(request.Content ?? string.Empty, Encoding.UTF8, "text/plain");
        await SendNoContentAsync(httpRequest, cancellationToken);
    }

    public async Task DeleteSynonymsAsync(SynonymsQuery query, CancellationToken cancellationToken)
    {
        var path = BuildSynonymsPath(query);
        using var request = CreateRequest(HttpMethod.Delete, path);
        await SendNoContentAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<ContentSearchHit>> SearchContentAsync(
        string query,
        string? locale,
        IReadOnlyList<string> contentTypes,
        CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph query settings (GatewayAddress, SingleKey) are not configured.");
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ContentSearchHit>();
        }

        var allowList = NormalizeContentTypes(contentTypes);
        var namePattern = $"%{query}%";
        var locales = !string.IsNullOrWhiteSpace(locale) ? new[] { locale } : Array.Empty<string>();

#if OPTIMIZELY_CMS13
        // CMS 13's Graph schema renamed the root collection to `_Content` and
        // moved every type-system field (Name, ContentType, Language,
        // ContentLink) into a unified `_metadata` block. The parser in
        // ExecuteContentQueryAsync reads both shapes, so the same code path
        // accepts items from either TFM's query.
        var contentTypeFilter = BuildContentTypeFilter(allowList, "_metadata", "types");
        var graphqlRequest = new
        {
            query = $@"
                query ContentPickerSearch($searchPhrase: String!, $namePattern: String!, $limit: Int!, $locale: [Locales!]) {{
                    _Content(
                        limit: $limit
                        locale: $locale
                        where: {{
                            _and: [
                                {contentTypeFilter}
                                {{ _or: [
                                    {{ _fulltext: {{ contains: $searchPhrase }} }}
                                    {{ _metadata: {{ displayName: {{ like: $namePattern }} }} }}
                                ] }}
                            ]
                        }}
                        orderBy: {{ _ranking: RELEVANCE }}
                    ) {{
                        items {{
                            _metadata {{
                                displayName
                                types
                                locale
                                key
                            }}
                        }}
                    }}
                }}",
            variables = new
            {
                searchPhrase = query,
                namePattern,
                limit = 20,
                locale = locales.Length > 0 ? (object)locales : null
            }
        };
#else
        var contentTypeFilter = BuildContentTypeFilter(allowList, "ContentType");
        var graphqlRequest = new
        {
            query = $@"
                query ContentPickerSearch($searchPhrase: String!, $namePattern: String!, $limit: Int!, $locale: [Locales!]) {{
                    Content(
                        limit: $limit
                        locale: $locale
                        where: {{
                            _and: [
                                {contentTypeFilter}
                                {{ _or: [
                                    {{ _fulltext: {{ contains: $searchPhrase }} }}
                                    {{ Name: {{ like: $namePattern }} }}
                                ] }}
                            ]
                        }}
                        orderBy: {{ _ranking: RELEVANCE }}
                    ) {{
                        items {{
                            Name
                            ContentType
                            ContentLink {{ GuidValue }}
                            Language {{ Name }}
                        }}
                    }}
                }}",
            variables = new
            {
                searchPhrase = query,
                namePattern,
                limit = 20,
                locale = locales.Length > 0 ? (object)locales : null
            }
        };
#endif

        return await ExecuteContentQueryAsync(creds, graphqlRequest, allowList, deduplicate: false, cancellationToken);
    }

    public async Task<IReadOnlyList<ContentSearchHit>> ResolveByGuidsAsync(
        IReadOnlyList<string> guids,
        IReadOnlyList<string> contentTypes,
        CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph query settings (GatewayAddress, SingleKey) are not configured.");
        }
        if (guids == null || guids.Count == 0)
        {
            return Array.Empty<ContentSearchHit>();
        }

        var allowList = NormalizeContentTypes(contentTypes);

#if OPTIMIZELY_CMS13
        var graphqlRequest = new
        {
            query = @"
                query ResolveGuids($guids: [String!]!) {
                    _Content(
                        limit: 100
                        where: {
                            _metadata: { key: { in: $guids } }
                        }
                    ) {
                        items {
                            _metadata {
                                displayName
                                types
                                locale
                                key
                            }
                        }
                    }
                }",
            variables = new { guids }
        };
#else
        var graphqlRequest = new
        {
            query = @"
                query ResolveGuids($guids: [String!]!) {
                    Content(
                        limit: 100
                        where: {
                            ContentLink: { GuidValue: { in: $guids } }
                        }
                    ) {
                        items {
                            Name
                            ContentType
                            ContentLink { GuidValue }
                            Language { Name }
                        }
                    }
                }",
            variables = new { guids }
        };
#endif

        return await ExecuteContentQueryAsync(creds, graphqlRequest, allowList, deduplicate: true, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetGraphLocalesAsync(CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph query settings (GatewayAddress, SingleKey) are not configured.");
        }

        // The schema's `Locales` enum is generated from the registered languages
        // in the index, so introspecting it returns exactly what Graph can serve
        // — independent of how the host CMS is configured.
        const string introspection = @"
            query GraphLocales {
                __type(name: ""Locales"") {
                    enumValues { name }
                }
            }";

        var json = JsonSerializer.Serialize(new { query = introspection }, _serializerOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildQueryEndpoint(creds))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GraphSearchApiException(response.StatusCode, body);
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("__type", out var type)
            || type.ValueKind != JsonValueKind.Object
            || !type.TryGetProperty("enumValues", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            var name = GetString(value, "name");
            if (string.IsNullOrEmpty(name)) continue;
            // The schema includes two synthetic enum values: `ALL` (the wildcard
            // for "any locale") and `neutralLanguage` (a placeholder for content
            // without a language). Neither corresponds to a real branch; the UI
            // already exposes "no locale filter" as its own picker option, so
            // surfacing these would just produce duplicate / nonsensical entries.
            if (string.Equals(name, "ALL", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, "neutralLanguage", StringComparison.OrdinalIgnoreCase)) continue;
            results.Add(name!);
        }
        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    private static string? GetString(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object
           && el.TryGetProperty(name, out var p)
           && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private async Task<IReadOnlyList<ContentSearchHit>> ExecuteContentQueryAsync(
        GraphCredentials creds,
        object graphqlRequest,
        IReadOnlyList<string> allowList,
        bool deduplicate,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(graphqlRequest, _serializerOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildQueryEndpoint(creds))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GraphSearchApiException(response.StatusCode, content);
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<ContentSearchHit>();
        }

        // CMS 13 renamed the root collection field from `Content` to `_Content`.
        // Try both so the parser can handle either schema regardless of which
        // TFM produced the binary — useful if a host runs against a CMS 12-shaped
        // tenant from a net10.0 build during a migration.
        if (!data.TryGetProperty("Content", out var contentElement)
            && !data.TryGetProperty("_Content", out contentElement))
        {
            return Array.Empty<ContentSearchHit>();
        }
        if (!contentElement.TryGetProperty("items", out var items))
        {
            return Array.Empty<ContentSearchHit>();
        }

        var seen = deduplicate ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
        var results = new List<ContentSearchHit>();
        foreach (var item in items.EnumerateArray())
        {
            // CMS 12 shape: ContentLink.GuidValue, Name, Language.Name, ContentType[]
            // CMS 13 shape: _metadata.{ key, displayName, locale, types[] }
            // Read the metadata block once and fall back to the flat fields.
            var meta = item.TryGetProperty("_metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;

            var guidValue =
                (meta.ValueKind == JsonValueKind.Object ? GetString(meta, "key") : null)
                ?? GetNestedString(item, "ContentLink", "GuidValue")
                ?? string.Empty;
            if (seen != null && !seen.Add(guidValue)) continue;

            var name =
                (meta.ValueKind == JsonValueKind.Object ? GetString(meta, "displayName") : null)
                ?? GetString(item, "Name")
                ?? string.Empty;
            var language =
                (meta.ValueKind == JsonValueKind.Object ? GetString(meta, "locale") : null)
                ?? GetNestedString(item, "Language", "Name")
                ?? string.Empty;

            var contentType = ResolveDisplayContentType(item, meta, allowList);

            results.Add(new ContentSearchHit
            {
                Name = name,
                ContentGuid = guidValue,
                Language = language,
                ContentType = contentType
            });
        }
        return results;
    }

    private static string ResolveDisplayContentType(JsonElement item, JsonElement meta, IReadOnlyList<string> allowList)
    {
        // CMS 12 surfaces the inheritance chain on the item itself
        // (`ContentType: [...]`); CMS 13 nests it under `_metadata.types`.
        JsonElement types;
        if (item.TryGetProperty("ContentType", out var ct) && ct.ValueKind == JsonValueKind.Array)
        {
            types = ct;
        }
        else if (meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array)
        {
            types = t;
        }
        else
        {
            return "Content";
        }

        foreach (var t in types.EnumerateArray())
        {
            var typeName = t.GetString();
            if (string.IsNullOrWhiteSpace(typeName)) continue;
            if (allowList.Count == 0 || allowList.Contains(typeName, StringComparer.OrdinalIgnoreCase))
            {
                return typeName!;
            }
        }
        return "Content";
    }

    private static string? GetNestedString(JsonElement el, string outer, string inner)
        => el.ValueKind == JsonValueKind.Object
           && el.TryGetProperty(outer, out var middle)
           && middle.ValueKind == JsonValueKind.Object
            ? GetString(middle, inner)
            : null;

    private static IReadOnlyList<string> NormalizeContentTypes(IReadOnlyList<string>? contentTypes)
    {
        if (contentTypes == null) return Array.Empty<string>();
        return contentTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildContentTypeFilter(IReadOnlyList<string> allowList, string fieldName)
    {
        if (allowList.Count == 0) return string.Empty;
        var quoted = string.Join(", ", allowList.Select(t => $"\"{t.Replace("\"", "\\\"")}\""));
        return $"{{ {fieldName}: {{ in: [{quoted}] }} }}";
    }

    /// <summary>
    /// Two-level variant used for the CMS 13 schema where the type-system
    /// filters live under <c>_metadata</c>: produces
    /// <c>{ outer: { inner: { in: [...] } } }</c>. Returns empty when
    /// <paramref name="allowList"/> is empty so the caller can drop the
    /// clause from the composed <c>_and</c>.
    /// </summary>
    private static string BuildContentTypeFilter(IReadOnlyList<string> allowList, string outerField, string innerField)
    {
        if (allowList.Count == 0) return string.Empty;
        var quoted = string.Join(", ", allowList.Select(t => $"\"{t.Replace("\"", "\\\"")}\""));
        return $"{{ {outerField}: {{ {innerField}: {{ in: [{quoted}] }} }} }}";
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var creds = _credentials.Resolve();
        EnsureAdminConfigured(creds);
        var uri = BuildAdminUri(creds, path);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuthValue(creds));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object payload)
    {
        var request = CreateRequest(method, path);
        var json = JsonSerializer.Serialize(payload, _serializerOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<T?> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GraphSearchApiException(response.StatusCode, content);
        }
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(content, _serializerOptions);
    }

    private async Task SendNoContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GraphSearchApiException(response.StatusCode, content);
        }
    }

    private static string BuildSynonymsPath(SynonymsQuery? query)
    {
        var queryString = BuildQueryString(new Dictionary<string, string?>
        {
            ["language_routing"] = query?.LanguageRouting,
            ["source_routing"] = query?.SourceRouting,
            ["synonym_slot"] = query?.Slot
        });

        return string.IsNullOrWhiteSpace(queryString)
            ? "resources/synonyms"
            : $"resources/synonyms{queryString}";
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string?> parameters)
    {
        var builder = new StringBuilder();
        foreach (var parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Value)) continue;
            builder.Append(builder.Length == 0 ? '?' : '&');
            builder.Append(Uri.EscapeDataString(parameter.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(parameter.Value));
        }
        return builder.ToString();
    }

    private static Uri BuildAdminUri(GraphCredentials creds, string path)
    {
        var gatewayAddress = creds.GatewayAddress.TrimEnd('/') + "/";
        return new Uri(new Uri(gatewayAddress, UriKind.Absolute), path.TrimStart('/'));
    }

    private static string BuildQueryEndpoint(GraphCredentials creds)
        => $"{creds.GatewayAddress.TrimEnd('/')}/content/v2?auth={creds.SingleKey}";

    private static string BuildBasicAuthValue(GraphCredentials creds)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.AppKey}:{creds.Secret}"));

    private static void EnsureAdminConfigured(GraphCredentials creds)
    {
        if (!creds.IsAdminConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph settings (GatewayAddress, AppKey, Secret) are not configured.");
        }
    }

}
