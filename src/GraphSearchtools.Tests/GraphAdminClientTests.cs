using System.Net;
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

        results.Should().ContainSingle().Which.Title.Should().Be("Test");
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

    private static IGraphCredentialsResolver CredentialsResolver()
        => new StaticCredentialsResolver(new GraphCredentials(
            "https://cg.optimizely.com",
            "app-key",
            "secret-key",
            string.Empty));

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
