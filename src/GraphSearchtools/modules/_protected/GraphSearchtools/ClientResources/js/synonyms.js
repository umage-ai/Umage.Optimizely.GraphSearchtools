/**
 * Graph Search Tools — standalone Synonyms tool bootstrap.
 *
 * The grid widget itself lives in synonyms-grid.js (shared with the
 * Profile-detail Synonyms tab). This file just wires the standalone
 * surface's language picker (one blob at a time, no merge with global)
 * and populates the picker options from /SitesApi/Locales.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.synonyms) || {};

    var alertBox = document.getElementById('gst-alert');
    var langSelect = document.getElementById('syn-language-filter');

    function s(path, fallback) {
        if (window.GST && typeof window.GST.s === 'function') return window.GST.s(path, fallback);
        return fallback;
    }

    function setAlert(message, isError) {
        if (!alertBox) return;
        if (!message) {
            alertBox.hidden = true;
            alertBox.textContent = '';
            alertBox.classList.remove('gst-alert--danger');
            return;
        }
        alertBox.hidden = false;
        alertBox.textContent = message;
        alertBox.classList.toggle('gst-alert--danger', !!isError);
    }

    function renderLanguageOptions(locales) {
        langSelect.innerHTML = '';
        var optGlobal = document.createElement('option');
        optGlobal.value = '';
        optGlobal.textContent = STRINGS.global || 'Global';
        langSelect.appendChild(optGlobal);
        (locales || []).forEach(function (code) {
            var opt = document.createElement('option');
            opt.value = code;
            opt.textContent = code;
            langSelect.appendChild(opt);
        });
    }

    function mountGrid() {
        return GST.synonymsGrid.mount({
            slot: 'one',
            mergeWithGlobal: false,
            getLang: function () { return langSelect.value; },
            onLangChange: function (handler) {
                langSelect.addEventListener('change', function () { handler(langSelect.value); });
            },
            dom: {
                rowsHost: '#gst-syn-rows',
                emptyEl: '#gst-syn-empty',
                addBtn: '#gst-syn-add',
                addEmpty: '#gst-syn-empty-add',
                alertEl: '#gst-syn-alert',
                saveBtn: '#gst-syn-save',
                discardBtn: '#gst-syn-discard',
                filterInput: '#gst-syn-filter',
                countEl: '#gst-syn-count',
                pagerEl: '#gst-syn-pager',
                pagerStatusEl: '#gst-syn-pager-status',
                prevBtn: '#gst-syn-prev',
                nextBtn: '#gst-syn-next',
                drawerEl: '#gst-syn-drawer',
                drawerCount: '#gst-syn-dirty-count',
                sortBtns: document.querySelectorAll('#gst-syn .gst-pinedit__sortbtn')
            }
        });
    }

    // Pull locale options from Graph's schema introspection rather than the
    // host CMS site list — synonym slots in Graph are routed by Graph's own
    // locale codes, so the picker stays accurate when the two have drifted.
    fetch(BASE + '/SitesApi/Locales', {
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
        credentials: 'same-origin'
    })
        .then(function (r) { return r.ok ? r.json() : []; })
        .catch(function () { return []; })
        .then(function (locales) {
            renderLanguageOptions(locales);
            mountGrid();
        })
        .catch(function (err) { setAlert((err && err.message) || s('synonyms.request_failed', 'Failed to load locales.'), true); });
})();
