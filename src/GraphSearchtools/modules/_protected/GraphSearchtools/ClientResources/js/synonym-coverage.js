/**
 * Graph Search Tools — Synonym Coverage.
 *
 * Read-only analyzer surfaced as two cards:
 *   1. Unused synonyms — entries from the saved synonym blobs that didn't
 *      match any logged query in the rolling window. Each row deep-links to
 *      the Synonyms editor with the language pre-selected and the entry
 *      pre-filtered.
 *   2. Suggested adds — top zero-result phrases that look like missing
 *      synonyms. When the analyzer found a close indexed-term match it
 *      annotates the row with that hint; otherwise the row is the bare
 *      phrase + hit count (fall-back mode).
 *
 * The page calls a single endpoint (GET /SynonymCoverageApi/Index) that
 * already does the join in C#. Keeps the JS focussed on rendering + linking.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.synonymCoverage) || {};

    // The Synonyms editor is reached via the framework's resolved resource
    // URL. We pass the selected language + entry through the URL hash so the
    // editor can opt into pre-fill without us hard-coding a query-string
    // contract here. When this script runs as the Unused tab inside the
    // Synonyms page, the link navigates in-place (hash-only change) and the
    // tab-switch script in the Synonyms view picks up the prefill hash.
    var SYNONYMS_URL = (window.GST_BASE_URL || '') + '/GraphSearchtools/Synonyms';

    // ── DOM ──────────────────────────────────────────────────────
    var alertBox = document.getElementById('gst-alert');
    var refreshBtn = document.getElementById('sc-refresh');
    var generatedAt = document.getElementById('sc-generated-at');
    var unusedGrid = document.getElementById('sc-unused-grid');
    var suggestedGrid = document.getElementById('sc-suggested-grid');

    // ── Utilities ────────────────────────────────────────────────

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
                    var msg = STRINGS.load_failed || 'Request failed';
                    try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
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

    function escHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    /**
     * Build a deep-link to the Synonyms editor with hints in the hash. The
     * Synonyms page can ignore these on first load — fragment routing is a
     * progressive enhancement, not a contract.
     */
    function synonymsLink(language, prefill) {
        var params = [];
        if (language && language !== 'Global') params.push('language=' + encodeURIComponent(language));
        if (prefill) params.push('prefill=' + encodeURIComponent(prefill));
        return SYNONYMS_URL + (params.length ? '#' + params.join('&') : '');
    }

    // ── Render ───────────────────────────────────────────────────

    function renderUnused(rows) {
        unusedGrid.innerHTML = '';
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="4" class="gst-empty"><p>'
                + escHtml(STRINGS.empty || 'No prune candidates — every synonym entry fired at least once.')
                + '</p></td>';
            unusedGrid.appendChild(emptyTr);
            return;
        }

        rows.forEach(function (row) {
            var tr = document.createElement('tr');
            tr.className = 'gst-sc-row';
            tr.innerHTML =
                '<td>' + escHtml(row.language || '—') + '</td>' +
                '<td><code class="gst-mono">' + escHtml(row.entry) + '</code></td>' +
                '<td><span class="gst-rl-dim">' + escHtml(row.reason) + '</span></td>' +
                '<td class="gst-sc-actions-col">' +
                    '<button type="button" class="gst-btn gst-btn--sm gst-btn--danger" data-sc-delete>' +
                        escHtml(STRINGS.delete_entry || 'Delete') +
                    '</button>' +
                '</td>';
            var deleteBtn = tr.querySelector('[data-sc-delete]');
            deleteBtn.addEventListener('click', function () { deleteUnusedEntry(row, tr, deleteBtn); });
            unusedGrid.appendChild(tr);
        });
    }

    /**
     * Delete one synonym entry by removing its line from the relevant scope's
     * blob and PUTing the updated blob back. The Synonyms API doesn't have a
     * per-entry delete primitive (it works on whole blobs), so we do a
     * GET → splice → PUT round-trip.
     */
    function deleteUnusedEntry(row, tr, btn) {
        var label = STRINGS.confirm_delete_entry
            || 'Delete this synonym rule? This can\'t be undone.';
        if (!window.confirm(label)) return;

        setAlert('');
        btn.disabled = true;

        var isGlobal = !row.language || row.language === 'Global';
        var qs = isGlobal ? '' : '?languageRouting=' + encodeURIComponent(row.language);

        ajax(BASE + '/SynonymsApi/Get' + qs)
            .then(function (resp) {
                var content = (resp && resp.content) || '';
                var lines = content.split(/\r?\n/);
                var keep = lines.filter(function (line) {
                    return line.trim() && line.trim() !== row.entry.trim();
                });
                if (keep.length === lines.filter(function (l) { return l.trim(); }).length) {
                    // No line matched — entry might have been edited elsewhere since
                    // the audit ran. Still call refresh so the user sees the latest.
                    throw new Error(STRINGS.delete_not_found
                        || 'The rule was not found in the current synonym set. Refresh and try again.');
                }
                return ajax(BASE + '/SynonymsApi/Update', {
                    method: 'PUT',
                    body: {
                        content: keep.join('\n'),
                        languageRouting: isGlobal ? '' : row.language,
                        sourceRouting: null,
                        slot: 'one'
                    }
                });
            })
            .then(function () {
                tr.parentNode && tr.parentNode.removeChild(tr);
                if (unusedGrid.children.length === 0) {
                    // Show empty state when the last row goes.
                    renderUnused([]);
                }
            })
            .catch(function (err) {
                btn.disabled = false;
                setAlert((STRINGS.delete_failed || 'Could not delete rule.') + ' ' + err.message, true);
            });
    }

    function renderSuggested(rows) {
        suggestedGrid.innerHTML = '';
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="5" class="gst-empty"><p>'
                + escHtml(STRINGS.no_logs || 'No zero-result phrases in the window — nothing to suggest.')
                + '</p></td>';
            suggestedGrid.appendChild(emptyTr);
            return;
        }

        rows.forEach(function (row) {
            var closestCell = row.closestIndexedTerm
                ? '<code class="gst-mono">' + escHtml(row.closestIndexedTerm) + '</code>'
                : '<span class="gst-rl-dim">—</span>';
            var similarity = row.similarity != null ? row.similarity : '—';
            var locale = row.locale ? ' <span class="gst-rl-dim">(' + escHtml(row.locale) + ')</span>' : '';

            var tr = document.createElement('tr');
            tr.className = 'gst-sc-row';
            tr.innerHTML =
                '<td>' + escHtml(row.phrase) + locale + '</td>' +
                '<td class="num">' + escHtml(row.hits) + '</td>' +
                '<td>' + closestCell + '</td>' +
                '<td class="num">' + escHtml(similarity) + '</td>' +
                '<td class="gst-sc-actions-col">' +
                    '<button type="button" class="gst-btn gst-btn--sm" data-sc-add>' +
                        escHtml(STRINGS.add_entry || 'Add') +
                    '</button>' +
                '</td>';
            var addBtn = tr.querySelector('[data-sc-add]');
            addBtn.addEventListener('click', function () { toggleInlineSynEditor(row, tr, addBtn); });
            suggestedGrid.appendChild(tr);
        });
    }

    /**
     * Toggle an inline synonym editor as a sibling row beneath the suggested
     * phrase. Mirrors the Profile Insights pattern (buildInlineSynonymEditor
     * in profiles.js): single text input prefilled with `phrase => closest`,
     * Save calls into the live synonyms-grid handle (window.GST.synonymsEditor)
     * so the rule lands in the same blob the Rules tab is editing — no extra
     * tab switch, no second mount.
     */
    function toggleInlineSynEditor(row, rowTr, addBtn) {
        var next = rowTr.nextElementSibling;
        if (next && next.classList && next.classList.contains('gst-sc-edit-row')) {
            next.remove();
            addBtn.classList.remove('is-active');
            return;
        }
        // Close any other open editor — only one open at a time keeps the
        // table tidy and prevents stacking save buttons on top of each other.
        var openRow = suggestedGrid.querySelector('.gst-sc-edit-row');
        if (openRow) {
            var prevBtn = openRow.previousElementSibling
                && openRow.previousElementSibling.querySelector('[data-sc-add]');
            if (prevBtn) prevBtn.classList.remove('is-active');
            openRow.remove();
        }
        addBtn.classList.add('is-active');

        var ed = (window.GST && window.GST.synonymsEditor) || null;
        var editorTr = document.createElement('tr');
        editorTr.className = 'gst-sc-edit-row';
        var td = document.createElement('td');
        td.className = 'gst-sc-edit-cell';
        td.colSpan = 5;
        editorTr.appendChild(td);

        var panel = document.createElement('div');
        panel.className = 'gst-sc-edit';
        td.appendChild(panel);

        var ruleInput = document.createElement('input');
        ruleInput.type = 'text';
        ruleInput.className = 'gst-sc-edit__input';
        ruleInput.placeholder = STRINGS.add_rule_placeholder || 'phrase => replacement';
        ruleInput.disabled = true;
        panel.appendChild(ruleInput);

        var saveBtn = document.createElement('button');
        saveBtn.type = 'button';
        saveBtn.className = 'gst-btn gst-btn--sm gst-btn--primary gst-sc-edit__save';
        saveBtn.textContent = STRINGS.add_save || 'Save';
        saveBtn.disabled = true;
        panel.appendChild(saveBtn);

        var cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'gst-btn gst-btn--sm gst-sc-edit__cancel';
        cancelBtn.textContent = STRINGS.add_cancel || 'Cancel';
        panel.appendChild(cancelBtn);

        var tip = document.createElement('span');
        tip.className = 'gst-sc-edit__tip';
        tip.textContent = STRINGS.add_rule_tip
            || 'Format: original => replacement. Queries for "original" are rewritten to "replacement".';
        panel.appendChild(tip);

        cancelBtn.addEventListener('click', function () {
            editorTr.remove();
            addBtn.classList.remove('is-active');
        });

        rowTr.parentNode.insertBefore(editorTr, rowTr.nextSibling);

        if (!ed || typeof ed.appendRule !== 'function') {
            ruleInput.placeholder = STRINGS.add_unavailable
                || 'Synonyms editor not ready — open the Rules tab once and try again.';
            return;
        }

        function parseRule() {
            var v = (ruleInput.value || '').trim();
            var idx = v.indexOf('=>');
            if (idx < 0) return null;
            var lhs = v.slice(0, idx).trim();
            var rhs = v.slice(idx + 2).trim();
            if (!lhs || !rhs) return null;
            return { lhs: lhs, rhs: rhs };
        }

        var existingRule = null;
        var whenReady = typeof ed.whenReady === 'function' ? ed.whenReady() : Promise.resolve();
        whenReady.then(function () {
            if (!editorTr.isConnected) return;
            ruleInput.disabled = false;
            existingRule = typeof ed.findRuleForPhrase === 'function'
                ? ed.findRuleForPhrase(row.phrase) : null;
            if (existingRule) {
                ruleInput.value = existingRule.ruleObj.rule;
                saveBtn.textContent = STRINGS.add_update || 'Update';
            } else {
                ruleInput.value = row.closestIndexedTerm
                    ? row.phrase + ' => ' + row.closestIndexedTerm
                    : row.phrase + ' => ';
            }
            saveBtn.disabled = !parseRule();
            try { ruleInput.setSelectionRange(ruleInput.value.length, ruleInput.value.length); }
            catch (e) { /* ignore */ }
            ruleInput.focus();
        });

        ruleInput.addEventListener('input', function () {
            saveBtn.disabled = !parseRule();
        });
        ruleInput.addEventListener('keydown', function (ev) {
            if (ev.key === 'Enter' && !saveBtn.disabled) {
                ev.preventDefault();
                saveBtn.click();
            } else if (ev.key === 'Escape') {
                cancelBtn.click();
            }
        });

        saveBtn.addEventListener('click', function () {
            var parsed = parseRule();
            if (!parsed) return;
            saveBtn.disabled = true;
            var origLabel = saveBtn.textContent;
            saveBtn.textContent = STRINGS.add_saving || 'Saving…';
            var saveCall = existingRule && typeof ed.updateRule === 'function'
                ? ed.updateRule(existingRule.ruleObj, parsed.lhs, parsed.rhs)
                : ed.appendRule(parsed.lhs, parsed.rhs);
            saveCall.then(function () {
                // Confetti-light confirmation, then drop both the editor row
                // and the suggested row — the suggestion is fulfilled.
                panel.innerHTML = '';
                var ok = document.createElement('span');
                ok.className = 'gst-sc-edit__ok';
                ok.textContent = STRINGS.add_saved || '✓ Rule added.';
                panel.appendChild(ok);
                setTimeout(function () {
                    editorTr.remove();
                    rowTr.remove();
                    if (suggestedGrid.children.length === 0) renderSuggested([]);
                }, 1400);
            }).catch(function (err) {
                saveBtn.disabled = false;
                saveBtn.textContent = origLabel;
                setAlert((STRINGS.add_failed || 'Could not add rule.') + ' ' + (err && err.message || ''), true);
            });
        });
    }

    function renderGeneratedAt(stamp) {
        if (!stamp) {
            generatedAt.textContent = '';
            return;
        }
        var text = STRINGS.generated_at || 'Generated';
        try {
            var d = new Date(stamp);
            if (!isNaN(d.getTime())) {
                generatedAt.textContent = text + ': ' + d.toLocaleString();
                return;
            }
        } catch (_) { /* fall through */ }
        generatedAt.textContent = text + ': ' + stamp;
    }

    function load() {
        setAlert('');
        ajax(BASE + '/SynonymCoverageApi/Index')
            .then(function (result) {
                if (!result) {
                    renderUnused([]);
                    renderSuggested([]);
                    renderGeneratedAt(null);
                    return;
                }
                renderGeneratedAt(result.generatedAt);
                renderUnused(result.unusedEntries || []);
                renderSuggested(result.suggestedAdds || []);
            })
            .catch(function (err) {
                renderUnused([]);
                renderSuggested([]);
                renderGeneratedAt(null);
                setAlert((STRINGS.load_failed || 'Could not load synonym coverage.') + ' ' + err.message, true);
            });
    }

    // ── Wire-up ──────────────────────────────────────────────────

    // Lazy init: the script is included on the Synonyms page (Rules tab is
    // the landing surface), but the data fetch should only happen when the
    // marketer actually opens the Unused tab. The merged Synonyms view calls
    // window.GST_SC_INIT on first tab switch.
    function init() {
        if (init._done) return;
        init._done = true;
        if (refreshBtn) refreshBtn.addEventListener('click', load);
        load();
    }
    window.GST_SC_INIT = init;
})();
