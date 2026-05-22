using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Channels;

/// <summary>
/// Serves the Razor views for the Channels top-level surface (index +
/// per-channel detail page). API calls go through
/// <see cref="ChannelsApiController"/>.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
public class ChannelsController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Channels);

    private readonly ChannelsService _service;
    private readonly FeatureAccessChecker _accessChecker;

    public ChannelsController(ChannelsService service, FeatureAccessChecker accessChecker)
    {
        _service = service;
        _accessChecker = accessChecker;
    }

    /// <summary>
    /// Renders the channels index when called without <paramref name="key"/>,
    /// otherwise renders the channel detail page for that key.
    /// </summary>
    /// <remarks>
    /// Both surfaces are served from the same controller action on purpose.
    /// The CMS shell maps URL → product-id (e.g. <c>global_cms</c>) from the
    /// set of registered menu URLs; a deep URL like <c>/Channels/alloy-search</c>
    /// would not match any menu item, leaving the sidebar stuck on loading
    /// dots. Keeping the key as a query parameter preserves the menu's
    /// <c>/Channels/Index</c> URL match.
    /// Routing uses the convention route registered by
    /// <see cref="UmageAI.Optimizely.GraphSearchTools.Infrastructure.ApplicationBuilderExtensions.MapGraphSearchtools(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)"/>,
    /// so the URL resolves to <c>{module-base}/Channels/Index</c> — the same
    /// module prefix every other tool uses, which is what lets the CMS 13
    /// platform chrome do SPA-style transitions between tools instead of
    /// triggering a full reload.
    /// </remarks>
    [HttpGet]
    public IActionResult Index([FromQuery] string? key = null)
    {
        if (!HasAccess()) return Forbid();

        if (!string.IsNullOrEmpty(key))
        {
            var model = _service.BuildDetailViewModel(key);
            if (model == null) return NotFound();
            return View("/Views/Channels/Detail.cshtml", model);
        }

        return View("/Views/Channels/Index.cshtml");
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Channels);
}
