using EPiServer.Shell.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Helpers;
using UmageAI.Optimizely.GraphSearchTools.Localization;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;
using UmageAI.Optimizely.GraphSearchTools.Tools.PinnedCoverage;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;
using UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage;
using UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;
using UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

namespace UmageAI.Optimizely.GraphSearchTools.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Graph Search Tools with default options.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="IGraphSearchtoolsBuilder"/> so callers can chain
    /// <c>.AddSearchChannel(...)</c>. This is a breaking change from earlier
    /// previews — code that needs the underlying <see cref="IServiceCollection"/>
    /// can read it from <see cref="IGraphSearchtoolsBuilder.Services"/>.
    /// </remarks>
    public static IGraphSearchtoolsBuilder AddGraphSearchtools(this IServiceCollection services)
    {
        return services.AddGraphSearchtools(_ => { });
    }

    /// <summary>
    /// Adds Graph Search Tools with custom options.
    /// </summary>
    /// <remarks>
    /// See remarks on <see cref="AddGraphSearchtools(IServiceCollection)"/>.
    /// </remarks>
    public static IGraphSearchtoolsBuilder AddGraphSearchtools(
        this IServiceCollection services,
        Action<GraphSearchtoolsOptions> configureOptions)
    {
        services.AddOptions<GraphSearchtoolsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.GetSection("UmageAI:GraphSearchTools").Bind(options);
            })
            .Configure(configureOptions);

        services.AddSingleton<IPostConfigureOptions<AuthorizationOptions>, ConfigureGraphSearchtoolsPolicy>();

        services.AddSingleton<FeatureAccessChecker>();
        services.AddSingleton<UserPreferencesService>();

        services.AddScoped<UiStringsProvider>();

        services.AddScoped<IGraphCredentialsResolver, GraphCredentialsResolver>();
        services.AddHttpClient<IGraphAdminClient, GraphAdminClient>();
        services.AddScoped<LanguageSiteEnumerator>();
        services.AddScoped<UmageAI.Optimizely.GraphSearchTools.Configuration.CmsLocaleResolver>();
        services.AddScoped<PinnedService>();
        services.AddScoped<SynonymsService>();
        services.AddHttpClient<QueryRunnerService>();

        // Phase 2.5: Search Channels foundation. The registry collects every
        // SearchChannel registered as a singleton (by AddSearchChannel).
        services.AddSingleton<ISearchChannelRegistry, SearchChannelRegistry>();
        services.AddSingleton<AuditLogService>();
        services.AddScoped<UmageAI.Optimizely.GraphSearchTools.Tools.Channels.ChannelsService>();

        // Phase 4 Wave 5: Search Logs UI — top phrases, zero-result phrases,
        // low-CTR phrases, raw events. Thin wrapper around ITelemetryReader
        // that defaults the time window and clamps `take`.
        services.AddScoped<SearchLogsService>();

        // Phase 4 Wave 5: Synonym Coverage — joins SynonymsService blobs with
        // ITelemetryReader phrase aggregates. Read-only analyzer; the single
        // GET endpoint serves a SynonymCoverageResult for the page to render.
        services.AddScoped<SynonymCoverageService>();

        // Phase 4 Wave 5 §6: Pinned Result Coverage audit. Read-only — joins
        // Graph pinned data, IContentLoader content state, ISearchChannelRegistry
        // (collection → channel mapping) and ITelemetryReader 7-day window.
        services.AddScoped<PinnedCoverageService>();

        // Aurora refactor: Insights dashboard. Pulls from the three services
        // above — no new datastore. Scoped because it composes scoped deps.
        services.AddScoped<UmageAI.Optimizely.GraphSearchTools.Tools.Insights.InsightsService>();

        // Telemetry: local sink + bucket flusher + reader on by default. To
        // route telemetry through a 3rd-party backend instead (App Insights,
        // Mixpanel, Matomo …), call UseExternalTelemetryReader<T>() after
        // AddGraphSearchtools — it removes the local sink + flusher and zero
        // DDS rows are written from this addon.
        services.AddSingleton<TelemetryAbuseGuard>();
        services.AddSingleton<LocalTelemetrySink>();
        services.AddSingleton<ITelemetrySink>(sp => sp.GetRequiredService<LocalTelemetrySink>());
        services.AddSingleton<ITelemetryMetrics>(sp => sp.GetRequiredService<LocalTelemetrySink>());
        services.AddSingleton<ITelemetryReader, LocalTelemetryReader>();
        services.AddHostedService<BucketFlusher>();

        services.Configure<ProtectedModuleOptions>(options =>
        {
            options.Items.Add(new ModuleDetails
            {
                Name = "GraphSearchtools"
            });
        });

        return new GraphSearchtoolsBuilder(services);
    }

    /// <summary>
    /// Replaces the default local telemetry pipeline with a customer-supplied
    /// reader (e.g. an App Insights / Mixpanel / Matomo adapter). Removes the
    /// local sink and bucket flusher so the addon writes zero DDS rows for
    /// telemetry; the public ingest endpoint then returns 410 Gone, and the
    /// client SDK disables itself after one such response.
    /// </summary>
    /// <remarks>
    /// Only one telemetry pipeline is wired by default; this swaps it for an
    /// adapter that talks to whatever backend the customer already runs. Hosts
    /// that share an instance with multiple tenants can use this to centralise
    /// telemetry storage instead of accumulating DDS rows per node.
    /// </remarks>
    public static IGraphSearchtoolsBuilder UseExternalTelemetryReader<TReader>(this IGraphSearchtoolsBuilder builder)
        where TReader : class, ITelemetryReader
    {
        var services = builder.Services;
        services.RemoveAll<ITelemetrySink>();
        services.RemoveAll<ITelemetryMetrics>();
        services.RemoveAll<LocalTelemetrySink>();
        services.RemoveAll<ITelemetryReader>();

        for (var i = services.Count - 1; i >= 0; i--)
        {
            var d = services[i];
            if (d.ImplementationType == typeof(BucketFlusher) ||
                (d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(BucketFlusher)))
            {
                services.RemoveAt(i);
            }
        }

        services.AddSingleton<ITelemetryReader, TReader>();
        return builder;
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

        options.AddPolicy("umageai:graphsearchtools", policy => policy.RequireRole(roles));
    }
}
