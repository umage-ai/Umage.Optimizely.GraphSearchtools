using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;

/// <summary>
/// Fetches the featured-products surface that the <c>alloy-products</c>
/// search profile is wired to. Sends one ProductPage GraphQL query with the
/// profile's pinned collection applied — when no pins are curated the page
/// falls back to organic ProductPages so the section never appears empty.
///
/// Editors tune the order via the GraphSearchtools admin: Profiles → Product
/// cards → Pinned tab. They pin items under phrase <c>"featured"</c> against
/// the collection key resolved from the profile's pinned-key formula
/// (<c>alloy-products-{locale}</c>), e.g. <c>alloy-products-en</c>.
/// </summary>
public sealed class FeaturedProductsService
{
    private const string PinnedPhrase = "featured";

    private readonly HttpClient _http;
    private readonly IGraphCredentialsResolver _credentials;
    private readonly JsonSerializerOptions _serializerOptions;

    public FeaturedProductsService(HttpClient http, IGraphCredentialsResolver credentials)
    {
        _http = http;
        _credentials = credentials;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<IReadOnlyList<FeaturedProduct>> GetFeaturedAsync(string locale, int limit, CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured) return Array.Empty<FeaturedProduct>();

        var clamped = Math.Clamp(limit, 1, 12);
        locale = string.IsNullOrWhiteSpace(locale) ? "en" : locale.Trim().ToLowerInvariant();
        var collection = $"alloy-products-{locale}";
        var endpoint = $"{creds.GatewayAddress.TrimEnd('/')}/content/v2?auth={creds.SingleKey}";

        // Mirror the alloy-products profile's pinned-key formula. Phrase
        // "featured" matches what marketers will pin against in the admin UI;
        // unpinned items fall through in their natural ProductPage order.
        var queryDocument = $@"
{{
  ProductPage(
    locale: [{locale}]
    limit: {clamped}
    pinned: {{ phrase: ""{PinnedPhrase}"", collections: [""{collection}""] }}
  ) {{
    items {{
      Name
      TeaserText
      MetaDescription
      RelativePath
      UniqueSellingPoints
    }}
  }}
}}";

        try
        {
            var json = JsonSerializer.Serialize(new { query = queryDocument }, _serializerOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<FeaturedProduct>();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("ProductPage", out var pp) ||
                !pp.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<FeaturedProduct>();
            }

            var result = new List<FeaturedProduct>(items.GetArrayLength());
            foreach (var item in items.EnumerateArray())
            {
                var teaser = GetString(item, "TeaserText");
                if (string.IsNullOrWhiteSpace(teaser)) teaser = GetString(item, "MetaDescription");

                result.Add(new FeaturedProduct
                {
                    Name = GetString(item, "Name") ?? string.Empty,
                    Url = GetString(item, "RelativePath") ?? "#",
                    Teaser = teaser ?? string.Empty,
                    SellingPoints = GetStringArray(item, "UniqueSellingPoints")
                });
            }
            return result;
        }
        catch
        {
            // Featured products is decorative — never let a Graph hiccup break
            // the start page. Returning empty hides the surface entirely.
            return Array.Empty<FeaturedProduct>();
        }
    }

    private static string? GetString(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object
           && el.TryGetProperty(name, out var p)
           && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>(p.GetArrayLength());
        foreach (var entry in p.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                var s = entry.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
            }
        }
        return list;
    }
}

public sealed class FeaturedProduct
{
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = "#";
    public string Teaser { get; init; } = string.Empty;
    public IReadOnlyList<string> SellingPoints { get; init; } = Array.Empty<string>();
}
