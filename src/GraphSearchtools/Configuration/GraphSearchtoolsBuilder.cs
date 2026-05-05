using Microsoft.Extensions.DependencyInjection;

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Default implementation of <see cref="IGraphSearchtoolsBuilder"/>.
/// </summary>
internal sealed class GraphSearchtoolsBuilder : IGraphSearchtoolsBuilder
{
    public GraphSearchtoolsBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Profiles = new List<SearchProfile>();
    }

    public IServiceCollection Services { get; }

    public IList<SearchProfile> Profiles { get; }
}
