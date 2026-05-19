using Microsoft.Extensions.DependencyInjection;

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Builder returned from <c>AddGraphSearchtools(...)</c> so callers can chain
/// further configuration — currently <c>AddSearchChannel(...)</c>.
/// </summary>
/// <remarks>
/// The shape mirrors the ASP.NET pattern (see <c>IMvcBuilder</c>,
/// <c>IIdentityBuilder</c>): a thin pair of <see cref="Services"/> plus a
/// feature-specific collection that extension methods append to.
/// </remarks>
public interface IGraphSearchtoolsBuilder
{
    /// <summary>The underlying service collection — exposed so callers can drop
    /// out of the fluent chain when they need to register their own services.</summary>
    IServiceCollection Services { get; }

    /// <summary>Channels registered so far via <c>AddSearchChannel(...)</c>.</summary>
    IList<SearchChannel> Channels { get; }
}
