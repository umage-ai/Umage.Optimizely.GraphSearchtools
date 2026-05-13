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
public sealed class GraphAdminClient : IGraphAdminClient
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
        var contentTypeFilter = BuildContentTypeFilter(allowList, "ContentType");
        var namePattern = $"%{query}%";
        var locales = !string.IsNullOrWhiteSpace(locale) ? new[] { locale } : Array.Empty<string>();

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

        return await ExecuteContentQueryAsync(creds, graphqlRequest, allowList, deduplicate: true, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> AutocompleteAsync(
        string typeName,
        string field,
        string value,
        string? locale,
        int limit,
        CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph query settings (GatewayAddress, SingleKey) are not configured.");
        }
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        if (!IsValidGraphIdentifier(typeName)) throw new ArgumentException("typeName must be a GraphQL identifier.", nameof(typeName));
        if (!IsValidGraphIdentifier(field)) throw new ArgumentException("field must be a GraphQL identifier.", nameof(field));

        var clampedLimit = Math.Clamp(limit, 1, 25);
        var locales = !string.IsNullOrWhiteSpace(locale) ? new[] { locale } : Array.Empty<string>();

        // autocomplete is nested under a content collection (NOT root-level), and
        // each per-type `<Type>Autocomplete` only exposes fields explicitly marked
        // Searchable in the index. typeName + field are validated as GraphQL
        // identifiers (no injection vector) so we inline them safely.
        var graphqlRequest = new
        {
            query = $@"
                query Autocomplete($value: String!, $limit: Int!, $locale: [Locales!]) {{
                    {typeName}(limit: 0, locale: $locale) {{
                        autocomplete {{
                            {field}(value: $value, limit: $limit)
                        }}
                    }}
                }}",
            variables = new
            {
                value,
                limit = clampedLimit,
                locale = locales.Length > 0 ? (object)locales : null
            }
        };

        var json = JsonSerializer.Serialize(graphqlRequest, _serializerOptions);
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
            || !data.TryGetProperty(typeName, out var contentNode)
            || !contentNode.TryGetProperty("autocomplete", out var autocomplete)
            || !autocomplete.TryGetProperty(field, out var suggestions)
            || suggestions.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        foreach (var item in suggestions.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s)) results.Add(s);
            }
        }
        return results;
    }

    public async Task<IReadOnlyList<AutocompleteFieldDescriptor>> GetAutocompleteSchemaAsync(CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            throw new InvalidOperationException("Optimizely Content Graph query settings (GatewayAddress, SingleKey) are not configured.");
        }

        // One round-trip discovers every root field whose return type carries an
        // `autocomplete` sub-field; for each such autocomplete type we read the
        // scalar (LIST/String) child fields. Object-typed children (ContentLink,
        // Language, ParentLink, …) are intentionally skipped — they need a
        // second-level selection that the picker UX doesn't model.
        //
        // Both `type.fields` and `type.ofType.fields` are requested at every
        // level because the gateway returns the FIELDS list either directly
        // (unwrapped OBJECT) or under ofType (NonNull/List wrapper), and we
        // don't know which without inspecting the actual response.
        const string introspection = @"
            query AutocompleteSchema {
                __schema {
                    queryType {
                        fields {
                            name
                            type {
                                name
                                kind
                                fields {
                                    name
                                    type {
                                        name
                                        kind
                                        fields {
                                            name
                                            type { name kind ofType { name kind } }
                                        }
                                        ofType {
                                            name
                                            kind
                                            fields {
                                                name
                                                type { name kind ofType { name kind } }
                                            }
                                        }
                                    }
                                }
                                ofType {
                                    name
                                    kind
                                    fields {
                                        name
                                        type {
                                            name
                                            kind
                                            fields {
                                                name
                                                type { name kind ofType { name kind } }
                                            }
                                            ofType {
                                                name
                                                kind
                                                fields {
                                                    name
                                                    type { name kind ofType { name kind } }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
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

        var results = new List<AutocompleteFieldDescriptor>();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("__schema", out var schema)
            || !schema.TryGetProperty("queryType", out var queryType)
            || !queryType.TryGetProperty("fields", out var rootFields)
            || rootFields.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var rootField in rootFields.EnumerateArray())
        {
            var rootName = GetString(rootField, "name");
            if (string.IsNullOrEmpty(rootName)) continue;

            // Resolve the OBJECT type of the root field — could be wrapped in NonNull.
            var rootType = rootField.TryGetProperty("type", out var t) ? t : default;
            var typeFields = ResolveObjectFields(rootType);
            if (typeFields.ValueKind != JsonValueKind.Array) continue;

            // Find an `autocomplete` sub-field; resolve its OBJECT type's scalar fields.
            JsonElement autocompleteFieldType = default;
            var foundAutocomplete = false;
            foreach (var f in typeFields.EnumerateArray())
            {
                if (GetString(f, "name") == "autocomplete")
                {
                    autocompleteFieldType = f.TryGetProperty("type", out var ft) ? ft : default;
                    foundAutocomplete = true;
                    break;
                }
            }
            if (!foundAutocomplete) continue;

            var autocompleteFields = ResolveObjectFields(autocompleteFieldType);
            if (autocompleteFields.ValueKind != JsonValueKind.Array) continue;

            var scalarFields = new List<string>();
            foreach (var f in autocompleteFields.EnumerateArray())
            {
                var name = GetString(f, "name");
                if (string.IsNullOrEmpty(name)) continue;
                if (!IsScalarListField(f)) continue;
                scalarFields.Add(name!);
            }
            if (scalarFields.Count == 0) continue;

            scalarFields.Sort(StringComparer.OrdinalIgnoreCase);
            results.Add(new AutocompleteFieldDescriptor(rootName!, scalarFields));
        }

        results.Sort((a, b) => string.Compare(a.TypeName, b.TypeName, StringComparison.OrdinalIgnoreCase));
        return results;
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

    /// <summary>
    /// Returns the <c>fields</c> array of an OBJECT type, unwrapping NonNull
    /// (<c>ofType</c>) once if needed. Returns <see cref="JsonValueKind.Undefined"/>
    /// when the type doesn't resolve to an OBJECT with fields.
    /// </summary>
    private static JsonElement ResolveObjectFields(JsonElement typeElement)
    {
        if (typeElement.ValueKind != JsonValueKind.Object) return default;
        if (typeElement.TryGetProperty("fields", out var direct) && direct.ValueKind == JsonValueKind.Array)
        {
            return direct;
        }
        if (typeElement.TryGetProperty("ofType", out var inner) && inner.ValueKind == JsonValueKind.Object
            && inner.TryGetProperty("fields", out var innerFields) && innerFields.ValueKind == JsonValueKind.Array)
        {
            return innerFields;
        }
        return default;
    }

    /// <summary>
    /// True when the field's type resolves to a LIST of SCALAR — i.e. it
    /// supports the <c>(value, limit)</c> autocomplete arguments directly.
    /// Excludes OBJECT children like <c>ContentLink</c> / <c>Language</c>.
    /// </summary>
    private static bool IsScalarListField(JsonElement field)
    {
        if (!field.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.Object) return false;
        var kind = GetString(type, "kind");
        if (kind == "LIST")
        {
            if (!type.TryGetProperty("ofType", out var inner) || inner.ValueKind != JsonValueKind.Object) return false;
            return GetString(inner, "kind") == "SCALAR";
        }
        // Some scalars may not be wrapped in LIST (rare); accept SCALAR directly too.
        return kind == "SCALAR";
    }

    private static string? GetString(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object
           && el.TryGetProperty(name, out var p)
           && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool IsValidGraphIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }

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
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("Content", out var contentElement)
            || !contentElement.TryGetProperty("items", out var items))
        {
            return Array.Empty<ContentSearchHit>();
        }

        var seen = deduplicate ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
        var results = new List<ContentSearchHit>();
        foreach (var item in items.EnumerateArray())
        {
            var guidValue = item.GetProperty("ContentLink").GetProperty("GuidValue").GetString() ?? string.Empty;
            if (seen != null && !seen.Add(guidValue)) continue;

            var name = item.GetProperty("Name").GetString() ?? string.Empty;
            var language = item.GetProperty("Language").GetProperty("Name").GetString() ?? string.Empty;

            var contentType = ResolveDisplayContentType(item, allowList);

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

    private static string ResolveDisplayContentType(JsonElement item, IReadOnlyList<string> allowList)
    {
        if (!item.TryGetProperty("ContentType", out var types) || types.ValueKind != JsonValueKind.Array)
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
