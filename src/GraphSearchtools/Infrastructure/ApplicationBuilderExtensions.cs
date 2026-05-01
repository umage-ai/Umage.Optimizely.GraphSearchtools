using EPiServer.Shell;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using UmageAI.Optimizely.GraphSearchTools.Menu;

namespace UmageAI.Optimizely.GraphSearchTools.Infrastructure;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseGraphSearchtools(this IApplicationBuilder app)
    {
        return app;
    }

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
