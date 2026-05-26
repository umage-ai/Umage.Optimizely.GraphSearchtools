using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Permissions;

/// <summary>
/// Snapshots the per-feature access decisions for the current request and
/// hands them to the layout in a shape that can be serialized to
/// <c>window.GST_PERMS</c>. JS consumers gate menu items, Overview cards,
/// and edit buttons off the resulting booleans so the UI matches the
/// server-side authorization without a round-trip per element.
/// </summary>
internal sealed class PermissionMap
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly FeatureAccessChecker _accessChecker;

    public PermissionMap(IHttpContextAccessor httpContextAccessor, FeatureAccessChecker accessChecker)
    {
        _httpContextAccessor = httpContextAccessor;
        _accessChecker = accessChecker;
    }

    public Snapshot ForCurrentUser()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx == null) return Snapshot.Empty;

        return new Snapshot(
            channels:      _accessChecker.HasAccess(ctx, nameof(FeatureToggles.Channels),  GraphSearchtoolsPermissions.Channels),
            insights:      _accessChecker.HasAccess(ctx, nameof(FeatureToggles.Insights),  GraphSearchtoolsPermissions.Insights),
            pinned:        _accessChecker.HasAccess(ctx, nameof(FeatureToggles.Pinned),    GraphSearchtoolsPermissions.Pinned),
            pinnedEdit:    _accessChecker.HasAccess(ctx, nameof(FeatureToggles.Pinned),    GraphSearchtoolsPermissions.PinnedEdit),
            collections:   _accessChecker.HasAccess(ctx, nameof(FeatureToggles.Pinned),    GraphSearchtoolsPermissions.Collections),
            synonyms:      _accessChecker.HasAccess(ctx, nameof(FeatureToggles.Synonyms),  GraphSearchtoolsPermissions.Synonyms),
            synonymsEdit:  _accessChecker.HasAccess(ctx, nameof(FeatureToggles.Synonyms),  GraphSearchtoolsPermissions.SynonymsEdit)
        );
    }

    internal sealed record Snapshot(
        bool channels,
        bool insights,
        bool pinned,
        bool pinnedEdit,
        bool collections,
        bool synonyms,
        bool synonymsEdit)
    {
        public static readonly Snapshot Empty = new(false, false, false, false, false, false, false);

        public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
