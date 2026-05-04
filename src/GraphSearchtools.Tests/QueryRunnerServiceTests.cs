using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

public class QueryRunnerServiceTests
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
        var service = NewService(handler);

        var result = await service.RunAsync(new RunnerRequest
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
        var service = NewService(handler);

        var result = await service.RunAsync(new RunnerRequest
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
        var service = NewService(handler);

        var result = await service.RunAsync(new RunnerRequest
        {
            Query = "x",
            Ranking = "INVALID_VALUE"
        }, CancellationToken.None);

        result.GraphQuery.Should().Contain("_ranking: RELEVANCE");
    }

    [Fact]
    public async Task RunAsync_WithDefaultQuery_SendsItVerbatimAndIgnoresKnobs()
    {
        const string customQuery = @"
query SiteSearch($query: String, $limit: Int, $locale: [Locales!], $today: Date, $kind: String) {
  ISearchableContent(
    where: { _fulltext: { match: $query }, ContentType: { eq: $kind } }
    locale: $locale
    limit: $limit
  ) {
    items { Name ContentType ContentLink { Id GuidValue } _score GetExcerpt }
    total(all: true)
  }
}";
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async req =>
        {
            capturedBody = req.Content == null ? null : await req.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":{\"ISearchableContent\":{\"total\":3,\"items\":[" +
                    "{\"Name\":\"Course A\",\"ContentType\":[\"Page\",\"CoursePage\"]," +
                    "\"ContentLink\":{\"Id\":42,\"GuidValue\":\"a-guid\"}," +
                    "\"_score\":0.91,\"GetExcerpt\":\"snippet from custom query\"}" +
                    "]}}}",
                    Encoding.UTF8, "application/json")
            };
        });

        var options = Options.Create(new GraphSearchtoolsOptions
        {
            SavedQueries = new SavedQueriesOptions
            {
                DefaultQuery = customQuery,
                DefaultQueryVariables = new Dictionary<string, object?> { ["kind"] = "Course" }
            }
        });
        var service = new QueryRunnerService(new HttpClient(handler), Credentials(), options);

        service.IsDefaultQueryActive.Should().BeTrue();
        var result = await service.RunAsync(new RunnerRequest
        {
            Query = "powershell",
            Locale = "en",
            Ranking = "SEMANTIC",
            SemanticWeight = 0.9,
            MinimumScore = 5.0,
            Limit = 7
        }, CancellationToken.None);

        // Custom query is sent verbatim — knobs do not appear in the GraphQL document.
        result.GraphQuery.Should().Be(customQuery);
        result.GraphQuery.Should().NotContain("_ranking");
        result.GraphQuery.Should().NotContain("_semanticWeight");
        result.GraphQuery.Should().NotContain("_minimumScore");

        // Variables: q + query alias, limit, locale, today, plus the configured "kind".
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("\"query\":\"powershell\"");
        capturedBody.Should().Contain("\"q\":\"powershell\"");
        capturedBody.Should().Contain("\"limit\":7");
        capturedBody.Should().Contain("\"locale\":[\"en\"]");
        capturedBody.Should().Contain("\"kind\":\"Course\"");
        capturedBody.Should().Contain("\"today\":");

        // Generic parser walks data → first object with `items`.
        result.TotalCount.Should().Be(3);
        result.Hits.Should().HaveCount(1);
        var hit = result.Hits[0];
        hit.Name.Should().Be("Course A");
        hit.ContentType.Should().Be("Page");
        hit.ContentId.Should().Be(42);
        hit.ContentGuid.Should().Be("a-guid");
        hit.Score.Should().Be(0.91);
        hit.FullTextSnippet.Should().Be("snippet from custom query");
    }

    [Fact]
    public void IsDefaultQueryActive_ReturnsFalseWhenUnset()
    {
        var service = NewService(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        service.IsDefaultQueryActive.Should().BeFalse();
    }

    private static QueryRunnerService NewService(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Credentials(), Options.Create(new GraphSearchtoolsOptions()));

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
