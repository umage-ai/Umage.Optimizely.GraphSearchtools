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
            next = S("shared/next")
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
            add = S("synonyms/add"),
            col_rule = S("synonyms/col_rule"),
            save = S("synonyms/save"),
            help_replacement_label = S("synonyms/help_replacement_label"),
            help_replacement_text = S("synonyms/help_replacement_text"),
            help_equivalent_label = S("synonyms/help_equivalent_label"),
            help_equivalent_text = S("synonyms/help_equivalent_text"),
            profile_locale = S("synonyms/profile_locale")
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
        savedqueries = new
        {
            request_failed = S("savedqueries/request_failed"),
            no_items = S("savedqueries/no_items"),
            no_filter_match = S("savedqueries/no_filter_match"),
            all_locales = S("savedqueries/all_locales"),
            action_load = S("savedqueries/action_load"),
            action_run = S("savedqueries/action_run"),
            action_edit = S("savedqueries/action_edit"),
            action_delete = S("savedqueries/action_delete"),
            new_title = S("savedqueries/new_title"),
            edit_title = S("savedqueries/edit_title"),
            error_name_required = S("savedqueries/error_name_required"),
            error_query_required = S("savedqueries/error_query_required"),
            created = S("savedqueries/created"),
            updated = S("savedqueries/updated"),
            deleted = S("savedqueries/deleted"),
            confirm_delete = S("savedqueries/confirm_delete"),
            run_failed = S("savedqueries/run_failed"),
            running = S("savedqueries/running"),
            no_results = S("savedqueries/no_results"),
            returned_for = S("savedqueries/returned_for"),
            show_query = S("savedqueries/show_query"),
            hide_query = S("savedqueries/hide_query")
        }
    };
}
