using System.Net;
using System.Text;
using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchConsole;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

public class SearchConsoleServiceTests
{
    [Fact]
    public async Task RunAsync_BuildsOrderByWithRankingSemanticWeightAndMinimumScore()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async req =>
        {
            capturedBody = req.Content == null ? null : await req.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"Content\":{\"total\":0,\"items\":[]}}}", Encoding.UTF8, "application/json")
            };
        });
        var service = new SearchConsoleService(new HttpClient(handler), Credentials());

        var result = await service.RunAsync(new SearchRequest
        {
            Query = "lamps",
            Ranking = "SEMANTIC",
            SemanticWeight = 0.4,
            MinimumScore = 1.5,
            Limit = 10
        }, CancellationToken.None);

        capturedBody.Should().NotBeNull();
        result.GraphQuery.Should().Contain("_ranking: SEMANTIC");
        result.GraphQuery.Should().Contain("_semanticWeight: 0.4");
        result.GraphQuery.Should().Contain("_minimumScore: 1.5");
        // The variables block is what carries the literal phrase; that path
        // is exercised here too.
        capturedBody.Should().Contain("\"q\":\"lamps\"");
    }

    [Fact]
    public async Task RunAsync_OmitsMinimumScoreWhenNotProvided()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":{\"Content\":{\"total\":0,\"items\":[]}}}", Encoding.UTF8, "application/json")
        });
        var service = new SearchConsoleService(new HttpClient(handler), Credentials());

        var result = await service.RunAsync(new SearchRequest
        {
            Query = "x",
            Ranking = "RELEVANCE",
            SemanticWeight = 0.2,
            MinimumScore = null
        }, CancellationToken.None);

        result.GraphQuery.Should().Contain("_ranking: RELEVANCE");
        result.GraphQuery.Should().NotContain("_minimumScore");
    }

    [Fact]
    public async Task RunAsync_RejectsUnknownRankingByFallingBackToRelevance()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":{\"Content\":{\"total\":0,\"items\":[]}}}", Encoding.UTF8, "application/json")
        });
        var service = new SearchConsoleService(new HttpClient(handler), Credentials());

        var result = await service.RunAsync(new SearchRequest
        {
            Query = "x",
            Ranking = "INVALID_VALUE"
        }, CancellationToken.None);

        result.GraphQuery.Should().Contain("_ranking: RELEVANCE");
    }

    private static IGraphCredentialsResolver Credentials() => new StaticCreds(new GraphCredentials(
        "https://cg.optimizely.com", "app-key", "secret-key", "single-key"));

    private sealed class StaticCreds : IGraphCredentialsResolver
    {
        private readonly GraphCredentials _credentials;
        public StaticCreds(GraphCredentials credentials) { _credentials = credentials; }
        public GraphCredentials Resolve() => _credentials;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _sync;
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>? _async;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) { _sync = handler; }
        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) { _async = handler; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _async != null ? _async(request) : Task.FromResult(_sync!(request));
    }
}
