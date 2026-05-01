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
        }
    };
}
