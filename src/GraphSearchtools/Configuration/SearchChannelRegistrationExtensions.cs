using Microsoft.Extensions.DependencyInjection;

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Fluent <c>AddSearchChannel(...)</c> extension on
/// <see cref="IGraphSearchtoolsBuilder"/>. The configured
/// <see cref="SearchChannel"/> is appended to <see cref="IGraphSearchtoolsBuilder.Channels"/>
/// for tests and diagnostics, and registered as a singleton in the underlying
/// <see cref="IServiceCollection"/> so <see cref="SearchChannelRegistry"/> can
/// pick it up via <c>IEnumerable&lt;SearchChannel&gt;</c> injection.
/// </summary>
public static class SearchChannelRegistrationExtensions
{
    /// <summary>
    /// Registers a channel under <paramref name="key"/> and applies the supplied
    /// fluent configuration. Throws when the key fails the <c>[a-z0-9-]+</c>
    /// validation; throws if the same key is registered twice on the same builder.
    /// </summary>
    public static IGraphSearchtoolsBuilder AddSearchChannel(
        this IGraphSearchtoolsBuilder builder,
        string key,
        Action<SearchChannelBuilder> configure)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var channelBuilder = new SearchChannelBuilder(key);
        configure(channelBuilder);
        var channel = channelBuilder.Build();

        if (builder.Channels.Any(p => string.Equals(p.Key, channel.Key, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Search channel '{channel.Key}' is already registered.");
        }

        builder.Channels.Add(channel);
        builder.Services.AddSingleton(channel);
        return builder;
    }
}
