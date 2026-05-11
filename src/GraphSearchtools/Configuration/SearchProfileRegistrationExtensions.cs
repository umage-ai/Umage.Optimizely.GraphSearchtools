using Microsoft.Extensions.DependencyInjection;

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Fluent <c>AddSearchProfile(...)</c> extension on
/// <see cref="IGraphSearchtoolsBuilder"/>. The configured
/// <see cref="SearchProfile"/> is appended to <see cref="IGraphSearchtoolsBuilder.Profiles"/>
/// for tests and diagnostics, and registered as a singleton in the underlying
/// <see cref="IServiceCollection"/> so <see cref="SearchProfileRegistry"/> can
/// pick it up via <c>IEnumerable&lt;SearchProfile&gt;</c> injection.
/// </summary>
public static class SearchProfileRegistrationExtensions
{
    /// <summary>
    /// Registers a profile under <paramref name="key"/> and applies the supplied
    /// fluent configuration. Throws when the key fails the <c>[a-z0-9-]+</c>
    /// validation; throws if the same key is registered twice on the same builder.
    /// </summary>
    public static IGraphSearchtoolsBuilder AddSearchProfile(
        this IGraphSearchtoolsBuilder builder,
        string key,
        Action<SearchProfileBuilder> configure)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var profileBuilder = new SearchProfileBuilder(key);
        configure(profileBuilder);
        var profile = profileBuilder.Build();

        if (builder.Profiles.Any(p => string.Equals(p.Key, profile.Key, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Search profile '{profile.Key}' is already registered.");
        }

        builder.Profiles.Add(profile);
        builder.Services.AddSingleton(profile);
        return builder;
    }
}
