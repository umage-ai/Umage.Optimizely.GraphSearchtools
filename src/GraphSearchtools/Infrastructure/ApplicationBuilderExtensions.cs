using EPiServer.Shell;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using UmageAI.Optimizely.GraphSearchTools.Menu;

namespace UmageAI.Optimizely.GraphSearchTools.Infrastructure;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Activates the GraphSearchtools middleware pipeline. Call after the
    /// host's authentication/authorization middleware and before
    /// <c>UseEndpoints</c>. Currently a no-op reserved for future
    /// middleware (e.g. request-scoped audit context); calling it is still
    /// required so existing wiring remains forward-compatible.
    /// </summary>
    public static IApplicationBuilder UseGraphSearchtools(this IApplicationBuilder app)
    {
        return app;
    }

    /// <summary>
    /// Registers the GraphSearchtools controller routes under the CMS
    /// shell's module base path (e.g.
    /// <c>/EPiServer/cms/graphsearchtools/{controller}/{action}/{id?}</c>).
    /// Call inside <c>UseEndpoints(endpoints =&gt; ...)</c> alongside the
    /// host's own endpoint mappings. The public telemetry ingest endpoint
    /// (<c>POST /api/telemetry/searchlog</c>) is registered through the
    /// addon's <c>[Route]</c> attribute and is reachable independently of
    /// this route mapping.
    /// </summary>
    public static IEndpointRouteBuilder MapGraphSearchtools(this IEndpointRouteBuilder endpoints)
    {
        var basePath = Paths.ToResource(typeof(GraphSearchtoolsMenuProvider), "")
            .TrimStart('/').TrimEnd('/');
        endpoints.MapControllerRoute(
            name: "GraphSearchtoolsDefault",
            pattern: $"{basePath}/{{controller}}/{{action}}/{{id?}}");

        return endpoints;
    }
}
