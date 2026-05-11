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
using UmageAI.Optimizely.GraphSearchTools.Tools.ContentSearchabilityAudit;
using UmageAI.Optimizely.GraphSearchTools.Tools.Health;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;
using UmageAI.Optimizely.GraphSearchTools.Tools.PinnedCoverage;
using UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;
using UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner;
using UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage;
using UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;
using UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;
using UmageAI.Optimizely.GraphSearchTools.Tools.Webhooks;

namespace UmageAI.Optimizely.GraphSearchTools.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Graph Search Tools with default options.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="IGraphSearchtoolsBuilder"/> so callers can chain
    /// <c>.AddSearchProfile(...)</c>. This is a breaking change from earlier
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
        services.AddHttpClient<HealthService>();
        services.AddScoped<HealthScanService>();
        services.AddHttpClient<QueryRunnerService>();
        services.AddScoped<WebhooksService>();

        // Phase 3: Semantic Weight Tuner — DDS-backed policy editor. Singleton
        // because the underlying DynamicDataStoreFactory is process-global and
        // the service is otherwise stateless.
        services.AddSingleton<SemanticTunerService>();

        // Phase 2.5: Search Profiles foundation. The registry collects every
        // SearchProfile registered as a singleton (by AddSearchProfile) plus
        // synthesises a Generic catchment.
        services.AddSingleton<ISearchProfileRegistry, SearchProfileRegistry>();
        services.AddSingleton<SearchProfileEditService>();
        services.AddScoped<UmageAI.Optimizely.GraphSearchTools.Tools.Profiles.ProfilesService>();

        // Phase 4 foundation: search-log DDS table + ingest endpoint. Phase 4
        // Wave 5 tools (Search Logs UI, Pinned Result Coverage, Synonym
        // Coverage) read from this service; TelemetryApiController writes.
        services.AddSingleton<SearchLogService>();

        // Phase 4 Wave 5: Search Logs UI — top phrases, zero-result phrases,
        // low-CTR phrases, raw events. Thin wrapper around SearchLogService
        // that defaults the time window and clamps `take`.
        services.AddScoped<SearchLogsService>();

        // Phase 4 Wave 5: Synonym Coverage — joins SynonymsService blobs with
        // SearchLogService phrase aggregates. Read-only analyzer; the single
        // GET endpoint serves a SynonymCoverageResult for the page to render.
        services.AddScoped<SynonymCoverageService>();

        // Phase 4 Wave 5 §6: Pinned Result Coverage audit. Read-only — joins
        // Graph pinned data, IContentLoader content state, ISearchProfileRegistry
        // (collection → profile mapping) and SearchLogService 7-day window.
        services.AddScoped<PinnedCoverageService>();

        // Phase 4 Wave 5 §6: Content Searchability Audit. On-demand local CMS
        // scan that walks every published page under every site root and flags
        // empty Name / MainBody / Tags plus oversize sortable string fields.
        // Scoped because it depends on scoped IContentLoader / IContentTypeRepository.
        services.AddScoped<ContentSearchabilityAuditService>();

        // Phase 5: Relevancy Lab — DDS-backed golden set CRUD + run history,
        // NDCG@10/MRR scoring, two-config comparison, CSV export. Singleton
        // because DynamicDataStoreFactory is process-global; QueryRunnerService
        // is HttpClient-bound (transient) so the run engine resolves it through
        // the scope factory at run-time rather than as a captive dependency.
        services.AddSingleton<RelevancyLabService>(sp =>
            new RelevancyLabService(sp.GetRequiredService<IServiceScopeFactory>()));

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
    /// This affects only the new aggregate-first telemetry pipeline introduced
    /// by the v0.5 design. The legacy <c>SearchLogService</c> ingest path
    /// (Phase 4 foundation) continues to write to its own DDS table independent
    /// of this switch — coexistence is intentional during the dual-running
    /// migration window described in <c>docs/search-telemetry-design.md</c> §8.
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

        options.AddPolicy("codeart:graphsearchtools", policy => policy.RequireRole(roles));
    }
}
