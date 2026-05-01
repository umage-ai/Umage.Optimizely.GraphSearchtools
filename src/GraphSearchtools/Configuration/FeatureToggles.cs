namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Feature toggles for enabling/disabling individual tools.
/// Phase 0 ships only the Overview tile; subsequent phases extend this class.
/// </summary>
public class FeatureToggles
{
    public bool Overview { get; set; } = true;
}
