using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Boot-time validator that runs <see cref="StartupDiagnostics.Evaluate"/>
/// once at <c>StartAsync</c> and logs each finding through
/// <see cref="ILogger"/>. The same evaluator drives the JSON health
/// endpoint, so what is logged here matches what
/// <c>GET /Overview/Health</c> returns. Agents tailing logs during a
/// fresh install can read these lines instead of having to hit the
/// health endpoint.
/// </summary>
internal sealed class GraphSearchtoolsStartupValidator : IHostedService
{
    private readonly IOptions<GraphSearchtoolsOptions> _options;
    private readonly ISearchChannelRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ILogger<GraphSearchtoolsStartupValidator> _logger;

    public GraphSearchtoolsStartupValidator(
        IOptions<GraphSearchtoolsOptions> options,
        ISearchChannelRegistry registry,
        IServiceProvider services,
        ILogger<GraphSearchtoolsStartupValidator> logger)
    {
        _options = options;
        _registry = registry;
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var channels = _registry.All;
        var hasLocalSink = _services.GetService<ITelemetrySink>() is LocalTelemetrySink;
        var diagnostics = StartupDiagnostics.Evaluate(_options.Value, channels, hasLocalSink);

        _logger.LogInformation(
            "GraphSearchtools ready: {ChannelCount} channel(s) registered, {DiagnosticCount} diagnostic(s).",
            channels.Count, diagnostics.Count);

        foreach (var d in diagnostics)
        {
            var level = d.Level switch
            {
                DiagnosticLevel.Error => LogLevel.Error,
                DiagnosticLevel.Warning => LogLevel.Warning,
                _ => LogLevel.Information,
            };
            _logger.Log(level, "GraphSearchtools[{Code}]: {Message}", d.Code, d.Message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
