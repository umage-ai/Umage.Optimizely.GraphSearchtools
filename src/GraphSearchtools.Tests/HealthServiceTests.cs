using System.Net;
using System.Text;
using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Tools.Health;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

public class HealthServiceTests
{
    [Fact]
    public async Task CheckAsync_WithFullCredentials_RunsAllFourProbesAndCapturesPerProbeLatency()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(req =>
        {
            requests.Add(Clone(req));
            // Gateway probe (GET on root) → 401 still proves reachable.
            if (req.RequestUri!.AbsolutePath == "/")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            // Admin probe → 200 (Basic accepted).
            if (req.RequestUri.AbsolutePath.Contains("api/pinned/collections"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };

            // Both content-endpoint probes (Single key + Index population) hit /content/v2.
            // Inspect the body to tell them apart.
            var body = req.Content!.ReadAsStringAsync().Result;
            if (body.Contains("__schema"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":{\"__schema\":{\"queryType\":{\"name\":\"Query\"}}}}", Encoding.UTF8, "application/json")
                };
            }
            // Index population
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"Content\":{\"total\":42}}}", Encoding.UTF8, "application/json")
            };
        });
        var svc = new HealthService(new HttpClient(handler), Resolver(new GraphCredentials(
            "https://cg.optimizely.com", "app-key", "secret-key", "single-key")));

        var result = await svc.CheckAsync(CancellationToken.None);

        result.Probes.Should().HaveCount(4);
        result.Probes.Select(p => p.Name).Should().Equal(
            "Gateway", "Admin credentials", "Single key", "Index population");

        // All probes hit something so each carries a non-negative latency.
        result.Probes.Should().OnlyContain(p => p.ElapsedMs >= 0);
        result.ElapsedMs.Should().BeGreaterThan(0);

        result.Probes[0].Status.Should().Be(HealthStatus.Green);   // Gateway reachable
        result.Probes[1].Status.Should().Be(HealthStatus.Green);   // Admin OK
        result.Probes[2].Status.Should().Be(HealthStatus.Green);   // Single key OK
        result.Probes[3].Status.Should().Be(HealthStatus.Green);   // Index has 42
        result.Probes[3].Message.Should().Contain("42");

        // Two distinct probes hit /content/v2 — the SingleKey probe AND the index probe.
        // We split them on purpose so support can isolate "key invalid" from "index empty".
        var contentRequests = requests.Where(r => r.RequestUri!.AbsolutePath == "/content/v2").ToList();
        contentRequests.Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckAsync_WhenSingleKeyIsRejected_ReportsRedAndDoesNotMaskAsIndexFailure()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/")
                return new HttpResponseMessage(HttpStatusCode.OK);
            if (req.RequestUri.AbsolutePath.Contains("api/pinned/collections"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            // Both /content/v2 calls reject the SingleKey.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("nope") };
        });
        var svc = new HealthService(new HttpClient(handler), Resolver(new GraphCredentials(
            "https://cg.optimizely.com", "app-key", "secret-key", "bad-key")));

        var result = await svc.CheckAsync(CancellationToken.None);

        var single = result.Probes.Single(p => p.Name == "Single key");
        single.Status.Should().Be(HealthStatus.Red);
        single.Message.Should().Contain("Rejected");
        single.Message.Should().Contain("SingleKey");

        // Index probe is independent — it ran but also hit 401, so amber (HTTP 401, can't
        // tell index state). The point of splitting the probes is exactly that the user
        // sees the SingleKey as the root cause, not the index probe's secondary 401.
        var index = result.Probes.Single(p => p.Name == "Index population");
        index.Status.Should().Be(HealthStatus.Amber);
    }

    [Fact]
    public async Task CheckAsync_WhenSingleKeyMissing_SkipsBothQueryProbes()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/")
                return new HttpResponseMessage(HttpStatusCode.OK);
            if (req.RequestUri.AbsolutePath.Contains("api/pinned/collections"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            throw new InvalidOperationException("Should not have called the query endpoint without a SingleKey.");
        });
        var svc = new HealthService(new HttpClient(handler), Resolver(new GraphCredentials(
            "https://cg.optimizely.com", "app-key", "secret-key", "")));

        var result = await svc.CheckAsync(CancellationToken.None);

        var single = result.Probes.Single(p => p.Name == "Single key");
        single.Status.Should().Be(HealthStatus.Red);
        single.Message.Should().Contain("not configured");

        var index = result.Probes.Single(p => p.Name == "Index population");
        index.Status.Should().Be(HealthStatus.Unknown);
    }

    [Fact]
    public async Task CheckAsync_WhenGatewayUnreachable_ShortCircuitsOtherProbes()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        var svc = new HealthService(new HttpClient(handler), Resolver(new GraphCredentials(
            "https://cg.optimizely.com", "app-key", "secret-key", "single-key")));

        var result = await svc.CheckAsync(CancellationToken.None);

        result.Probes.Should().HaveCount(4);
        result.Probes[0].Name.Should().Be("Gateway");
        result.Probes[0].Status.Should().Be(HealthStatus.Red);
    }

    [Fact]
    public async Task CheckAsync_QueryProbesAppendSingleKeyToTheEndpointUrl()
    {
        // Regression for "Health: Single key reports 401" — the displayed Target
        // is the bare /content/v2, but the actual POST URL must carry ?auth=...
        var contentRequests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/content/v2") contentRequests.Add(req);
            if (req.RequestUri!.AbsolutePath == "/")
                return new HttpResponseMessage(HttpStatusCode.OK);
            if (req.RequestUri.AbsolutePath.Contains("api/pinned/collections"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"Content\":{\"total\":7}}}")
            };
        });
        var svc = new HealthService(new HttpClient(handler), Resolver(new GraphCredentials(
            "https://cg.optimizely.com", "app-key", "secret-key", "the-single-key")));

        var result = await svc.CheckAsync(CancellationToken.None);

        // Both the SingleKey and the Index population probes must hit the
        // authed URL — without the auth, Graph returns 401.
        contentRequests.Should().HaveCount(2);
        contentRequests.Should().OnlyContain(r =>
            r.RequestUri!.Query.Contains("auth=the-single-key", StringComparison.Ordinal));

        // The displayed Target (what the UI shows) stays clean — no key leak.
        var single = result.Probes.Single(p => p.Name == "Single key");
        single.Target.Should().Be("https://cg.optimizely.com/content/v2");
        single.Target.Should().NotContain("the-single-key");
    }

    [Fact]
    public async Task CheckAsync_AdminProbeUsesBasicAuthHeaderBuiltFromAppKeyAndSecret()
    {
        HttpRequestMessage? adminRequest = null;
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("api/pinned/collections"))
            {
                adminRequest = Clone(req);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":{}}") };
        });
        var svc = new HealthService(new HttpClient(handler), Resolver(new GraphCredentials(
            "https://cg.optimizely.com", "the-app", "the-secret", "single-key")));

        await svc.CheckAsync(CancellationToken.None);

        adminRequest.Should().NotBeNull();
        adminRequest!.Headers.Authorization!.Scheme.Should().Be("Basic");
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(adminRequest.Headers.Authorization.Parameter!));
        decoded.Should().Be("the-app:the-secret");
    }

    private static IGraphCredentialsResolver Resolver(GraphCredentials creds) => new StaticResolver(creds);

    private static HttpRequestMessage Clone(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);
        foreach (var h in req.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (req.Content != null) clone.Content = new StringContent(req.Content.ReadAsStringAsync().Result);
        return clone;
    }

    private sealed class StaticResolver : IGraphCredentialsResolver
    {
        private readonly GraphCredentials _creds;
        public StaticResolver(GraphCredentials creds) { _creds = creds; }
        public GraphCredentials Resolve() => _creds;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) { _handler = handler; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
