using EPiServer.Shell;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using UmageAI.Optimizely.GraphSearchTools.Menu;

namespace UmageAI.Optimizely.GraphSearchTools.Infrastructure;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the GraphSearchtools controller routes under the CMS
    /// shell's module base path — <c>/EPiServer/GraphSearchtools/...</c>
    /// on CMS 12 or <c>/Optimizely/GraphSearchtools/...</c> on CMS 13,
    /// resolved from the shell's configured base path so it tracks
    /// whichever the host runs. The full pattern is
    /// <c>{basePath}/{controller}/{action}/{id?}</c>. Call inside
    /// <c>UseEndpoints(endpoints =&gt; ...)</c> alongside the host's own
    /// endpoint mappings. The public telemetry ingest endpoint
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
