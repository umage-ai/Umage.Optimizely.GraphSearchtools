using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Helpers;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Sites;

/// <summary>
/// Read-only API surface used by the Pinned Results and Synonyms tools to
/// populate site/language pickers. The list is derived generically from
/// <see cref="LanguageSiteEnumerator"/> so the addon makes no assumption about
/// the host site's content model.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SitesApiController : Controller
{
    private readonly LanguageSiteEnumerator _enumerator;

    public SitesApiController(LanguageSiteEnumerator enumerator)
    {
        _enumerator = enumerator;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<SiteInfo>> List()
    {
        return Ok(_enumerator.Enumerate());
    }
}
