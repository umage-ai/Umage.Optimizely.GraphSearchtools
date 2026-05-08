using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Components;

/// <summary>
/// Renders the Featured Products surface that the <c>alloy-products</c>
/// search profile is wired to. Invoked from <c>Views/StartPage/Index.cshtml</c>.
/// </summary>
public class FeaturedProductsViewComponent : ViewComponent
{
    private readonly FeaturedProductsService _service;

    public FeaturedProductsViewComponent(FeaturedProductsService service)
    {
        _service = service;
    }

    public async Task<IViewComponentResult> InvokeAsync(string? locale = null, int limit = 6)
    {
        var products = await _service.GetFeaturedAsync(locale ?? "en", limit, HttpContext.RequestAborted);
        return View(products);
    }
}
