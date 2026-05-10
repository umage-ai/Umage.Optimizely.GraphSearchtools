using EPiServer.Framework.Localization;

namespace UmageAI.Optimizely.GraphSearchTools.Localization;

/// <summary>
/// Provides all JavaScript UI strings from the localization service,
/// serialized to window.GST_STRINGS in the layout.
/// </summary>
public class UiStringsProvider(LocalizationService loc)
{
    private string S(string key) => loc.GetString($"/graphsearchtools/ui/{key}");

    /// <summary>
    /// Lookup outside the /ui/* tree — used for top-level Profile loc keys at
    /// /graphsearchtools/profiles/* that the Profiles JS reads via GST.s().
    /// </summary>
    private string P(string key) => loc.GetString($"/graphsearchtools/profiles/{key}");

    /// <summary>
    /// Lookup for Webhooks strings under /graphsearchtools/tools/webhooks/*.
    /// Webhooks JS reads inline confirm/empty/save messages from this section.
    /// </summary>
    private string W(string key) => loc.GetString($"/graphsearchtools/tools/webhooks/{key}");

    /// <summary>
    /// Lookup for Semantic Weight Tuner strings under
    /// /graphsearchtools/tools/semanticTuner/*. The page is a thin client; all
    /// labels and toast messages route through this section.
    /// </summary>
    private string ST(string key) => loc.GetString($"/graphsearchtools/tools/semanticTuner/{key}");

    /// <summary>
    /// Lookup for Search Logs strings under /graphsearchtools/tools/searchLogs/*.
    /// The Search Logs JS reads inline empty-card / status-badge / link / load-
    /// failed messages from this section.
    /// </summary>
    private string SL(string key) => loc.GetString($"/graphsearchtools/tools/searchLogs/{key}");

    /// <summary>
    /// Lookup for Pinned Result Coverage strings under
    /// /graphsearchtools/tools/pinnedCoverage/*. The Pinned Coverage JS reads
    /// the kind-badge labels, generated-at prefix, fix-link button, and empty/
    /// load-failed copy from this section.
    /// </summary>
    private string PC(string key) => loc.GetString($"/graphsearchtools/tools/pinnedCoverage/{key}");

    /// <summary>
    /// Lookup for Synonym Coverage strings under
    /// /graphsearchtools/tools/synonymCoverage/*. The Synonym Coverage JS reads
    /// the generated-at prefix, prune/add link labels, and empty / no-logs /
    /// load-failed copy from this section.
    /// </summary>
    private string SC(string key) => loc.GetString($"/graphsearchtools/tools/synonymCoverage/{key}");

    /// <summary>
    /// Lookup for Content Searchability Audit strings under
    /// /graphsearchtools/tools/contentSearchabilityAudit/*. The audit JS reads
    /// the kind labels, stat labels, run/running button text, table column
    /// headers, and empty / run-failed copy from this section.
    /// </summary>
    private string CSA(string key) => loc.GetString($"/graphsearchtools/tools/contentSearchabilityAudit/{key}");

    public object GetAll() => new
    {
        shared = new
        {
            loading = S("shared/loading"),
            noresults = S("shared/noresults"),
            cancel = S("shared/cancel"),
            apply = S("shared/apply"),
            close = S("shared/close"),
            refresh = S("shared/refresh"),
            save = S("shared/save"),
            delete = S("shared/delete"),
            edit = S("shared/edit"),
            failed = S("shared/failed"),
            yes = S("shared/yes"),
            no = S("shared/no"),
            open = S("shared/open"),
            all = S("shared/all"),
            prev = S("shared/prev"),
            next = S("shared/next"),
            copy = S("shared/copy"),
            copied = S("shared/copied"),
            pagerLabel = S("shared/pagerLabel"),
            pagerStatus = S("shared/pagerStatus")
        },
        components = new
        {
            picker_title = S("components/picker_title"),
            picker_search = S("components/picker_search"),
            picker_noresults = S("components/picker_noresults"),
            btn_cancel = S("components/btn_cancel"),
            btn_select = S("components/btn_select"),
            typepicker_title = S("components/typepicker_title"),
            typepicker_search = S("components/typepicker_search"),
            typepicker_noresults = S("components/typepicker_noresults")
        },
        graphsearchtools = new
        {
            loading = S("graphsearchtools/loading"),
            noresults = S("graphsearchtools/noresults")
        },
        pinned = new
        {
            request_failed = S("pinned/request_failed"),
            all_sites = S("pinned/all_sites"),
            action_save = S("pinned/action_save"),
            action_delete = S("pinned/action_delete"),
            search_placeholder = S("pinned/search_placeholder"),
            error_phrase_and_content_required = S("pinned/error_phrase_and_content_required"),
            created = S("pinned/created"),
            updated = S("pinned/updated"),
            deleted = S("pinned/deleted"),
            confirm_delete = S("pinned/confirm_delete"),
            no_sites = S("pinned/no_sites")
        },
        synonyms = new
        {
            request_failed = S("synonyms/request_failed"),
            global = S("synonyms/global"),
            action_remove = S("synonyms/action_remove"),
            rule_placeholder = S("synonyms/rule_placeholder"),
            saved = S("synonyms/saved"),
            confirm_unsaved = S("synonyms/confirm_unsaved"),
            filter_placeholder = S("synonyms/filter_placeholder"),
            placeholder_hint = S("synonyms/placeholder_hint"),
            add = S("synonyms/add"),
            col_rule = S("synonyms/col_rule"),
            save = S("synonyms/save"),
            help_replacement_label = S("synonyms/help_replacement_label"),
            help_replacement_text = S("synonyms/help_replacement_text"),
            help_equivalent_label = S("synonyms/help_equivalent_label"),
            help_equivalent_text = S("synonyms/help_equivalent_text")
        },
        health = new
        {
            request_failed = S("health/request_failed"),
            probing = S("health/probing"),
            gateway_unset = S("health/gateway_unset"),
            checked_just_now = S("health/checked_just_now"),
            checked_seconds_ago = S("health/checked_seconds_ago"),
            checked_minutes_ago = S("health/checked_minutes_ago"),
            status_healthy = S("health/status_healthy"),
            status_degraded = S("health/status_degraded"),
            status_down = S("health/status_down"),
            status_unknown = S("health/status_unknown"),
            status_running = S("health/status_running"),
            subtitle_healthy = S("health/subtitle_healthy"),
            subtitle_degraded = S("health/subtitle_degraded"),
            subtitle_down = S("health/subtitle_down"),
            subtitle_unknown = S("health/subtitle_unknown"),
            auto_enabled = S("health/auto_enabled"),
            auto_disabled = S("health/auto_disabled"),
            last_scan_at = S("health/last_scan_at"),
            last_scan_never = S("health/last_scan_never"),
            bar_tooltip = S("health/bar_tooltip")
        },
        autocomplete = new
        {
            request_failed = S("autocomplete/request_failed"),
            schema_failed = S("autocomplete/schema_failed"),
            no_results = S("autocomplete/no_results"),
            no_types = S("autocomplete/no_types"),
            no_fields = S("autocomplete/no_fields"),
            all_locales = S("autocomplete/all_locales"),
            returned_for = S("autocomplete/returned_for"),
            error_pick_type_and_field = S("autocomplete/error_pick_type_and_field")
        },
        // Phase 2.5 — Search Profiles. Reads from the top-level
        // /graphsearchtools/profiles/* tree rather than /ui/* so the loc paths
        // line up with the design doc and stay grouped near the foundation
        // agent's profile-builder strings.
        profiles = new
        {
            requestFailed = P("requestFailed"),
            cols = new
            {
                profile = P("index/cols/profile"),
                scope = P("index/cols/scope"),
                tuning = P("index/cols/tuning"),
                status = P("index/cols/status"),
                lastEdited = P("index/cols/lastEdited")
            },
            stats = new
            {
                profiles = P("index/stats/profiles"),
                pinned = P("index/stats/pinned"),
                synonyms = P("index/stats/synonyms"),
                coverage = P("index/stats/coverage"),
                sites = P("index/stats/sites"),
                locales = P("index/stats/locales")
            },
            status = new
            {
                tuned = P("status/tuned"),
                needsReview = P("status/needsReview"),
                docMissing = P("status/docMissing"),
                freeForm = P("status/freeForm"),
                cold = P("status/cold")
            },
            time = new
            {
                justNow = P("time/justNow"),
                minutesAgo = P("time/minutesAgo"),
                hoursAgo = P("time/hoursAgo"),
                daysAgo = P("time/daysAgo")
            },
            detail = new
            {
                comingSoon = P("detail/comingSoon"),
                sharedWarning = P("detail/sharedWarning"),
                meta = new
                {
                    allSites = P("detail/meta/allSites"),
                    allLocales = P("detail/meta/allLocales")
                },
                audit = new
                {
                    empty = P("detail/audit/empty"),
                    col = new
                    {
                        @when = P("detail/audit/col/when"),
                        who = P("detail/audit/col/who"),
                        kind = P("detail/audit/col/kind"),
                        action = P("detail/audit/col/action"),
                        subject = P("detail/audit/col/subject"),
                        locale = P("detail/audit/col/locale")
                    }
                }
            }
        },
        webhooks = new
        {
            empty = W("empty"),
            status_active = W("status_active"),
            status_disabled = W("status_disabled"),
            delete = W("delete"),
            delete_confirm = W("delete_confirm"),
            save_failed = W("save_failed"),
            delete_failed = W("delete_failed"),
            load_failed = W("load_failed")
        },
        semanticTuner = new
        {
            tier_min_tokens = ST("tier_min_tokens"),
            tier_max_tokens = ST("tier_max_tokens"),
            tier_max_unbounded = ST("tier_max_unbounded"),
            tier_ranking = ST("tier_ranking"),
            tier_weight = ST("tier_weight"),
            tier_description = ST("tier_description"),
            add_tier = ST("add_tier"),
            remove_tier = ST("remove_tier"),
            save = ST("save"),
            saved = ST("saved"),
            save_failed = ST("save_failed"),
            load_failed = ST("load_failed"),
            snippet_label = ST("snippet_label"),
            snippet_help = ST("snippet_help"),
            copy = ST("copy"),
            copied = ST("copied"),
            empty = ST("empty"),
            ranking_relevance = ST("ranking_relevance"),
            ranking_semantic = ST("ranking_semantic"),
            ranking_boostonly = ST("ranking_boostonly"),
            ranking_doc = ST("ranking_doc")
        },
        searchLogs = new
        {
            add_as_synonym = SL("add_as_synonym"),
            tune_pinned = SL("tune_pinned"),
            empty_title = SL("empty_title"),
            empty_body = SL("empty_body"),
            load_failed = SL("load_failed")
        },
        pinnedCoverage = new
        {
            generated_at = PC("generated_at"),
            run_audit = PC("run_audit"),
            issue_kind_unpublished = PC("issue_kind_unpublished"),
            issue_kind_deleted = PC("issue_kind_deleted"),
            issue_kind_expired = PC("issue_kind_expired"),
            issue_kind_low_ctr = PC("issue_kind_low_ctr"),
            issue_kind_no_activity = PC("issue_kind_no_activity"),
            col_kind = PC("col_kind"),
            col_phrase = PC("col_phrase"),
            col_target = PC("col_target"),
            col_collection = PC("col_collection"),
            col_detail = PC("col_detail"),
            fix_in_profile = PC("fix_in_profile"),
            overlaps_title = PC("overlaps_title"),
            overlap_phrase = PC("overlap_phrase"),
            overlap_collections = PC("overlap_collections"),
            empty = PC("empty"),
            load_failed = PC("load_failed")
        },
        synonymCoverage = new
        {
            generated_at = SC("generated_at"),
            prune_in_synonyms = SC("prune_in_synonyms"),
            add_in_synonyms = SC("add_in_synonyms"),
            empty = SC("empty"),
            no_logs = SC("no_logs"),
            load_failed = SC("load_failed")
        },
        contentAudit = new
        {
            run = CSA("run"),
            running = CSA("running"),
            scanned_at = CSA("scanned_at"),
            items_scanned = CSA("items_scanned"),
            issues_total = CSA("issues_total"),
            kind_missing_name = CSA("kind_missing_name"),
            kind_missing_main_body = CSA("kind_missing_main_body"),
            kind_no_tags = CSA("kind_no_tags"),
            kind_oversize_sort = CSA("kind_oversize_sort"),
            col_kind = CSA("col_kind"),
            col_name = CSA("col_name"),
            col_type = CSA("col_type"),
            col_detail = CSA("col_detail"),
            col_edit = CSA("col_edit"),
            edit = CSA("edit"),
            empty = CSA("empty"),
            load_failed = CSA("load_failed"),
            run_failed = CSA("run_failed")
        }
    };
}
