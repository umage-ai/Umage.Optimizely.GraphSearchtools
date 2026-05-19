using System.Text.Json;
using System.Text.RegularExpressions;
using EPiServer.Framework.Localization;
using Microsoft.AspNetCore.Hosting;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Channels.Models;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Channels;

/// <summary>
/// Reads the channel registry + edit log and shapes both into JSON-friendly
/// view models for the Channels UI. No writes — Phase 2.5 v1 is read-only.
/// </summary>
public sealed class ChannelsService
{
    private readonly ISearchChannelRegistry _registry;
    private readonly AuditLogService _audit;
    private readonly LocalizationService _localization;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly IGraphAdminClient _graphAdmin;
    private readonly QueryRunnerService _runner;
    private readonly CmsLocaleResolver _localeResolver;

    public ChannelsService(
        ISearchChannelRegistry registry,
        AuditLogService audit,
        LocalizationService localization,
        IWebHostEnvironment hostEnvironment,
        IGraphAdminClient graphAdmin,
        QueryRunnerService runner,
        CmsLocaleResolver localeResolver)
    {
        _registry = registry;
        _audit = audit;
        _localization = localization;
        _hostEnvironment = hostEnvironment;
        _graphAdmin = graphAdmin;
        _runner = runner;
        _localeResolver = localeResolver;
    }

    /// <summary>
    /// Channel-locale lookup. Returns the static list for declarative
    /// channels, or the CMS-derived list when the channel opted in with
    /// <c>LocalesFromCmsLanguages()</c>. Use this everywhere instead of
    /// reading <c>channel.Locales</c> directly so both modes stay
    /// transparent to the rest of the service.
    /// </summary>
    private IReadOnlyList<string> LocalesFor(SearchChannel channel)
        => _localeResolver.Resolve(channel);

    public IReadOnlyList<ChannelSummary> ListSummaries()
        => _registry.All.Select(p => BuildSummary(p)).ToList();

    public ChannelDetail? BuildDetail(string key)
    {
        var channel = _registry.Get(key);
        if (channel == null) return null;

        return new ChannelDetail
        {
            Summary = BuildSummary(channel),
            SearchedFields = channel.SearchedFields ?? Array.Empty<string>(),
            PinnedKeySample = ResolvePinnedKeySample(channel),
            PinnedKeyIsSiteShared = IsPinnedKeySiteShared(channel),
            // v1: counts that require live Graph calls are surfaced as 0 here.
            // Phase 2.5 follow-up wires Graph admin calls per the design doc §3.
            PinnedPhraseCount = 0,
            SynonymEntryCount = 0
        };
    }

    /// <summary>
    /// Razor view model variant — we pre-resolve display strings here so the
    /// view itself stays presentation-only.
    /// </summary>
    public ChannelDetailViewModel? BuildDetailViewModel(string key)
    {
        var channel = _registry.Get(key);
        if (channel == null) return null;

        // Inline content takes precedence — the host code passed the actual
        // query string in via GraphQLDocumentInline, so there's no file to
        // load and the "exists" check is implicitly satisfied.
        var hasInline = !string.IsNullOrWhiteSpace(channel.GraphQLDocumentContent);
        var graphqlExists = hasInline;
        string? content = channel.GraphQLDocumentContent;

        if (!hasInline && !string.IsNullOrWhiteSpace(channel.GraphQLDocumentPath))
        {
            try
            {
                var fullPath = Path.Combine(_hostEnvironment.ContentRootPath, channel.GraphQLDocumentPath);
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

        return new ChannelDetailViewModel
        {
            Key = channel.Key,
            DisplayName = channel.DisplayName?.Resolve(_localization) ?? channel.Key,
            DescriptionResolved = channel.Description?.Resolve(_localization),
            IsSiteShared = IsPinnedKeySiteShared(channel),
            Sites = channel.Sites ?? Array.Empty<string>(),
            Locales = LocalesFor(channel),
            SearchedFields = channel.SearchedFields ?? Array.Empty<string>(),
            PinnedKeyFormula = ResolvePinnedKeyFormula(channel),
            RankingName = channel.Ranking.ToString(),
            SemanticWeight = channel.SemanticWeight,
            GraphQLDocPath = channel.GraphQLDocumentPath,
            GraphQLDocExists = graphqlExists,
            GraphQLDocContent = content,
            GraphQLDocIsInline = hasInline,
            QueryAppliesPinned = !string.IsNullOrEmpty(content) && UsePinnedRegex.IsMatch(content),
            QueryAppliesSynonyms = !string.IsNullOrEmpty(content) && SynonymsArgRegex.IsMatch(content)
        };
    }

    private ChannelSummary BuildSummary(SearchChannel channel)
    {
        var displayName = channel.DisplayName?.Resolve(_localization) ?? channel.Key;
        var description = channel.Description?.Resolve(_localization);
        // Inline content always "exists" by virtue of being in the registration;
        // path-based registrations need the file to be on disk.
        var hasInline = !string.IsNullOrWhiteSpace(channel.GraphQLDocumentContent);
        var hasPath = !string.IsNullOrWhiteSpace(channel.GraphQLDocumentPath);
        var hasDoc = hasInline || hasPath;
        var docExists = hasInline || (hasPath && DocumentExists(channel.GraphQLDocumentPath!));
        var lastEdit = _audit.LatestForChannel(channel.Key);

        return new ChannelSummary
        {
            Key = channel.Key,
            DisplayName = displayName,
            DescriptionResolved = description,
            Sites = channel.Sites ?? Array.Empty<string>(),
            Locales = LocalesFor(channel),
            HasGraphQLDoc = hasDoc,
            GraphQLDocPath = channel.GraphQLDocumentPath,
            SemanticWeight = channel.SemanticWeight,
            RankingName = channel.Ranking.ToString(),
            Status = DeriveStatus(hasDoc, docExists, lastEdit),
            LastEditedAt = lastEdit?.At,
            LastEditedBy = lastEdit?.ActorName ?? lastEdit?.ActorId
        };
    }

    private static ChannelStatus DeriveStatus(
        bool hasDoc,
        bool docExists,
        AuditLogEntry? lastEdit)
    {
        if (hasDoc && !docExists) return ChannelStatus.DocMissing;
        if (lastEdit == null) return ChannelStatus.Cold;
        return ChannelStatus.Tuned;
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

    private static string? ResolvePinnedKeySample(SearchChannel channel)
    {
        if (channel.PinnedKeyForLocale == null) return null;
        var locale = channel.Locales?.FirstOrDefault() ?? "en";
        try { return channel.PinnedKeyForLocale(locale); }
        catch { return null; }
    }

    /// <summary>
    /// Returns the formula expressed as a token string ("site-{locale}") if
    /// possible, otherwise the resolved sample. Detection is heuristic — we
    /// invoke the lambda twice with two different locales and diff.
    /// </summary>
    private static string? ResolvePinnedKeyFormula(SearchChannel channel)
    {
        if (channel.PinnedKeyForLocale == null) return null;
        try
        {
            var a = channel.PinnedKeyForLocale("en");
            var b = channel.PinnedKeyForLocale("__locale__");
            if (a == b) return a; // formula doesn't depend on locale
            // Replace the substituted-in marker with a placeholder so the UI
            // shows "site-{locale}" rather than "site-__locale__".
            return b.Replace("__locale__", "{locale}", StringComparison.Ordinal);
        }
        catch
        {
            return ResolvePinnedKeySample(channel);
        }
    }

    /// <summary>
    /// True when invoking the pinned-key formula gives the same key for any
    /// site in the channel's <c>Sites</c> list — implies the marketer's edits
    /// will land on every site simultaneously.
    /// </summary>
    private static bool IsPinnedKeySiteShared(SearchChannel channel)
    {
        if (channel.PinnedKeyForLocale == null) return false;
        if (channel.Sites == null || channel.Sites.Count <= 1) return false;
        try
        {
            var locale = channel.Locales?.FirstOrDefault() ?? "en";
            var first = channel.PinnedKeyForLocale(locale);
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

    // Strips a Language: { Name: { eq: "$locale" } } clause (with optional
    // trailing comma) from a query's `_and` list. Used when the preview API
    // is called without a locale — the registered query carries `$locale` so
    // the storefront's per-branch scoping shows up in the admin's
    // representative document, but the preview shouldn't 400 when no picker
    // value is supplied.
    private static readonly Regex LocalePlaceholderClauseRegex = new(
        @"\{\s*Language\s*:\s*\{\s*Name\s*:\s*\{\s*eq\s*:\s*""\$locale""\s*\}\s*\}\s*\}\s*,?",
        RegexOptions.Compiled);

    // Detects opt-in to Graph's synonym pool — the `synonyms: ONE|TWO` argument
    // inside an `_fulltext` clause. Without it, Graph silently bypasses the
    // synonym index even when rules are stored under the matching language. We
    // accept either slot enum so a channel that targets the staging slot still
    // reads as "applies synonyms".
    private static readonly Regex SynonymsArgRegex = new(@"\bsynonyms\s*:\s*(ONE|TWO)\b", RegexOptions.Compiled);

    /// <summary>
    /// Runs the registered channel's GraphQL document against Graph after
    /// substituting the runtime placeholders the addon's representative form
    /// uses (<c>"$phrase"</c>, <c>"$pinnedCollectionId"</c>). Returns null
    /// when the channel is unknown or carries no inline document.
    /// </summary>
    /// <remarks>
    /// This is the Channels tab's preview endpoint — it deliberately does NOT
    /// look at <c>SavedQueries.DefaultQuery</c>, so a tenant-specific runner
    /// query configured at the host level can't pollute other channels' Try-it.
    /// </remarks>
    public async Task<RunnerResult?> RunPreviewAsync(string channelKey, string phrase, string? locale, CancellationToken cancellationToken)
    {
        var channel = _registry.Get(channelKey);
        if (channel == null) return null;
        var template = channel.GraphQLDocumentContent;
        if (string.IsNullOrWhiteSpace(template)) return null;
        if (string.IsNullOrWhiteSpace(phrase)) return new RunnerResult(0, 0, template, Array.Empty<RunnerHit>());

        var query = template.Replace("\"$phrase\"", JsonSerializer.Serialize(phrase));

        // Substitute the locale placeholder. The Alloy storefront emits a
        // `Language: { Name: { eq: "$locale" } }` clause so the registered
        // query reflects the per-branch scoping the SERP applies; the picker
        // on the Channel detail drives this same value at preview time.
        // Empty / missing locale → strip the entire clause so Graph doesn't
        // receive a literal "$locale" or filter on the empty string.
        if (!string.IsNullOrEmpty(locale))
        {
            query = query.Replace("\"$locale\"", JsonSerializer.Serialize(locale));
        }
        else
        {
            query = LocalePlaceholderClauseRegex.Replace(query, string.Empty);
        }

        // Resolve the pinned collection id for this channel + locale, if any.
        // Missing or empty → strip the usePinned directive so Graph doesn't
        // see a literal "$pinnedCollectionId" string or an empty id.
        string? collectionId = null;
        if (channel.PinnedKeyForLocale != null)
        {
            try
            {
                var pinnedKey = channel.PinnedKeyForLocale(locale ?? string.Empty);
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

        // Graph keys its query cache by the raw JSON bytes of the request — two
        // logically-identical queries with the same bytes share a cache entry.
        // For the admin preview that's the wrong default: a marketer who just
        // saved a pin or synonym re-runs the same phrase and expects to see the
        // edit reflected, but Graph happily serves the pre-edit response from
        // cache (we've seen empty results persist for minutes even though the
        // pin lives in the collection). Appending a per-call nonce comment
        // forces a cache miss so every preview hits live data. The comment is
        // a no-op for GraphQL semantics — it changes only the request bytes.
        query += "\n# preview-nonce-" + Guid.NewGuid().ToString("N");

        var result = await _runner.RunRawAsync(query, variables: null, cancellationToken);

        // Graph's `usePinned` argument bypasses the language clause in the
        // where filter — a pin in collection "alloy-en" surfaces in BOTH the
        // en AND sv response branches when the preview asks for locale=en
        // (probed empirically against cg.optimizely.com). Drop hits whose
        // Language.Name doesn't match the requested locale so the SERP shows
        // a single branch even when pinned items spill across locales.
        if (!string.IsNullOrEmpty(locale) && result.Hits.Count > 0)
        {
            var filtered = result.Hits
                .Where(h => string.IsNullOrEmpty(h.Language)
                    || string.Equals(h.Language, locale, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count != result.Hits.Count)
            {
                result = result with { Hits = filtered, TotalCount = filtered.Count };
            }
        }

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
