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
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;
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

        // Phase 2: Saved Queries runner (the user-facing preset CRUD surface
        // was dropped in favour of Graph's own GraphiQL; the runner stays
        // because the Pinned tab's A/B preview hits it).
        services.Should().Contain(d => d.ServiceType == typeof(QueryRunnerService));

        // Phase 2.5: Search Channels registry + audit-log service.
        services.Should().Contain(d => d.ServiceType == typeof(ISearchChannelRegistry));
        services.Should().Contain(d => d.ServiceType == typeof(AuditLogService));

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
        var policy = authOptions.Value.GetPolicy("umageai:graphsearchtools");
        policy.Should().NotBeNull();

        // Module is registered with the correct name (drives the layout virtual-path remap).
        var moduleOptions = provider.GetRequiredService<IOptions<ProtectedModuleOptions>>();
        moduleOptions.Value.Items.Should().ContainSingle(m => m.Name == "GraphSearchtools");
    }

    [Fact]
    public void AddGraphSearchtools_BindsConfigurationFromUmageAISection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UmageAI:GraphSearchTools:CheckPermissionForEachFeature"] = "true",
                ["UmageAI:GraphSearchTools:SearchableContentTypes:0"] = "_Content",
                ["UmageAI:GraphSearchTools:SearchableContentTypes:1"] = "ArticlePage",
                ["UmageAI:GraphSearchTools:Graph:GatewayAddress"] = "https://example.com/graph",
                ["UmageAI:GraphSearchTools:Graph:AppKey"] = "key1",
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

    [Fact]
    public void AddGraphSearchtools_IgnoresLegacyCodeArtSection()
    {
        // Hard cut: the pre-1.0 rename from CodeArt:GraphSearchtools to
        // UmageAI:GraphSearchTools does not retain a fallback. Hosts that
        // upgrade without renaming their config section get defaults.
        // Pick a value the legacy section *opposes* the default for, so
        // the test proves "ignored" instead of an incidental match.
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodeArt:GraphSearchtools:CheckPermissionForEachFeature"] = "false",
                ["CodeArt:GraphSearchtools:Graph:GatewayAddress"] = "https://example.com/graph",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        services.AddGraphSearchtools();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphSearchtoolsOptions>>();

        // Default is true; legacy "false" should not override it.
        options.Value.CheckPermissionForEachFeature.Should().BeTrue();
        options.Value.Graph.Should().BeNull();
    }

    [Fact]
    public void AddGraphSearchtools_BindsSavedQueriesDefaultQueryAndVariables()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UmageAI:GraphSearchTools:SavedQueries:DefaultQuery"] = "query Q($q:String){ Content(where:{_fulltext:{match:$q}}){ items { Name } total } }",
                ["UmageAI:GraphSearchTools:SavedQueries:DefaultQueryVariables:productNodeType"] = "ProductNode",
                ["UmageAI:GraphSearchTools:SavedQueries:DefaultQueryVariables:contentType"] = "Content"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        services.AddGraphSearchtools();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphSearchtoolsOptions>>();

        options.Value.SavedQueries.Should().NotBeNull();
        options.Value.SavedQueries.DefaultQuery.Should().Contain("query Q($q:String)");
        options.Value.SavedQueries.DefaultQueryVariables.Should().ContainKey("productNodeType")
            .WhoseValue!.ToString().Should().Be("ProductNode");
        options.Value.SavedQueries.DefaultQueryVariables.Should().ContainKey("contentType")
            .WhoseValue!.ToString().Should().Be("Content");
    }

    [Fact]
    public void AddSearchChannel_RegistersChannelWithBuilderAndServiceCollection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        var builder = services.AddGraphSearchtools()
            .AddSearchChannel("site-search", p => p
                .DisplayName("Site search")
                .Sites("corporate")
                .Locales("en")
                .UsesPinnedKey("site-{locale}"));

        builder.Channels.Should().ContainSingle().Which.Key.Should().Be("site-search");

        // The channel is also a DI singleton so the registry can find it via IEnumerable<SearchChannel>.
        var registered = services
            .Where(d => d.ServiceType == typeof(SearchChannel))
            .Select(d => d.ImplementationInstance)
            .OfType<SearchChannel>()
            .ToList();
        registered.Should().ContainSingle().Which.Key.Should().Be("site-search");
    }

    [Fact]
    public void AddSearchChannel_GraphQLDocumentInline_RoundtripsContent()
    {
        // The "single source of truth" wiring: hosts pass the same query string
        // their runtime executes via GraphQLDocumentInline so the admin Channel
        // detail view renders what production sends to Graph — no static .graphql
        // stub to drift from the live code.
        const string queryDoc = "{ Content(where: { _and: [{ ContentType: { eq: \"Page\" } }] } limit: 20) { items { Name } } }";

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        services.AddGraphSearchtools()
            .AddSearchChannel("site-search", p => p
                .DisplayName("Site search")
                .GraphQLDocumentInline(queryDoc));

        var channel = services
            .Where(d => d.ServiceType == typeof(SearchChannel))
            .Select(d => d.ImplementationInstance)
            .OfType<SearchChannel>()
            .Single();

        channel.GraphQLDocumentContent.Should().Be(queryDoc);
        channel.GraphQLDocumentPath.Should().BeNull();
    }

    [Fact]
    public void AddSearchChannel_ThrowsOnDuplicateKey()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        var act = () => services.AddGraphSearchtools()
            .AddSearchChannel("dup", p => p.DisplayName("first"))
            .AddSearchChannel("dup", p => p.DisplayName("second"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*dup*");
    }
}
