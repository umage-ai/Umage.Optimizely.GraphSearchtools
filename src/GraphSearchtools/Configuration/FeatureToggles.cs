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

    /// <summary>
    /// Back-compat toggle for the legacy "Saved Queries" label. Phase 2.5 renamed
    /// the menu entry to "Diagnostics" (see <see cref="Diagnostics"/>); both
    /// flags must be true for the entry to surface — explicitly setting either to
    /// false hides it. Remove once all hosts have migrated to Diagnostics.
    /// </summary>
    public bool SavedQueries { get; set; } = true;

    public bool Profiles { get; set; } = true;

    /// <summary>
    /// When true, the Diagnostics top-level entry (formerly Saved Queries)
    /// appears in the menu. Hosts that want to hide diagnostics from production
    /// editors can set <c>CodeArt:GraphSearchtools:Features:Diagnostics: false</c>
    /// in production appsettings. Default stays true; the env-aware default
    /// (true in dev, false in prod) is a follow-up.
    /// </summary>
    public bool Diagnostics { get; set; } = true;
}
