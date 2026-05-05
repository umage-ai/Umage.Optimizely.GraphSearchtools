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
using UmageAI.Optimizely.GraphSearchTools.Tools.Health;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;
using UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner;
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

        // Phase 2: Health + the Saved Queries runner (the user-facing preset
        // CRUD surface was dropped in favour of Graph's own GraphiQL; the
        // runner stays because the Pinned tab's A/B preview hits it).
        services.Should().Contain(d => d.ServiceType == typeof(HealthService));
        services.Should().Contain(d => d.ServiceType == typeof(QueryRunnerService));

        // Phase 2.5: Search Profiles registry + audit-log service.
        services.Should().Contain(d => d.ServiceType == typeof(ISearchProfileRegistry));
        services.Should().Contain(d => d.ServiceType == typeof(SearchProfileEditService));

        var provider = services.BuildServiceProvider();

        // Options bind correctly with the supplied configure-action overrides.
        var options = provider.GetRequiredService<IOptions<GraphSearchtoolsOptions>>();
        options.Value.AuthorizedRoles.Should().Contain("WebAdmins");
        options.Value.SearchableContentTypes.Should().Contain("_Page");
        options.Value.Features.Overview.Should().BeTrue();
        options.Value.Features.Pinned.Should().BeTrue();
        options.Value.Features.Synonyms.Should().BeTrue();
        options.Value.Features.Health.Should().BeTrue();
        options.Value.Features.Autocomplete.Should().BeTrue();
        options.Value.Features.DecaySandbox.Should().BeTrue();

        // Phase 3: Decay & Factor Sandbox is a client-side preview tool — no
        // service registration, but its PermissionType must still resolve so
        // it shows up in CMS Set Access Rights.
        GraphSearchtoolsPermissions.DecaySandbox.Should().NotBeNull();
        GraphSearchtoolsPermissions.DecaySandbox.Name.Should().Be("DecaySandbox");

        // Phase 3: Semantic Weight Tuner — DDS-backed policy editor.
        services.Should().Contain(d => d.ServiceType == typeof(SemanticTunerService));
        GraphSearchtoolsPermissions.SemanticTuner.Should().NotBeNull();
        GraphSearchtoolsPermissions.SemanticTuner.Name.Should().Be("SemanticTuner");
        options.Value.Features.SemanticTuner.Should().BeTrue();

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

    [Fact]
    public void AddGraphSearchtools_BindsSavedQueriesDefaultQueryAndVariables()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodeArt:GraphSearchtools:SavedQueries:DefaultQuery"] = "query Q($q:String){ Content(where:{_fulltext:{match:$q}}){ items { Name } total } }",
                ["CodeArt:GraphSearchtools:SavedQueries:DefaultQueryVariables:productNodeType"] = "ProductNode",
                ["CodeArt:GraphSearchtools:SavedQueries:DefaultQueryVariables:contentType"] = "Content"
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
    public void AddSearchProfile_RegistersProfileWithBuilderAndServiceCollection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        var builder = services.AddGraphSearchtools()
            .AddSearchProfile("site-search", p => p
                .DisplayName("Site search")
                .Sites("corporate")
                .Locales("en")
                .UsesPinnedKey("site-{locale}"));

        builder.Profiles.Should().ContainSingle().Which.Key.Should().Be("site-search");

        // The profile is also a DI singleton so the registry can find it via IEnumerable<SearchProfile>.
        var registered = services
            .Where(d => d.ServiceType == typeof(SearchProfile))
            .Select(d => d.ImplementationInstance)
            .OfType<SearchProfile>()
            .ToList();
        registered.Should().ContainSingle().Which.Key.Should().Be("site-search");
    }

    [Fact]
    public void AddSearchProfile_ThrowsOnDuplicateKey()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.AddAuthorizationCore();

        var act = () => services.AddGraphSearchtools()
            .AddSearchProfile("dup", p => p.DisplayName("first"))
            .AddSearchProfile("dup", p => p.DisplayName("second"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*dup*");
    }
}
