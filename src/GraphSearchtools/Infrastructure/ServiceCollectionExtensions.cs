using EPiServer.Shell.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Helpers;
using UmageAI.Optimizely.GraphSearchTools.Localization;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Connectivity;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchConsole;
using UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;

namespace UmageAI.Optimizely.GraphSearchTools.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Graph Search Tools with default options.
    /// </summary>
    public static IServiceCollection AddGraphSearchtools(this IServiceCollection services)
    {
        return services.AddGraphSearchtools(_ => { });
    }

    /// <summary>
    /// Adds Graph Search Tools with custom options.
    /// </summary>
    public static IServiceCollection AddGraphSearchtools(
        this IServiceCollection services,
        Action<GraphSearchtoolsOptions> configureOptions)
    {
        services.AddOptions<GraphSearchtoolsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.GetSection("CodeArt:GraphSearchtools").Bind(options);
            })
            .Configure(configureOptions);

        services.AddSingleton<IPostConfigureOptions<AuthorizationOptions>, ConfigureGraphSearchtoolsPolicy>();

        services.AddSingleton<FeatureAccessChecker>();
        services.AddSingleton<UserPreferencesService>();

        services.AddScoped<UiStringsProvider>();

        services.AddScoped<IGraphCredentialsResolver, GraphCredentialsResolver>();
        services.AddHttpClient<IGraphAdminClient, GraphAdminClient>();
        services.AddScoped<LanguageSiteEnumerator>();
        services.AddScoped<PinnedService>();
        services.AddScoped<SynonymsService>();
        services.AddHttpClient<ConnectivityService>();
        services.AddHttpClient<SearchConsoleService>();
        services.AddSingleton<SavedQueriesService>();

        services.Configure<ProtectedModuleOptions>(options =>
        {
            options.Items.Add(new ModuleDetails
            {
                Name = "GraphSearchtools"
            });
        });

        return services;
    }
}

/// <summary>
/// Configures the authorization policy using roles from GraphSearchtoolsOptions.
/// Runs after all option configurations have been applied.
/// </summary>
internal class ConfigureGraphSearchtoolsPolicy : IPostConfigureOptions<AuthorizationOptions>
{
    private readonly IOptions<GraphSearchtoolsOptions> _options;

    public ConfigureGraphSearchtoolsPolicy(IOptions<GraphSearchtoolsOptions> options)
    {
        _options = options;
    }

    public void PostConfigure(string? name, AuthorizationOptions options)
    {
        var roles = _options.Value.AuthorizedRoles;
        if (roles == null || roles.Length == 0)
            roles = ["WebAdmins", "Administrators"];

        options.AddPolicy("codeart:graphsearchtools", policy => policy.RequireRole(roles));
    }
}
