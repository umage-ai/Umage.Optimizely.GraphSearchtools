using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace UmageAI.Optimizely.GraphSearchTools.Localization;

[Authorize(Policy = "umageai:graphsearchtools")]
public class UiStringsController : Controller
{
    private readonly UiStringsProvider _provider;

    public UiStringsController(UiStringsProvider provider)
    {
        _provider = provider;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        return Json(_provider.GetAll());
    }
}
