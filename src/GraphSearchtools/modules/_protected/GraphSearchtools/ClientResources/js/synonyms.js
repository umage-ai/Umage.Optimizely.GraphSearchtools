/**
 * Graph Search Tools — Synonyms editor.
 *
 * One synonym blob per language plus a "Global" blob (no language). The list
 * format is one rule per line; replacement vs equivalent semantics are
 * encoded in the rule text by Optimizely Graph itself.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.synonyms) || {};
    var SYN_SLOT = 'one';
    var SYN_SOURCE = '';

    var alertBox = document.getElementById('gst-alert');
    var synGrid = document.getElementById('syn-grid');
    var synFilter = document.getElementById('syn-filter');
    var synLanguageFilter = document.getElementById('syn-language-filter');
    var saveButton = document.getElementById('syn-save');
    var addButton = document.getElementById('syn-add');

    var sites = [];
    var rows = [];
    var dirty = false;
    var currentLang = '';

    function ajax(url, opts) {
        opts = opts || {};
        var headers = { 'X-Requested-With': 'XMLHttpRequest' };
        if (opts.body) headers['Content-Type'] = 'application/json';
        return fetch(url, {
            method: opts.method || 'GET',
            headers: headers,
            credentials: 'same-origin',
            body: opts.body ? JSON.stringify(opts.body) : undefined
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.request_failed || 'Request failed';
                    try {
                        var parsed = t ? JSON.parse(t) : null;
                        if (parsed && parsed.message) msg = parsed.message;
                    } catch (_) { /* not JSON */ }
                    throw new Error(msg + ' (' + resp.status + ')');
                });
            }
            if (resp.status === 204) return null;
            return resp.json();
        });
    }

    function setAlert(message, isError) {
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

    function updateSaveButton() {
        saveButton.disabled = !dirty;
    }

    function renderLanguageFilter() {
        synLanguageFilter.innerHTML = '';
        var optGlobal = document.createElement('option');
        optGlobal.value = '';
        optGlobal.textContent = STRINGS.global || 'Global';
        synLanguageFilter.appendChild(optGlobal);
        sites.forEach(function (site) {
            var opt = document.createElement('option');
            opt.value = site.languageCode;
            opt.textContent = site.title + ' (' + site.languageCode + ')';
            synLanguageFilter.appendChild(opt);
        });
    }

    function loadSitesAndInitial() {
        ajax(BASE + '/SitesApi/List')
            .then(function (s) {
                sites = s || [];
                renderLanguageFilter();
                return loadForLanguage('');
            })
            .catch(function (err) { setAlert(err.message, true); });
    }

    function loadForLanguage(lang) {
        rows = [];
        var qs = lang
            ? '?languageRouting=' + encodeURIComponent(lang) + '&slot=' + encodeURIComponent(SYN_SLOT)
            : '?slot=' + encodeURIComponent(SYN_SLOT);
        return ajax(BASE + '/SynonymsApi/Get' + qs)
            .then(function (result) {
                var content = result ? result.content : '';
                if (content) {
                    if (content.charAt(0) === '"' && content.charAt(content.length - 1) === '"') {
                        try { content = JSON.parse(content); } catch (_) { /* leave as-is */ }
                    }
                    content.split(/\r?\n/).forEach(function (line) {
                        var rule = line.trim();
                        if (!rule) return;
                        rows.push({ rule: rule, dirty: false, isNew: false });
                    });
                }
            })
            .catch(function () { /* no synonyms is fine */ })
            .then(function () {
                currentLang = lang;
                dirty = false;
                updateSaveButton();
                renderGrid();
            });
    }

    function getFilteredRows() {
        var text = synFilter.value.trim().toLowerCase();
        var filtered = rows;
        if (text) filtered = filtered.filter(function (r) { return r.rule.toLowerCase().indexOf(text) !== -1; });
        return filtered.filter(function (r) { return r.isNew || (r.rule && r.rule.trim().length > 0); });
    }

    function renderGrid() {
        synGrid.innerHTML = '';
        var filtered = getFilteredRows();
        filtered.forEach(function (row) { synGrid.appendChild(buildRow(row)); });
    }

    function buildRow(row) {
        var tr = document.createElement('tr');
        tr.className = 'gst-syn-row' + (row.dirty ? ' is-dirty' : '') + (row.isNew ? ' is-new' : '');

        var ruleCell = document.createElement('td');
        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'gst-cell-input';
        input.value = row.rule;
        input.placeholder = STRINGS.rule_placeholder || 'H2O => water  or  laptop, computer, pc';
        input.addEventListener('input', function () {
            row.rule = input.value;
            row.dirty = true;
            dirty = true;
            tr.classList.add('is-dirty');
            updateSaveButton();
        });
        ruleCell.appendChild(input);
        tr.appendChild(ruleCell);

        var actCell = document.createElement('td');
        actCell.className = 'gst-syn-actions';
        var del = document.createElement('button');
        del.type = 'button';
        del.className = 'gst-pin-delete-btn';
        del.innerHTML = '&#x2716;';
        del.title = STRINGS.action_remove || 'Remove';
        del.addEventListener('click', function () {
            rows = rows.filter(function (r) { return r !== row; });
            dirty = true;
            updateSaveButton();
            renderGrid();
        });
        actCell.appendChild(del);
        tr.appendChild(actCell);
        return tr;
    }

    function addRow() {
        rows.push({ rule: '', dirty: true, isNew: true });
        dirty = true;
        updateSaveButton();
        renderGrid();
        var trs = synGrid.querySelectorAll('tr');
        var last = trs[trs.length - 1];
        if (last) {
            last.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            var input = last.querySelector('.gst-cell-input');
            if (input && input.focus) input.focus();
        }
    }

    function save() {
        var rules = rows.map(function (r) { return r.rule.trim(); }).filter(function (r) { return r; });
        var content = rules.join('\n');
        var lang = currentLang;
        var promise;
        if (content) {
            promise = ajax(BASE + '/SynonymsApi/Update', {
                method: 'PUT',
                body: {
                    content: content,
                    languageRouting: lang || null,
                    sourceRouting: SYN_SOURCE || null,
                    slot: SYN_SLOT
                }
            });
        } else {
            var qs = lang
                ? '?languageRouting=' + encodeURIComponent(lang) + '&slot=' + encodeURIComponent(SYN_SLOT)
                : '?slot=' + encodeURIComponent(SYN_SLOT);
            promise = ajax(BASE + '/SynonymsApi/Delete' + qs, { method: 'DELETE' });
        }
        promise.then(function () {
            dirty = false;
            rows = rows.filter(function (r) { return r.rule.trim(); });
            rows.forEach(function (r) { r.dirty = false; r.isNew = false; });
            updateSaveButton();
            renderGrid();
            setAlert(STRINGS.saved || 'Synonyms saved.');
        }).catch(function (err) { setAlert(err.message, true); });
    }

    function handleLanguageChange() {
        var newLang = synLanguageFilter.value;
        if (newLang === currentLang) return;
        if (dirty) {
            var saveFirst = confirm(STRINGS.confirm_unsaved || 'You have unsaved synonym changes. Press OK to save, or Cancel to discard.');
            var promise = saveFirst ? new Promise(function (resolve) { save(); resolve(); }) : Promise.resolve();
            promise.then(function () { loadForLanguage(newLang); });
            return;
        }
        loadForLanguage(newLang);
    }

    addButton.addEventListener('click', addRow);
    saveButton.addEventListener('click', save);
    synFilter.addEventListener('input', renderGrid);
    synLanguageFilter.addEventListener('change', handleLanguageChange);

    window.addEventListener('beforeunload', function (e) {
        if (dirty) e.preventDefault();
    });

    updateSaveButton();
    loadSitesAndInitial();
})();
