using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

public class GraphCredentialsResolverTests
{
    [Fact]
    public void Resolve_FallsBackToOptimizelyContentGraphSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Optimizely:ContentGraph:GatewayAddress"] = "https://host.example/",
                ["Optimizely:ContentGraph:AppKey"] = "host-app",
                ["Optimizely:ContentGraph:Secret"] = "host-secret",
                ["Optimizely:ContentGraph:SingleKey"] = "host-single"
            })
            .Build();
        var options = Options.Create(new GraphSearchtoolsOptions());
        var resolver = new GraphCredentialsResolver(configuration, options);

        var creds = resolver.Resolve();

        creds.GatewayAddress.Should().Be("https://host.example/");
        creds.AppKey.Should().Be("host-app");
        creds.Secret.Should().Be("host-secret");
        creds.SingleKey.Should().Be("host-single");
    }

    [Fact]
    public void Resolve_PrefersExplicitOverridePerField()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Optimizely:ContentGraph:GatewayAddress"] = "https://host.example/",
                ["Optimizely:ContentGraph:AppKey"] = "host-app",
                ["Optimizely:ContentGraph:Secret"] = "host-secret"
            })
            .Build();
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Graph = new GraphConnectionOptions
            {
                AppKey = "override-app",
                SingleKey = "override-single"
            }
        });
        var resolver = new GraphCredentialsResolver(configuration, options);

        var creds = resolver.Resolve();

        creds.GatewayAddress.Should().Be("https://host.example/"); // not overridden -> falls back
        creds.AppKey.Should().Be("override-app");                  // overridden
        creds.Secret.Should().Be("host-secret");                   // not overridden -> falls back
        creds.SingleKey.Should().Be("override-single");            // overridden
    }
}
