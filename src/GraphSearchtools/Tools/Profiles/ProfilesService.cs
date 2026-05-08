using System.Text.Json;
using System.Text.RegularExpressions;
using EPiServer.Framework.Localization;
using Microsoft.AspNetCore.Hosting;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Profiles.Models;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

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
    private readonly IGraphAdminClient _graphAdmin;
    private readonly QueryRunnerService _runner;

    public ProfilesService(
        ISearchProfileRegistry registry,
        SearchProfileEditService edits,
        LocalizationService localization,
        IWebHostEnvironment hostEnvironment,
        IGraphAdminClient graphAdmin,
        QueryRunnerService runner)
    {
        _registry = registry;
        _edits = edits;
        _localization = localization;
        _hostEnvironment = hostEnvironment;
        _graphAdmin = graphAdmin;
        _runner = runner;
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

        // Inline content takes precedence — the host code passed the actual
        // query string in via GraphQLDocumentInline, so there's no file to
        // load and the "exists" check is implicitly satisfied.
        var hasInline = !string.IsNullOrWhiteSpace(profile.GraphQLDocumentContent);
        var graphqlExists = hasInline;
        string? content = profile.GraphQLDocumentContent;

        if (!hasInline && !string.IsNullOrWhiteSpace(profile.GraphQLDocumentPath))
        {
            try
            {
                var fullPath = Path.Combine(_hostEnvironment.ContentRootPath, profile.GraphQLDocumentPath);
                graphqlExists = File.Exists(fullPath);
                if (graphqlExists)
                {
                    content = File.ReadAllText(fullPath);
                }
            }
            catch
            {
                graphqlExists = false;
                content = null;
            }
        }

        return new ProfileDetailViewModel
        {
            Key = profile.Key,
            DisplayName = profile.DisplayName?.Resolve(_localization) ?? profile.Key,
            DescriptionResolved = profile.Description?.Resolve(_localization),
            IsGeneric = string.Equals(profile.Key, GenericKey, StringComparison.OrdinalIgnoreCase),
            IsSiteShared = IsPinnedKeySiteShared(profile),
            Sites = profile.Sites ?? Array.Empty<string>(),
            Locales = profile.Locales ?? Array.Empty<string>(),
            SearchedFields = profile.SearchedFields ?? Array.Empty<string>(),
            PinnedKeyFormula = ResolvePinnedKeyFormula(profile),
            RankingName = profile.Ranking.ToString(),
            SemanticWeight = profile.SemanticWeight,
            GraphQLDocPath = profile.GraphQLDocumentPath,
            GraphQLDocExists = graphqlExists,
            GraphQLDocContent = content,
            GraphQLDocIsInline = hasInline,
            QueryAppliesPinned = !string.IsNullOrEmpty(content) && UsePinnedRegex.IsMatch(content),
            QueryAppliesSynonyms = !string.IsNullOrEmpty(content) && SynonymsArgRegex.IsMatch(content)
        };
    }

    private ProfileSummary BuildSummary(SearchProfile profile)
    {
        var displayName = profile.DisplayName?.Resolve(_localization) ?? profile.Key;
        var description = profile.Description?.Resolve(_localization);
        // Inline content always "exists" by virtue of being in the registration;
        // path-based registrations need the file to be on disk.
        var hasInline = !string.IsNullOrWhiteSpace(profile.GraphQLDocumentContent);
        var hasPath = !string.IsNullOrWhiteSpace(profile.GraphQLDocumentPath);
        var hasDoc = hasInline || hasPath;
        var docExists = hasInline || (hasPath && DocumentExists(profile.GraphQLDocumentPath!));
        var lastEdit = _edits.LatestForProfile(profile.Key);
        var isGeneric = string.Equals(profile.Key, GenericKey, StringComparison.OrdinalIgnoreCase);

        return new ProfileSummary
        {
            Key = profile.Key,
            DisplayName = displayName,
            DescriptionResolved = description,
            Sites = profile.Sites ?? Array.Empty<string>(),
            Locales = profile.Locales ?? Array.Empty<string>(),
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

    // Strips a `usePinned: { ... }` directive (with a single level of nested
    // braces, which matches the shape AlloySearchService emits) from a query
    // string. Used when no pinned collection exists for the requested locale —
    // sending `collectionId: ""` to Graph 400s, dropping the directive lets
    // the rest of the query run.
    private static readonly Regex UsePinnedRegex = new(@"\busePinned\s*:\s*\{[^{}]*\}", RegexOptions.Compiled);

    // Detects opt-in to Graph's synonym pool — the `synonyms: ONE|TWO` argument
    // inside an `_fulltext` clause. Without it, Graph silently bypasses the
    // synonym index even when rules are stored under the matching language. We
    // accept either slot enum so a profile that targets the staging slot still
    // reads as "applies synonyms".
    private static readonly Regex SynonymsArgRegex = new(@"\bsynonyms\s*:\s*(ONE|TWO)\b", RegexOptions.Compiled);

    /// <summary>
    /// Runs the registered profile's GraphQL document against Graph after
    /// substituting the runtime placeholders the addon's representative form
    /// uses (<c>"$phrase"</c>, <c>"$pinnedCollectionId"</c>). Returns null
    /// when the profile is unknown or carries no inline document.
    /// </summary>
    /// <remarks>
    /// This is the Profiles tab's preview endpoint — it deliberately does NOT
    /// look at <c>SavedQueries.DefaultQuery</c>, so a tenant-specific runner
    /// query configured at the host level can't pollute other profiles' Try-it.
    /// </remarks>
    public async Task<RunnerResult?> RunPreviewAsync(string profileKey, string phrase, string? locale, CancellationToken cancellationToken)
    {
        var profile = _registry.Get(profileKey);
        if (profile == null) return null;
        var template = profile.GraphQLDocumentContent;
        if (string.IsNullOrWhiteSpace(template)) return null;
        if (string.IsNullOrWhiteSpace(phrase)) return new RunnerResult(0, 0, template, Array.Empty<RunnerHit>());

        var query = template.Replace("\"$phrase\"", JsonSerializer.Serialize(phrase));

        // Resolve the pinned collection id for this profile + locale, if any.
        // Missing or empty → strip the usePinned directive so Graph doesn't
        // see a literal "$pinnedCollectionId" string or an empty id.
        string? collectionId = null;
        if (profile.PinnedKeyForLocale != null)
        {
            try
            {
                var pinnedKey = profile.PinnedKeyForLocale(locale ?? string.Empty);
                if (!string.IsNullOrEmpty(pinnedKey))
                {
                    var collections = await _graphAdmin.GetCollectionsAsync(cancellationToken);
                    collectionId = collections
                        .FirstOrDefault(c => string.Equals(c.Key, pinnedKey, StringComparison.OrdinalIgnoreCase))
                        ?.Id;
                }
            }
            catch
            {
                // Best-effort — fall through with no pin.
                collectionId = null;
            }
        }

        if (!string.IsNullOrEmpty(collectionId))
        {
            query = query.Replace("\"$pinnedCollectionId\"", JsonSerializer.Serialize(collectionId));
        }
        else
        {
            query = UsePinnedRegex.Replace(query, string.Empty);
        }

        var result = await _runner.RunRawAsync(query, variables: null, cancellationToken);

        // Mark which hits Graph would have pinned for this phrase. Mirrors
        // Graph's usePinned semantics on the server: load the collection's
        // items, keep targetKeys whose phrases match the preview phrase
        // (case-insensitive, comma-tokenized), then stamp Pinned=true on
        // RunnerHits whose ContentGuid is in that set. The UI gets a hard
        // boolean so the renderer doesn't have to reverse-engineer it from
        // the editor's local state — and it works regardless of which
        // tenant or how the registered query is shaped, as long as the
        // query projects ContentLink.GuidValue.
        if (!string.IsNullOrEmpty(collectionId) && result.Hits.Count > 0)
        {
            try
            {
                var pinnedTargets = await BuildPinnedTargetSetAsync(collectionId!, phrase, cancellationToken);
                if (pinnedTargets.Count > 0)
                {
                    var marked = new List<RunnerHit>(result.Hits.Count);
                    foreach (var hit in result.Hits)
                    {
                        var isPinned = !string.IsNullOrEmpty(hit.ContentGuid)
                            && pinnedTargets.Contains(hit.ContentGuid);
                        marked.Add(isPinned ? hit with { Pinned = true } : hit);
                    }
                    result = result with { Hits = marked };
                }
            }
            catch
            {
                // Pinned-marking is decorative; never let it break the preview.
            }
        }

        return result;
    }

    private async Task<HashSet<string>> BuildPinnedTargetSetAsync(string collectionId, string phrase, CancellationToken cancellationToken)
    {
        var items = await _graphAdmin.GetItemsAsync(collectionId, cancellationToken);
        var phraseLower = phrase.Trim().ToLowerInvariant();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.TargetKey)) continue;
            if (string.IsNullOrEmpty(item.Phrases)) continue;
            // Phrase semantics: an item applies to the preview phrase when any
            // of its comma-separated phrases case-insensitively contains, or
            // is contained by, the preview phrase. Mirrors the loose match
            // pattern used elsewhere in the editor.
            var matches = item.Phrases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(token =>
                {
                    var t = token.ToLowerInvariant();
                    return t.Length > 0 && (t == phraseLower || t.Contains(phraseLower) || phraseLower.Contains(t));
                });
            if (matches) targets.Add(item.TargetKey);
        }
        return targets;
    }
}
