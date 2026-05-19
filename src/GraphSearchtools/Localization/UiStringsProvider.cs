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
    /// Lookup for Pinned Result Coverage strings under
    /// /graphsearchtools/tools/pinnedCoverage/*. The Pinned Coverage JS reads
    /// the kind-badge labels, generated-at prefix, fix-link button, and empty/
    /// load-failed copy from this section.
    /// </summary>
    private string PC(string key) => loc.GetString($"/graphsearchtools/tools/pinnedCoverage/{key}");

    /// <summary>
    /// Lookup for Insights strings under /graphsearchtools/tools/insights/*.
    /// The Insights JS reads coverage stat-card labels (parameterised with
    /// %1), empty-state copy, and the load-failed message from this section.
    /// </summary>
    private string IN(string key) => loc.GetString($"/graphsearchtools/tools/insights/{key}");

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
            pagerStatus = S("shared/pagerStatus"),
            today = S("shared/today")
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
            no_sites = S("pinned/no_sites"),
            target_unresolved = S("pinned/target_unresolved"),
            items_count = S("pinned/items_count"),
            empty_grid = S("pinned/empty_grid"),
            load_failed = S("pinned/load_failed"),
            save_failed = S("pinned/save_failed"),
            save_progress = S("pinned/save_progress"),
            conflict = S("pinned/conflict"),
            target_pick = S("pinned/target_pick"),
            activity_unknown = S("pinned/activity_unknown")
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
            help_equivalent_text = S("synonyms/help_equivalent_text"),
            col_activity = S("synonyms/col_activity"),
            activity_unknown = S("synonyms/activity_unknown")
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
                activity = P("index/cols/activity"),
                lastEdited = P("index/cols/lastEdited")
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
                },
                pinned = new
                {
                    loading = P("detail/pinned/loading"),
                    colFilterPhrase = P("detail/pinned/colFilterPhrase"),
                    colFilterTarget = P("detail/pinned/colFilterTarget")
                },
                synonyms = new
                {
                    loading = P("detail/synonyms/loading"),
                    colFilterRule = P("detail/synonyms/colFilterRule"),
                    colFilterScope = P("detail/synonyms/colFilterScope")
                },
                insights = new
                {
                    topTitle = P("detail/insights/topTitle"),
                    zeroTitle = P("detail/insights/zeroTitle"),
                    lowCtrTitle = P("detail/insights/lowCtrTitle"),
                    countSuffix = P("detail/insights/countSuffix"),
                    empty = P("detail/insights/empty"),
                    emptyZero = P("detail/insights/emptyZero"),
                    emptyLowCtr = P("detail/insights/emptyLowCtr"),
                    loadFailed = P("detail/insights/loadFailed"),
                    showMore = P("detail/insights/showMore"),
                    actionPreview = P("detail/insights/actionPreview"),
                    actionPin = P("detail/insights/actionPin"),
                    actionPinDisabled = P("detail/insights/actionPinDisabled"),
                    actionSynonym = P("detail/insights/actionSynonym"),
                    actionSynonymDisabled = P("detail/insights/actionSynonymDisabled"),
                    pinPickerPlaceholder = P("detail/insights/pinPickerPlaceholder"),
                    pinUnavailable = P("detail/insights/pinUnavailable"),
                    pinLoading = P("detail/insights/pinLoading"),
                    synRulePlaceholder = P("detail/insights/synRulePlaceholder"),
                    synRuleTip = P("detail/insights/synRuleTip"),
                    synUnavailable = P("detail/insights/synUnavailable"),
                    editSave = P("detail/insights/editSave"),
                    editUpdate = P("detail/insights/editUpdate"),
                    editSaving = P("detail/insights/editSaving"),
                    editCancel = P("detail/insights/editCancel"),
                    editPinSaved = P("detail/insights/editPinSaved"),
                    editSynSaved = P("detail/insights/editSynSaved"),
                    editFailed = P("detail/insights/editFailed"),
                    ctrLabel = P("detail/insights/ctrLabel"),
                    hitsLabel = P("detail/insights/hitsLabel")
                }
            }
        },
        insights = new
        {
            kpi_searches = IN("kpi_searches"),
            kpi_ctr = IN("kpi_ctr"),
            kpi_zero = IN("kpi_zero"),
            kpi_window = IN("kpi_window"),
            kpi_days_ago = IN("kpi_days_ago"),
            kpi_tooltip = IN("kpi_tooltip"),
            empty_phrases = IN("empty_phrases"),
            empty_zero = IN("empty_zero"),
            empty_lowctr = IN("empty_lowctr"),
            load_failed = IN("load_failed"),
            hits_label = IN("hits_label"),
            ctr_label = IN("ctr_label"),
            show_more = IN("show_more"),
            open_profile = IN("open_profile"),
            phrase_filter_no_matches = IN("phrase_filter_no_matches")
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
    };
}
