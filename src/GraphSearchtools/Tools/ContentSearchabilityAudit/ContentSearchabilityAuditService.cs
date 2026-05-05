using System.Diagnostics;
using System.Reflection;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.Web;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Tools.ContentSearchabilityAudit.Models;

#pragma warning disable CS0618 // ISiteDefinitionRepository (CMS 12 name) — see HealthScanService for the same accommodation.

namespace UmageAI.Optimizely.GraphSearchTools.Tools.ContentSearchabilityAudit;

/// <summary>
/// Phase 4 — runs a local CMS scan looking for content quality issues that
/// degrade Graph relevance: missing Name, empty MainBody-style body fields,
/// no tags, and string properties that exceed Graph's 1024-character sort
/// limit (per <c>docs/research/optimizely-graph-site-search.md</c> §3
/// caveat). The scan is on-demand only — it walks every published page under
/// every site root and reflects each property on the resolved content type,
/// so it's intentionally not on a timer.
/// </summary>
public sealed class ContentSearchabilityAuditService
{
    /// <summary>Field-length sort caveat from Optimizely Graph.</summary>
    internal const int SortLengthCeiling = 1024;

    /// <summary>
    /// Heuristic body-property names. Order matters — first match wins per
    /// content type, so the most specific names sit at the top.
    /// </summary>
    private static readonly string[] BodyPropertyNames =
        ["MainBody", "Body", "BodyText", "Content", "MainContent", "Description"];

    /// <summary>
    /// Heuristic tag-property names — same matching rule as
    /// <see cref="BodyPropertyNames"/>.
    /// </summary>
    private static readonly string[] TagPropertyNames =
        ["Tags", "Categories", "Keywords"];

    private readonly IContentLoader _contentLoader;
    private readonly IContentTypeRepository _contentTypeRepository;
    private readonly ISiteDefinitionRepository _siteDefinitions;
    private readonly ILogger<ContentSearchabilityAuditService> _logger;

    public ContentSearchabilityAuditService(
        IContentLoader contentLoader,
        IContentTypeRepository contentTypeRepository,
        ISiteDefinitionRepository siteDefinitions,
        ILogger<ContentSearchabilityAuditService> logger)
    {
        _contentLoader = contentLoader;
        _contentTypeRepository = contentTypeRepository;
        _siteDefinitions = siteDefinitions;
        _logger = logger;
    }

    /// <summary>
    /// Walks every published page under every site root and runs the four
    /// quality probes. Cancellation is honoured between items so the caller
    /// (HTTP request) can abort a long scan when the user navigates away.
    /// </summary>
    public AuditResult Run(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var byKind = new Dictionary<string, List<AuditIssue>>(StringComparer.Ordinal)
        {
            [AuditIssueKinds.MissingName] = new(),
            [AuditIssueKinds.MissingMainBody] = new(),
            [AuditIssueKinds.NoTags] = new(),
            [AuditIssueKinds.OversizeSortField] = new()
        };

        var typeMetadataCache = new Dictionary<int, TypeMetadata>();
        var truncated = false;
        var scanned = 0;

        try
        {
            foreach (var content in EnumeratePublishedContent())
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;

                var meta = ResolveTypeMetadata(content, typeMetadataCache);
                EvaluateContent(content, meta, byKind, ref truncated);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Per-item failures (broken content, missing properties) shouldn't
            // crash the whole audit — log and surface what we have so far.
            _logger.LogWarning(ex, "Content searchability audit aborted mid-scan; returning partial results.");
        }

        sw.Stop();

        var allIssues = new List<AuditIssue>(
            byKind.Values.Sum(list => list.Count));
        // Stable, predictable ordering: kind in the constants' declared order,
        // then by name. The view groups by Kind anyway but stable order keeps
        // tests deterministic and the "click-row" deep-link list reproducible.
        foreach (var kind in new[]
        {
            AuditIssueKinds.MissingName,
            AuditIssueKinds.MissingMainBody,
            AuditIssueKinds.NoTags,
            AuditIssueKinds.OversizeSortField
        })
        {
            allIssues.AddRange(byKind[kind].OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase));
        }

        return new AuditResult
        {
            ScannedAt = DateTime.UtcNow,
            ElapsedMs = sw.ElapsedMilliseconds,
            ItemsScanned = scanned,
            Truncated = truncated,
            Issues = allIssues
        };
    }

    /// <summary>
    /// Runs the four probes against a single content item. Adds at most one
    /// issue per kind per item, with the OversizeSortField probe expanded so
    /// each oversized field becomes its own row (otherwise the editor can't
    /// tell which field tripped the rule).
    /// </summary>
    private static void EvaluateContent(
        IContent content,
        TypeMetadata meta,
        Dictionary<string, List<AuditIssue>> byKind,
        ref bool truncated)
    {
        var contentTypeName = meta.DisplayName;
        var locale = (content as ILocalizable)?.Language?.Name ?? string.Empty;
        var contentLinkId = content.ContentLink?.ID ?? 0;
        var contentGuid = content.ContentGuid.ToString();
        var name = content.Name ?? string.Empty;

        // 1. Missing Name. We treat whitespace-only as missing too — Graph
        //    doesn't show those usefully and most editors don't realise the
        //    field is blank.
        if (string.IsNullOrWhiteSpace(name))
        {
            AddCapped(byKind, AuditIssueKinds.MissingName, ref truncated, () => new AuditIssue
            {
                Kind = AuditIssueKinds.MissingName,
                ContentLink = contentLinkId,
                ContentGuid = contentGuid,
                Name = "(no name)",
                ContentType = contentTypeName,
                Locale = locale,
                Detail = "Page has no Name set."
            });
        }

        // 2. MainBody-like body field present on the type but empty.
        if (meta.BodyPropertyName != null)
        {
            var raw = TryGetPropertyValue(content, meta.BodyPropertyName);
            if (IsEmptyBody(raw))
            {
                AddCapped(byKind, AuditIssueKinds.MissingMainBody, ref truncated, () => new AuditIssue
                {
                    Kind = AuditIssueKinds.MissingMainBody,
                    ContentLink = contentLinkId,
                    ContentGuid = contentGuid,
                    Name = string.IsNullOrWhiteSpace(name) ? "(no name)" : name,
                    ContentType = contentTypeName,
                    Locale = locale,
                    Detail = $"'{meta.BodyPropertyName}' is empty."
                });
            }
        }

        // 3. Tag-like property present on the type but empty.
        if (meta.TagPropertyName != null)
        {
            var raw = TryGetPropertyValue(content, meta.TagPropertyName);
            if (IsEmptyTags(raw))
            {
                AddCapped(byKind, AuditIssueKinds.NoTags, ref truncated, () => new AuditIssue
                {
                    Kind = AuditIssueKinds.NoTags,
                    ContentLink = contentLinkId,
                    ContentGuid = contentGuid,
                    Name = string.IsNullOrWhiteSpace(name) ? "(no name)" : name,
                    ContentType = contentTypeName,
                    Locale = locale,
                    Detail = $"'{meta.TagPropertyName}' has no values."
                });
            }
        }

        // 4. Searchable string properties that exceed Graph's 1024-char
        //    sortable limit. We flag the property name + actual length so the
        //    editor can decide between trimming, splitting, or marking the
        //    field unsearchable.
        foreach (var prop in meta.SearchableStringProperties)
        {
            var raw = TryGetPropertyValue(content, prop);
            if (raw is string s && s.Length > SortLengthCeiling)
            {
                AddCapped(byKind, AuditIssueKinds.OversizeSortField, ref truncated, () => new AuditIssue
                {
                    Kind = AuditIssueKinds.OversizeSortField,
                    ContentLink = contentLinkId,
                    ContentGuid = contentGuid,
                    Name = string.IsNullOrWhiteSpace(name) ? "(no name)" : name,
                    ContentType = contentTypeName,
                    Locale = locale,
                    Detail = $"'{prop}' is {s.Length} chars (sort limit {SortLengthCeiling})."
                });
            }
        }
    }

    private static void AddCapped(
        Dictionary<string, List<AuditIssue>> byKind,
        string kind,
        ref bool truncated,
        Func<AuditIssue> factory)
    {
        var list = byKind[kind];
        if (list.Count >= AuditResult.ItemCap)
        {
            truncated = true;
            return;
        }
        list.Add(factory());
    }

    /// <summary>
    /// Cheap check for "the body has no usable text". Strings checked for
    /// whitespace; XhtmlString uses <c>IsEmpty</c>; everything else is treated
    /// as non-empty when not null (we only flag what we can be sure about).
    /// </summary>
    private static bool IsEmptyBody(object? raw)
    {
        if (raw == null) return true;
        if (raw is string s) return string.IsNullOrWhiteSpace(s);
        if (raw is XhtmlString xhtml) return xhtml.IsEmpty;
        return false;
    }

    private static bool IsEmptyTags(object? raw)
    {
        if (raw == null) return true;
        if (raw is string s) return string.IsNullOrWhiteSpace(s);
        if (raw is System.Collections.IEnumerable enumerable)
        {
            foreach (var _ in enumerable) return false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reads <paramref name="propertyName"/> off the content's property bag.
    /// Returns null on miss — the caller decides what missing means per kind.
    /// </summary>
    private static object? TryGetPropertyValue(IContent content, string propertyName)
    {
        try
        {
            if (content is IContentData data)
            {
                var prop = data.Property[propertyName];
                if (prop != null) return prop.Value;
            }
        }
        catch
        {
            // Property bag access can throw on dynamically-typed pages —
            // fall through to reflection below.
        }

        try
        {
            var prop = content.GetOriginalType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanRead) return prop.GetValue(content);
        }
        catch
        {
            // Best-effort only — a misbehaving property accessor shouldn't
            // crash the entire scan.
        }

        return null;
    }

    private TypeMetadata ResolveTypeMetadata(IContent content, Dictionary<int, TypeMetadata> cache)
    {
        var typeId = content.ContentTypeID;
        if (cache.TryGetValue(typeId, out var cached)) return cached;

        var contentType = _contentTypeRepository.Load(typeId);
        var modelType = contentType?.ModelType ?? content.GetOriginalType();

        var displayName = contentType?.DisplayName ?? contentType?.Name ?? modelType.Name;
        var bodyName = ResolveBodyPropertyName(modelType);
        var tagsName = ResolveTagsPropertyName(modelType);
        var searchable = ResolveSearchableStringProperties(modelType, contentType);

        var meta = new TypeMetadata(displayName, bodyName, tagsName, searchable);
        cache[typeId] = meta;
        return meta;
    }

    private static string? ResolveBodyPropertyName(Type modelType) =>
        BodyPropertyNames.FirstOrDefault(name =>
            modelType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) != null);

    private static string? ResolveTagsPropertyName(Type modelType) =>
        TagPropertyNames.FirstOrDefault(name =>
            modelType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) != null);

    /// <summary>
    /// Picks every string property that's marked searchable — either through
    /// <c>[Searchable]</c> on the CLR property, or <c>PropertyDefinition.Searchable</c>
    /// on the registered content type (which is what the CMS UI exposes).
    /// </summary>
    private static IReadOnlyList<string> ResolveSearchableStringProperties(
        Type modelType, ContentType? contentType)
    {
        var hits = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // CLR-attribute path — picks up [Searchable] declared on the model.
        foreach (var prop in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(string)) continue;
            var attr = prop.GetCustomAttribute<SearchableAttribute>(inherit: true);
            if (attr is { IsSearchable: true } && seen.Add(prop.Name))
            {
                hits.Add(prop.Name);
            }
        }

        // PropertyDefinition path — picks up the runtime/admin-toggle setting,
        // which can override the CLR attribute. Only string properties qualify
        // for the sort-length warning.
        if (contentType?.PropertyDefinitions != null)
        {
            foreach (var def in contentType.PropertyDefinitions)
            {
                if (!def.Searchable) continue;
                var name = def.Name;
                if (string.IsNullOrEmpty(name)) continue;
                var clrProp = modelType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (clrProp == null || clrProp.PropertyType != typeof(string)) continue;
                if (seen.Add(name)) hits.Add(name);
            }
        }

        return hits;
    }

    private IEnumerable<IContent> EnumeratePublishedContent()
    {
        var seen = new HashSet<int>();
        foreach (var site in _siteDefinitions.List())
        {
            if (ContentReference.IsNullOrEmpty(site.StartPage)) continue;
            // Include the site root itself; GetDescendents skips it.
            if (seen.Add(site.StartPage.ID)
                && _contentLoader.TryGet<IContent>(site.StartPage, out var root)
                && IsPublished(root))
            {
                yield return root;
            }
            foreach (var link in _contentLoader.GetDescendents(site.StartPage))
            {
                if (!seen.Add(link.ID)) continue;
                if (!_contentLoader.TryGet<IContent>(link, out var content)) continue;
                if (!IsPublished(content)) continue;
                yield return content;
            }
        }
    }

    private static bool IsPublished(IContent content)
    {
        if (content is IVersionable v) return v.Status == VersionStatus.Published;
        return true;
    }

    private sealed record TypeMetadata(
        string DisplayName,
        string? BodyPropertyName,
        string? TagPropertyName,
        IReadOnlyList<string> SearchableStringProperties);
}
