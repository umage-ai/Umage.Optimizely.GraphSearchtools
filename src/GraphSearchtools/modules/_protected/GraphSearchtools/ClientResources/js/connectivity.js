/**
 * Graph Search Tools — Connectivity Tester.
 *
 * Hits ConnectivityApi/Check, renders one row per probe, and maps the lowest
 * status across all probes to an overall red/amber/green dot. Auto-refreshes
 * on first load only — refresh is manual.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.connectivity) || {};

    var alertBox = document.getElementById('gst-alert');
    var grid = document.getElementById('conn-grid');
    var overallDot = document.getElementById('conn-overall-dot');
    var overallLabel = document.getElementById('conn-overall-label');
    var gatewayLabel = document.getElementById('conn-gateway');
    var checkedLabel = document.getElementById('conn-checked');
    var refreshButton = document.getElementById('conn-refresh');

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

    function statusModifier(status) {
        switch ((status || '').toLowerCase()) {
            case 'green': return 'is-green';
            case 'amber': return 'is-amber';
            case 'red':   return 'is-red';
            default:      return 'is-unknown';
        }
    }

    function rollup(probes) {
        var rank = { 'red': 3, 'amber': 2, 'unknown': 1, 'green': 0 };
        var worst = 'green';
        for (var i = 0; i < probes.length; i++) {
            var s = (probes[i].status || 'unknown').toLowerCase();
            if ((rank[s] || 0) > (rank[worst] || 0)) worst = s;
        }
        return worst;
    }

    function renderProbes(result) {
        grid.innerHTML = '';
        var probes = result.probes || [];
        probes.forEach(function (p) {
            var tr = document.createElement('tr');

            var tdStatus = document.createElement('td');
            var dot = document.createElement('span');
            dot.className = 'gst-conn-dot ' + statusModifier(p.status);
            tdStatus.appendChild(dot);
            tdStatus.appendChild(document.createTextNode(' ' + (p.status || '—')));
            tr.appendChild(tdStatus);

            var tdName = document.createElement('td');
            tdName.textContent = p.name;
            tr.appendChild(tdName);

            var tdMsg = document.createElement('td');
            tdMsg.textContent = p.message || '';
            tr.appendChild(tdMsg);

            grid.appendChild(tr);
        });

        var overall = rollup(probes);
        overallDot.className = 'gst-conn-dot ' + statusModifier(overall);
        overallLabel.textContent = overallLabelFor(overall);
        gatewayLabel.textContent = result.gatewayAddress
            ? (STRINGS.gateway || 'Gateway') + ': ' + result.gatewayAddress
            : (STRINGS.gateway_unset || 'Gateway not configured');
        checkedLabel.textContent = result.checkedAt
            ? (STRINGS.checked_at || 'Checked at') + ' ' + new Date(result.checkedAt).toLocaleTimeString()
            : '';
    }

    function overallLabelFor(status) {
        switch (status) {
            case 'green':   return STRINGS.overall_green || 'All probes passed.';
            case 'amber':   return STRINGS.overall_amber || 'Reachable with warnings.';
            case 'red':     return STRINGS.overall_red   || 'Connection problem.';
            default:        return STRINGS.overall_unknown || 'Unknown status.';
        }
    }

    function load() {
        setAlert(null);
        refreshButton.disabled = true;
        overallDot.className = 'gst-conn-dot is-unknown';
        overallLabel.textContent = STRINGS.probing || 'Probing…';
        fetch(BASE + '/ConnectivityApi/Check', {
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (resp) {
                if (!resp.ok) throw new Error('HTTP ' + resp.status);
                return resp.json();
            })
            .then(renderProbes)
            .catch(function (err) {
                setAlert((STRINGS.request_failed || 'Connectivity check failed') + ': ' + err.message, true);
                overallDot.className = 'gst-conn-dot is-red';
                overallLabel.textContent = STRINGS.overall_unknown || 'Unknown status.';
            })
            .then(function () { refreshButton.disabled = false; });
    }

    refreshButton.addEventListener('click', load);
    load();
})();
