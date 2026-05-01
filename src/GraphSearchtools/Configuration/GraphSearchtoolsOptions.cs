namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Configuration options for Graph Search Tools.
/// Bound from appsettings section "CodeArt:GraphSearchtools" by AddGraphSearchtools().
/// </summary>
public class GraphSearchtoolsOptions
{
    /// <summary>
    /// Feature toggles to enable/disable individual tools.
    /// </summary>
    public FeatureToggles Features { get; set; } = new();

    /// <summary>
    /// When true, each feature checks the user's EPiServer permissions (Permissions For Functions)
    /// in addition to the authorization policy.
    /// </summary>
    public bool CheckPermissionForEachFeature { get; set; }

    /// <summary>
    /// Roles that have full access to all Graph Search Tools features.
    /// Default: WebAdmins, Administrators.
    /// </summary>
    public string[] AuthorizedRoles { get; set; } = ["WebAdmins", "Administrators"];

    /// <summary>
    /// Optional override for Optimizely Graph credentials. When unset, the addon reads
    /// from the host's Optimizely:ContentGraph section.
    /// </summary>
    public GraphConnectionOptions? Graph { get; set; }

    /// <summary>
    /// Content type fully qualified names that the search picker / search console
    /// should query. Defaults to ["_Page"] so the addon works out of the box on a
    /// vanilla CMS install.
    /// </summary>
    public string[] SearchableContentTypes { get; set; } = ["_Page"];
}

/// <summary>
/// Optimizely Graph connection credentials. Mirrors Optimizely:ContentGraph host config.
/// </summary>
public class GraphConnectionOptions
{
    public string? GatewayAddress { get; set; }
    public string? AppKey { get; set; }
    public string? Secret { get; set; }
    public string? SingleKey { get; set; }
}
