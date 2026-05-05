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
    public bool SavedQueries { get; set; } = true;

    // STUB: belongs to foundation agent — to be replaced at integration.
    // The foundation agent introduces the Profiles toggle alongside the
    // Search Profiles registration plumbing. Stubbed here so the UI scaffolding
    // can reference it via nameof(FeatureToggles.Profiles).
    public bool Profiles { get; set; } = true;
}
