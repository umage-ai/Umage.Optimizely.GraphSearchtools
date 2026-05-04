using EPiServer.Framework.Localization;

namespace UmageAI.Optimizely.GraphSearchTools.Localization;

/// <summary>
/// Provides all JavaScript UI strings from the localization service,
/// serialized to window.GST_STRINGS in the layout.
/// </summary>
public class UiStringsProvider(LocalizationService loc)
{
    private string S(string key) => loc.GetString($"/graphsearchtools/ui/{key}");

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
            confirm_unsaved = S("synonyms/confirm_unsaved")
        },
        connectivity = new
        {
            request_failed = S("connectivity/request_failed"),
            probing = S("connectivity/probing"),
            gateway = S("connectivity/gateway"),
            gateway_unset = S("connectivity/gateway_unset"),
            checked_at = S("connectivity/checked_at"),
            overall_green = S("connectivity/overall_green"),
            overall_amber = S("connectivity/overall_amber"),
            overall_red = S("connectivity/overall_red"),
            overall_unknown = S("connectivity/overall_unknown")
        },
        autocomplete = new
        {
            request_failed = S("autocomplete/request_failed"),
            schema_failed = S("autocomplete/schema_failed"),
            no_results = S("autocomplete/no_results"),
            no_types = S("autocomplete/no_types"),
            no_fields = S("autocomplete/no_fields"),
            any_locale = S("autocomplete/any_locale"),
            returned_for = S("autocomplete/returned_for"),
            error_pick_type_and_field = S("autocomplete/error_pick_type_and_field")
        },
        searchconsole = new
        {
            request_failed = S("searchconsole/request_failed"),
            running = S("searchconsole/running"),
            any_locale = S("searchconsole/any_locale"),
            no_results = S("searchconsole/no_results"),
            returned_for = S("searchconsole/returned_for"),
            error_query_required = S("searchconsole/error_query_required"),
            show_query = S("searchconsole/show_query"),
            hide_query = S("searchconsole/hide_query")
        },
        savedqueries = new
        {
            request_failed = S("savedqueries/request_failed"),
            no_items = S("savedqueries/no_items"),
            no_filter_match = S("savedqueries/no_filter_match"),
            any_locale = S("savedqueries/any_locale"),
            action_run = S("savedqueries/action_run"),
            action_edit = S("savedqueries/action_edit"),
            action_delete = S("savedqueries/action_delete"),
            new_title = S("savedqueries/new_title"),
            edit_title = S("savedqueries/edit_title"),
            error_name_required = S("savedqueries/error_name_required"),
            created = S("savedqueries/created"),
            updated = S("savedqueries/updated"),
            deleted = S("savedqueries/deleted"),
            confirm_delete = S("savedqueries/confirm_delete")
        }
    };
}
