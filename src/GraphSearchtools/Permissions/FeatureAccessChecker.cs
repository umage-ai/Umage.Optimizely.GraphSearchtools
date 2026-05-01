using EPiServer.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Permissions;

/// <summary>
/// Checks whether a user has access to a specific feature based on:
/// 1. Feature toggle (is the feature enabled at all?)
/// 2. EPiServer permission (optional, per-user check if CheckPermissionForEachFeature is true)
/// </summary>
public class FeatureAccessChecker
{
    private readonly IOptions<GraphSearchtoolsOptions> _options;
    private readonly PermissionService _permissionService;

    public FeatureAccessChecker(
        IOptions<GraphSearchtoolsOptions> options,
        PermissionService permissionService)
    {
        _options = options;
        _permissionService = permissionService;
    }

    public bool IsFeatureEnabled(string featureName)
    {
        var toggles = _options.Value.Features;
        var property = typeof(FeatureToggles).GetProperty(featureName);
        if (property == null)
            return false;

        return (bool)(property.GetValue(toggles) ?? false);
    }

    public bool HasPermission(HttpContext context, PermissionType permissionType)
    {
        if (!_options.Value.CheckPermissionForEachFeature)
            return true;

        return _permissionService.IsPermitted(context.User, permissionType);
    }

    public bool HasAccess(HttpContext context, string featureName, PermissionType permissionType)
    {
        return IsFeatureEnabled(featureName) && HasPermission(context, permissionType);
    }
}
