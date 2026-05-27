using System.Net;
using System.Text;
using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Pinned upstream contracts with Optimizely Graph. These two facts have
/// already caught one regression in the source addon; do not weaken them.
/// </summary>
public class GraphAdminClientTests
{
    [Fact]
    public async Task GetCollectionsAsync_UsesBasicAuthHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"title\":\"Test\",\"key\":\"test\",\"isActive\":true,\"id\":\"1\",\"createdAt\":\"2024-01-01\",\"updatedAt\":\"2024-01-02\"}]")
            };
        });
        var client = new GraphAdminClient(new HttpClient(handler), CredentialsResolver());

        var results = await client.GetCollectionsAsync(CancellationToken.None);

        results.Should().ContainSingle().Which.Key.Should().Be("test");
        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.ToString().Should().Be("https://cg.optimizely.com/api/pinned/collections");
        captured.Headers.Authorization.Should().NotBeNull();
        captured.Headers.Authorization!.Scheme.Should().Be("Basic");
    }

    [Fact]
    public async Task UpdateSynonymsAsync_SendsPlainTextBodyWithRoutingQueryParameters()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            captured = request;
            body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new GraphAdminClient(new HttpClient(handler), CredentialsResolver());

        var request = new SynonymsRequest
        {
            Content = "water => H2O",
            LanguageRouting = "en",
            SourceRouting = "default",
            Slot = "one"
        };

        await client.UpdateSynonymsAsync(request, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Put);
        captured.Content!.Headers.ContentType!.MediaType.Should().Be("text/plain");
        var query = captured.RequestUri!.Query;
        query.Should().Contain("language_routing=en");
        query.Should().Contain("source_routing=default");
        query.Should().Contain("synonym_slot=one");
        body.Should().Be("water => H2O");
    }

    [Fact]
    public async Task GetGraphLocalesAsync_IntrospectsLocalesEnumAndStripsSyntheticValues()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            captured = request;
            body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":{\"__type\":{\"enumValues\":[" +
                    "{\"name\":\"en\"},{\"name\":\"da\"},{\"name\":\"sv\"}," +
                    "{\"name\":\"ALL\"},{\"name\":\"neutralLanguage\"}]}}}",
                    Encoding.UTF8, "application/json")
            };
        });
        var client = new GraphAdminClient(new HttpClient(handler), QueryCredentialsResolver());

        var locales = await client.GetGraphLocalesAsync(CancellationToken.None);

        // Hit the GraphQL endpoint (POST), with the query string carrying the SingleKey.
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/content/v2");
        captured.RequestUri.Query.Should().Contain("auth=single-key");
        // System.Text.Json's default encoder writes `"` as the unicode escape `"`.
        body.Should().Contain("__type(name: \\u0022Locales\\u0022)");

        // Both "ALL" and "neutralLanguage" are dropped — the picker provides its own
        // wildcard option, and neutralLanguage isn't a real selectable branch.
        locales.Should().Equal("da", "en", "sv");
    }

    [Fact]
    public async Task GetItemsAsync_OmitsOffsetQueryWhenZero()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });
        var client = new GraphAdminClient(new HttpClient(handler), CredentialsResolver());

        await client.GetItemsAsync("my-collection", CancellationToken.None);

        captured!.RequestUri!.PathAndQuery.Should().Be("/api/pinned/collections/my-collection/items");
    }

    [Fact]
    public async Task GetItemsAsync_ForwardsOffsetQuery()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });
        var client = new GraphAdminClient(new HttpClient(handler), CredentialsResolver());

        await client.GetItemsAsync("my-collection", CancellationToken.None, 40);

        captured!.RequestUri!.PathAndQuery.Should().Be("/api/pinned/collections/my-collection/items?offset=40");
    }

    [Fact]
    public async Task GetGraphLocalesAsync_ReturnsEmptyWhenSchemaHasNoLocalesType()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":{\"__type\":null}}", Encoding.UTF8, "application/json")
        });
        var client = new GraphAdminClient(new HttpClient(handler), QueryCredentialsResolver());

        var locales = await client.GetGraphLocalesAsync(CancellationToken.None);

        locales.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveByGuidsAsync_NormalisesResponseKeyToCanonicalGuid()
    {
        // Graph's `_metadata.key` is the GUID in "N" form (no hyphens, lowercase).
        // Pinned items are stored in canonical "D" form, so the API must hand back
        // canonical GUIDs or the JS-side target-name lookup misses every row.
        const string responseBody = "{\"data\":{\"_Content\":{\"items\":[{\"_metadata\":{\"key\":\"fdac9c5f86b64223b4a6397fe72483f9\",\"displayName\":\"Alloy Plan\",\"locale\":\"en\",\"types\":[\"ProductPage\",\"_Page\"]}}]}}}";

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        var client = new GraphAdminClient(new HttpClient(handler), QueryCredentialsResolver());

        var hits = await client.ResolveByGuidsAsync(
            new[] { "fdac9c5f-86b6-4223-b4a6-397fe72483f9" },
            Array.Empty<string>(),
            CancellationToken.None);

        hits.Should().ContainSingle().Which.ContentGuid.Should().Be("fdac9c5f-86b6-4223-b4a6-397fe72483f9");
    }

    [Fact]
    public async Task ResolveByGuidsAsync_SendsHyphenlessKeysInQueryVariables()
    {
        // Optimizely Graph's _metadata.key filter only matches when the GUID is
        // in "N" form. Hyphenated inputs must be normalised before being placed
        // in $guids or every lookup returns zero hits.
        string? body = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"_Content\":{\"items\":[]}}}", Encoding.UTF8, "application/json")
            };
        });
        var client = new GraphAdminClient(new HttpClient(handler), QueryCredentialsResolver());

        await client.ResolveByGuidsAsync(
            new[] { "fdac9c5f-86b6-4223-b4a6-397fe72483f9" },
            Array.Empty<string>(),
            CancellationToken.None);

        body.Should().NotBeNull();
        body!.Should().Contain("fdac9c5f86b64223b4a6397fe72483f9");
        body.Should().NotContain("fdac9c5f-86b6-4223-b4a6-397fe72483f9");
    }

    private static IGraphCredentialsResolver CredentialsResolver()
        => new StaticCredentialsResolver(new GraphCredentials(
            "https://cg.optimizely.com",
            "app-key",
            "secret-key",
            string.Empty));

    private static IGraphCredentialsResolver QueryCredentialsResolver()
        => new StaticCredentialsResolver(new GraphCredentials(
            "https://cg.optimizely.com",
            "app-key",
            "secret-key",
            "single-key"));

    private sealed class StaticCredentialsResolver : IGraphCredentialsResolver
    {
        private readonly GraphCredentials _credentials;

        public StaticCredentialsResolver(GraphCredentials credentials)
        {
            _credentials = credentials;
        }

        public GraphCredentials Resolve() => _credentials;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _sync;
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>? _async;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _sync = handler;
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _async = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _async != null ? _async(request) : Task.FromResult(_sync!(request));
        }
    }
}
