using EPiServer.Framework.Localization;
using Microsoft.AspNetCore.Hosting;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Profiles.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Profiles;

/// <summary>
/// Reads the profile registry + edit log and shapes both into JSON-friendly
/// view models for the Profiles UI. No writes — Phase 2.5 v1 is read-only.
/// </summary>
public sealed class ProfilesService
{
    private const string GenericKey = "generic";

    private readonly ISearchProfileRegistry _registry;
    private readonly SearchProfileEditService _edits;
    private readonly LocalizationService _localization;
    private readonly IWebHostEnvironment _hostEnvironment;

    public ProfilesService(
        ISearchProfileRegistry registry,
        SearchProfileEditService edits,
        LocalizationService localization,
        IWebHostEnvironment hostEnvironment)
    {
        _registry = registry;
        _edits = edits;
        _localization = localization;
        _hostEnvironment = hostEnvironment;
    }

    public IReadOnlyList<ProfileSummary> ListSummaries()
        => _registry.All.Select(p => BuildSummary(p)).ToList();

    public ProfileDetail? BuildDetail(string key)
    {
        var profile = _registry.Get(key);
        if (profile == null) return null;

        var summary = BuildSummary(profile);
        var edits = _edits.ListForProfile(profile.Key, take: 25)
            .Select(ProfileEditDto.From)
            .ToList();

        return new ProfileDetail
        {
            Summary = summary,
            SearchedFields = profile.SearchedFields ?? Array.Empty<string>(),
            PinnedKeySample = ResolvePinnedKeySample(profile),
            PinnedKeyIsSiteShared = IsPinnedKeySiteShared(profile),
            // v1: counts that require live Graph calls are surfaced as 0 here.
            // Phase 2.5 follow-up wires Graph admin calls per the design doc §3.
            PinnedPhraseCount = 0,
            SynonymEntryCount = 0,
            RecentEdits = edits
        };
    }

    public IReadOnlyList<ProfileEditDto> ListAudit(string key, int take)
    {
        if (_registry.Get(key) == null) return Array.Empty<ProfileEditDto>();
        return _edits.ListForProfile(key, take).Select(ProfileEditDto.From).ToList();
    }

    /// <summary>
    /// Razor view model variant — we pre-resolve display strings here so the
    /// view itself stays presentation-only.
    /// </summary>
    public ProfileDetailViewModel? BuildDetailViewModel(string key)
    {
        var profile = _registry.Get(key);
        if (profile == null) return null;

        var graphqlExists = false;
        if (!string.IsNullOrWhiteSpace(profile.GraphQLDocumentPath))
        {
            try
            {
                var fullPath = Path.Combine(_hostEnvironment.ContentRootPath, profile.GraphQLDocumentPath);
                graphqlExists = File.Exists(fullPath);
            }
            catch
            {
                graphqlExists = false;
            }
        }

        return new ProfileDetailViewModel
        {
            Key = profile.Key,
            DisplayName = profile.DisplayName?.Resolve(_localization) ?? profile.Key,
            DescriptionResolved = profile.Description?.Resolve(_localization),
            HasSynonymTab = !string.IsNullOrWhiteSpace(profile.SynonymSlot),
            IsGeneric = string.Equals(profile.Key, GenericKey, StringComparison.OrdinalIgnoreCase),
            IsSiteShared = IsPinnedKeySiteShared(profile),
            Sites = profile.Sites ?? Array.Empty<string>(),
            Locales = profile.Locales ?? Array.Empty<string>(),
            SearchedFields = profile.SearchedFields ?? Array.Empty<string>(),
            SynonymSlot = profile.SynonymSlot,
            PinnedKeyFormula = ResolvePinnedKeyFormula(profile),
            RankingName = profile.Ranking.ToString(),
            SemanticWeight = profile.SemanticWeight,
            GraphQLDocPath = profile.GraphQLDocumentPath,
            GraphQLDocExists = graphqlExists
        };
    }

    private ProfileSummary BuildSummary(SearchProfile profile)
    {
        var displayName = profile.DisplayName?.Resolve(_localization) ?? profile.Key;
        var description = profile.Description?.Resolve(_localization);
        var hasDoc = !string.IsNullOrWhiteSpace(profile.GraphQLDocumentPath);
        var docExists = hasDoc && DocumentExists(profile.GraphQLDocumentPath!);
        var lastEdit = _edits.LatestForProfile(profile.Key);
        var isGeneric = string.Equals(profile.Key, GenericKey, StringComparison.OrdinalIgnoreCase);

        return new ProfileSummary
        {
            Key = profile.Key,
            DisplayName = displayName,
            DescriptionResolved = description,
            Sites = profile.Sites ?? Array.Empty<string>(),
            Locales = profile.Locales ?? Array.Empty<string>(),
            SynonymSlot = profile.SynonymSlot,
            HasGraphQLDoc = hasDoc,
            GraphQLDocPath = profile.GraphQLDocumentPath,
            SemanticWeight = profile.SemanticWeight,
            RankingName = profile.Ranking.ToString(),
            IsGeneric = isGeneric,
            Status = DeriveStatus(profile, isGeneric, hasDoc, docExists, lastEdit),
            LastEditedAt = lastEdit?.At,
            LastEditedBy = lastEdit?.ActorName ?? lastEdit?.ActorId
        };
    }

    private static ProfileStatus DeriveStatus(
        SearchProfile profile,
        bool isGeneric,
        bool hasDoc,
        bool docExists,
        SearchProfileEdit? lastEdit)
    {
        if (hasDoc && !docExists) return ProfileStatus.DocMissing;
        if (isGeneric) return ProfileStatus.FreeForm;
        if (lastEdit == null) return ProfileStatus.Cold;
        return ProfileStatus.Tuned;
    }

    private bool DocumentExists(string relativePath)
    {
        try
        {
            var full = Path.Combine(_hostEnvironment.ContentRootPath, relativePath);
            return File.Exists(full);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolvePinnedKeySample(SearchProfile profile)
    {
        if (profile.PinnedKeyForLocale == null) return null;
        var locale = profile.Locales?.FirstOrDefault() ?? "en";
        try { return profile.PinnedKeyForLocale(locale); }
        catch { return null; }
    }

    /// <summary>
    /// Returns the formula expressed as a token string ("site-{locale}") if
    /// possible, otherwise the resolved sample. Detection is heuristic — we
    /// invoke the lambda twice with two different locales and diff.
    /// </summary>
    private static string? ResolvePinnedKeyFormula(SearchProfile profile)
    {
        if (profile.PinnedKeyForLocale == null) return null;
        try
        {
            var a = profile.PinnedKeyForLocale("en");
            var b = profile.PinnedKeyForLocale("__locale__");
            if (a == b) return a; // formula doesn't depend on locale
            // Replace the substituted-in marker with a placeholder so the UI
            // shows "site-{locale}" rather than "site-__locale__".
            return b.Replace("__locale__", "{locale}", StringComparison.Ordinal);
        }
        catch
        {
            return ResolvePinnedKeySample(profile);
        }
    }

    /// <summary>
    /// True when invoking the pinned-key formula gives the same key for any
    /// site in the profile's <c>Sites</c> list — implies the marketer's edits
    /// will land on every site simultaneously.
    /// </summary>
    private static bool IsPinnedKeySiteShared(SearchProfile profile)
    {
        if (profile.PinnedKeyForLocale == null) return false;
        if (profile.Sites == null || profile.Sites.Count <= 1) return false;
        try
        {
            var locale = profile.Locales?.FirstOrDefault() ?? "en";
            var first = profile.PinnedKeyForLocale(locale);
            // The lambda takes a locale, not a site. If the formula were
            // site-aware the developer would have closed over site context —
            // we can't introspect that, so the heuristic here is: if Sites > 1
            // and the formula returns one value per locale, it's shared.
            return !string.IsNullOrEmpty(first);
        }
        catch
        {
            return false;
        }
    }
}
