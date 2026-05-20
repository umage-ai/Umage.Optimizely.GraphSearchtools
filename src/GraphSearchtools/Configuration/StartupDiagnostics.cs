namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Severity of a <see cref="Diagnostic"/>. <see cref="Info"/> is a setup
/// observation (not a problem), <see cref="Warning"/> means the install
/// will run but something is likely misconfigured, <see cref="Error"/>
/// means a required surface will not work.
/// </summary>
public enum DiagnosticLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One setup observation. <see cref="Code"/> is a stable short identifier
/// (e.g. <c>no-channels</c>) intended for programmatic consumers — agents,
/// log filters, dashboards. <see cref="Message"/> is the human-readable
/// explanation including the suggested fix.
/// </summary>
public sealed record Diagnostic(DiagnosticLevel Level, string Code, string Message);

/// <summary>
/// Pure evaluator that turns the addon's configuration + registry state +
/// telemetry wiring into a list of <see cref="Diagnostic"/> records.
/// Shared between the startup logger and the JSON health endpoint so both
/// surfaces report identical findings.
/// </summary>
public static class StartupDiagnostics
{
    /// <summary>
    /// Computes diagnostics for the current addon state. Pure — no DI,
    /// no logger, no exceptions.
    /// </summary>
    /// <param name="options">Bound <see cref="GraphSearchtoolsOptions"/>.</param>
    /// <param name="channels">Channels currently in the registry.</param>
    /// <param name="localSinkConfigured">
    /// True when the default local <c>ITelemetrySink</c> is wired; false when
    /// <c>UseExternalTelemetryReader&lt;T&gt;()</c> has swapped it out.
    /// </param>
    public static IReadOnlyList<Diagnostic> Evaluate(
        GraphSearchtoolsOptions options,
        IReadOnlyList<SearchChannel> channels,
        bool localSinkConfigured)
    {
        var results = new List<Diagnostic>();

        if (channels.Count == 0)
        {
            results.Add(new Diagnostic(
                DiagnosticLevel.Info,
                "no-channels",
                "No search channels are registered. Marketers will only see the synthesized Generic fallback. " +
                "Call AddSearchChannel(\"key\", c => …) on the builder returned by AddGraphSearchtools to register a channel."));
        }

        var creds = options.Graph;
        if (creds != null && (LooksLikePlaceholder(creds.AppKey)
                              || LooksLikePlaceholder(creds.Secret)
                              || LooksLikePlaceholder(creds.SingleKey)
                              || LooksLikePlaceholder(creds.GatewayAddress)))
        {
            results.Add(new Diagnostic(
                DiagnosticLevel.Warning,
                "placeholder-credentials",
                "Optimizely Graph credentials in UmageAI:GraphSearchTools:Graph look like placeholders " +
                "(e.g. \"...\", \"your-key\", \"TODO\"). Replace them with real values, or remove the section " +
                "entirely so the host's Optimizely:ContentGraph block is used."));
        }

        if (options.Features.Insights && !options.Features.Telemetry)
        {
            results.Add(new Diagnostic(
                DiagnosticLevel.Warning,
                "insights-without-telemetry",
                "Features.Insights = true but Features.Telemetry = false. The Insights tool reads aggregates from " +
                "the telemetry pipeline; with Telemetry disabled it will show empty results."));
        }

        if (options.Features.SearchLogs && !options.Features.Telemetry)
        {
            results.Add(new Diagnostic(
                DiagnosticLevel.Warning,
                "searchlogs-without-telemetry",
                "Features.SearchLogs = true but Features.Telemetry = false. SearchLogs reads from the telemetry " +
                "pipeline; with Telemetry disabled it will show empty results."));
        }

        if (options.Features.Telemetry && !localSinkConfigured)
        {
            results.Add(new Diagnostic(
                DiagnosticLevel.Info,
                "external-telemetry-reader",
                "Features.Telemetry = true but no local sink is wired — an external ITelemetryReader is in use. " +
                "The public ingest endpoint at /api/telemetry/searchlog will return 410 Gone; beacon events to " +
                "your analytics platform directly instead."));
        }

        return results;
    }

    /// <summary>
    /// Heuristic placeholder detector. Treats null/empty as "not set" (no
    /// diagnostic) since partial overrides are valid — only flags values
    /// that look like the developer pasted them from a docs sample.
    /// </summary>
    internal static bool LooksLikePlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed == "..." || trimmed.Contains("...")) return true;

        var lowered = trimmed.ToLowerInvariant();
        return lowered.StartsWith("your")
            || lowered.StartsWith("todo")
            || lowered.StartsWith("replace")
            || lowered.StartsWith("change")
            || lowered.StartsWith("placeholder")
            || lowered.StartsWith("xxx")
            || lowered == "<key>"
            || lowered == "<secret>";
    }
}
