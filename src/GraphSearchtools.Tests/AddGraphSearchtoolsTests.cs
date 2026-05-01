using EPiServer.Shell.Modules;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Helpers;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Localization;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;
using UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

public class AddGraphSearchtoolsTests
{
    [Fact]
    public void AddGraphSearchtools_RegistersExpectedServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        services.AddGraphSearchtools(o =>
        {
            o.AuthorizedRoles = ["WebAdmins", "Administrators"];
        });

        // Service descriptors are registered (we don't resolve UiStringsProvider /
        // FeatureAccessChecker because they depend on EPiServer runtime services
        // that aren't available in a plain unit test).
        services.Should().Contain(d => d.ServiceType == typeof(UserPreferencesService));
        services.Should().Contain(d => d.ServiceType == typeof(UiStringsProvider));
        services.Should().Contain(d => d.ServiceType == typeof(FeatureAccessChecker));

        // Phase 1: Graph admin client + tool services + helpers.
        services.Should().Contain(d => d.ServiceType == typeof(IGraphAdminClient));
        services.Should().Contain(d => d.ServiceType == typeof(IGraphCredentialsResolver));
        services.Should().Contain(d => d.ServiceType == typeof(LanguageSiteEnumerator));
        services.Should().Contain(d => d.ServiceType == typeof(PinnedService));
        services.Should().Contain(d => d.ServiceType == typeof(SynonymsService));

        var provider = services.BuildServiceProvider();

        // Options bind correctly with the supplied configure-action overrides.
        var options = provider.GetRequiredService<IOptions<GraphSearchtoolsOptions>>();
        options.Value.AuthorizedRoles.Should().Contain("WebAdmins");
        options.Value.SearchableContentTypes.Should().Contain("_Page");
        options.Value.Features.Overview.Should().BeTrue();
        options.Value.Features.Pinned.Should().BeTrue();
        options.Value.Features.Synonyms.Should().BeTrue();

        // Auth policy is configured under the canonical name.
        var authOptions = provider.GetRequiredService<IOptions<AuthorizationOptions>>();
        var policy = authOptions.Value.GetPolicy("codeart:graphsearchtools");
        policy.Should().NotBeNull();

        // Module is registered with the correct name (drives the layout virtual-path remap).
        var moduleOptions = provider.GetRequiredService<IOptions<ProtectedModuleOptions>>();
        moduleOptions.Value.Items.Should().ContainSingle(m => m.Name == "GraphSearchtools");
    }

    [Fact]
    public void AddGraphSearchtools_BindsConfigurationFromCodeArtSection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodeArt:GraphSearchtools:CheckPermissionForEachFeature"] = "true",
                ["CodeArt:GraphSearchtools:SearchableContentTypes:0"] = "_Content",
                ["CodeArt:GraphSearchtools:SearchableContentTypes:1"] = "ArticlePage",
                ["CodeArt:GraphSearchtools:Graph:GatewayAddress"] = "https://example.com/graph",
                ["CodeArt:GraphSearchtools:Graph:AppKey"] = "key1",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        services.AddGraphSearchtools();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphSearchtoolsOptions>>();

        options.Value.CheckPermissionForEachFeature.Should().BeTrue();
        options.Value.SearchableContentTypes.Should().Contain("_Content").And.Contain("ArticlePage");
        options.Value.Graph.Should().NotBeNull();
        options.Value.Graph!.GatewayAddress.Should().Be("https://example.com/graph");
        options.Value.Graph.AppKey.Should().Be("key1");
    }
}
