namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Feature toggles for enabling/disabling individual tools.
/// Each toggle defaults to enabled so a fresh install lights up every tool
/// the package version supports.
/// </summary>
public class FeatureToggles
{
    public bool Overview { get; set; } = true;
    public bool Pinned { get; set; } = true;
    public bool Synonyms { get; set; } = true;
    public bool Health { get; set; } = true;
    public bool Autocomplete { get; set; } = true;
    public bool Profiles { get; set; } = true;
    public bool DecaySandbox { get; set; } = true;
    public bool Webhooks { get; set; } = true;
    public bool CustomDataSources { get; set; } = true;
    public bool RequestLogs { get; set; } = true;

    /// <summary>
    /// Phase 4 foundation — gates the telemetry ingest endpoints
    /// (<c>POST /TelemetryApi/SearchLog</c>, <c>POST /TelemetryApi/SearchLogBatch</c>).
    /// Phase 4 Wave 5 analytics tools (Search Logs UI, Pinned Result Coverage,
    /// Synonym Coverage) consume the data this captures.
    /// </summary>
    public bool Telemetry { get; set; } = true;
}
