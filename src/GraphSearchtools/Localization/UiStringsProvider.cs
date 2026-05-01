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
        }
    };
}
