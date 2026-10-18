// Import & Migration pages (Department Onboarding and Migration Wizard plan, section 6.6): binds from script, controls
// stay disabled until bound, same-origin JSON calls carry the antiforgery header, strings come from the page's JSON block.
(function () {
    'use strict';
    var root = document.getElementById('import-page');
    if (!root) return;
    var config = JSON.parse(root.textContent);
    var text = config.text || {};

    function token() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function readJson(id) {
        var node = document.getElementById(id);
        return node ? JSON.parse(node.textContent) : null;
    }

    // Navigation targets come from the page's markup and response headers: only ever a page on this site (never another
    // origin or a javascript: address).
    function navigate(address) {
        if (!address) return false;
        try {
            var url = new URL(address, window.location.origin);
            if (url.origin !== window.location.origin) return false;
            window.location.assign(url.href);
            return true;
        } catch (e) {
            return false;
        }
    }

    function setStatus(node, message, isError) {
        if (!node) return;
        node.textContent = message || '';
        node.classList.toggle('text-danger', !!isError);
    }

    async function postJson(url, body) {
        var response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
            body: JSON.stringify(body)
        });
        var stepUp = response.headers.get('X-Resgrid-Step-Up');
        if (stepUp && navigate(stepUp)) return null;
        var result = null;
        try { result = await response.json(); } catch (e) { result = null; }
        if (!response.ok) throw new Error((result && result.message) || text.failed);
        return result;
    }

    async function sha256(buffer) {
        var digest = await crypto.subtle.digest('SHA-256', buffer);
        return Array.from(new Uint8Array(digest)).map(function (b) { return b.toString(16).padStart(2, '0'); }).join('');
    }

    function format(template) {
        var args = Array.prototype.slice.call(arguments, 1);
        return (template || '').replace(/\{(\d+)\}/g, function (match, index) { return args[index] !== undefined ? args[index] : match; });
    }

    // Forms that ask before they act (cancel a workspace or a run).
    document.querySelectorAll('form.import-confirm').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (!window.confirm(form.getAttribute('data-confirm-text') || text.confirm)) event.preventDefault();
        });
    });

    // ---- Files: chunked, resumable upload ------------------------------------------------------------------------
    var upload = document.getElementById('import-upload');
    if (upload) {
        var fileInput = document.getElementById('import-file');
        var startButton = document.getElementById('import-upload-start');
        var bar = document.getElementById('import-upload-progress');
        var barWrap = document.getElementById('import-upload-progress-wrap');
        var status = document.getElementById('import-upload-status');
        var chunkSize = parseInt(upload.getAttribute('data-chunk'), 10) || (8 * 1024 * 1024);

        fileInput.disabled = false;
        fileInput.addEventListener('change', function () { startButton.disabled = !fileInput.files.length; });

        // The same file picked again (after a dropped connection or a reload) continues its unfinished upload. Storage can be
        // unavailable (private windows, blocked site data); then every upload simply starts fresh.
        function resumeKey(file) {
            return 'rg-import-upload:' + upload.getAttribute('data-workspace') + ':' + file.name + ':' + file.size + ':' + file.lastModified;
        }
        function remembered(file) {
            try { return window.sessionStorage.getItem(resumeKey(file)); } catch (e) { return null; }
        }
        function remember(file, artifactId) {
            try {
                if (artifactId) window.sessionStorage.setItem(resumeKey(file), artifactId);
                else window.sessionStorage.removeItem(resumeKey(file));
            } catch (e) { /* storage unavailable */ }
        }
        async function unfinished(file) {
            var artifactId = remembered(file);
            if (!artifactId) return null;
            try {
                var response = await fetch(upload.getAttribute('data-status') + '?artifactId=' + encodeURIComponent(artifactId), { credentials: 'same-origin', cache: 'no-store' });
                var current = response.ok ? await response.json() : null;
                if (current && !current.Completed && !current.ErrorCode && current.ExpectedBytes === file.size) return current;
            } catch (e) { /* fall through to a new upload */ }
            remember(file, null);
            return null;
        }

        startButton.addEventListener('click', async function () {
            var file = fileInput.files[0];
            if (!file) return;
            startButton.disabled = true;
            fileInput.disabled = true;
            barWrap.style.display = '';
            try {
                var started = await unfinished(file);
                if (!started) {
                    setStatus(status, text.hashing);
                    var whole = await sha256(await file.arrayBuffer());
                    started = await postJson(upload.getAttribute('data-start'), {
                        WorkspaceId: upload.getAttribute('data-workspace'),
                        FileName: file.name,
                        SizeBytes: file.size,
                        Sha256: whole,
                        ContentType: file.type || 'application/octet-stream'
                    });
                    if (!started) return;
                    remember(file, started.ArtifactId);
                }
                var size = started.ChunkSizeBytes || chunkSize;
                var parts = Math.max(1, Math.ceil(file.size / size));
                for (var part = started.NextPartNumber || 1; part <= parts; part++) {
                    var slice = await file.slice((part - 1) * size, Math.min(file.size, part * size)).arrayBuffer();
                    var url = upload.getAttribute('data-chunk-url') + '?artifactId=' + encodeURIComponent(started.ArtifactId) + '&part=' + part + '&sha256=' + (await sha256(slice));
                    var response = await fetch(url, {
                        method: 'POST', credentials: 'same-origin', cache: 'no-store',
                        headers: { 'Content-Type': 'application/octet-stream', 'RequestVerificationToken': token() }, body: slice
                    });
                    if (!response.ok) {
                        var failure = null;
                        try { failure = await response.json(); } catch (e) { failure = null; }
                        throw new Error((failure && failure.message) || text.failed);
                    }
                    var percent = Math.round(part * 100 / parts);
                    bar.style.width = percent + '%';
                    bar.setAttribute('aria-valuenow', String(percent));
                    setStatus(status, format(text.uploading, percent));
                }
                setStatus(status, text.finishing);
                await postJson(upload.getAttribute('data-complete') + '?artifactId=' + encodeURIComponent(started.ArtifactId), {});
                remember(file, null);
                setStatus(status, text.uploaded);
                window.setTimeout(function () { navigate(upload.getAttribute('data-reload')); }, 1200);
            } catch (error) {
                setStatus(status, error.message, true);
                startButton.disabled = false;
                fileInput.disabled = false;
            }
        });
    }

    var templateForm = document.getElementById('import-template-form');
    if (templateForm) {
        templateForm.addEventListener('submit', function () {
            var keys = Array.from(document.querySelectorAll('.import-template-entity:checked')).map(function (box) { return box.value; });
            document.getElementById('import-template-entities').value = keys.join(',');
        });
    }

    // ---- Data set: column matching ------------------------------------------------------------------------------------
    // One row per use of a column: a column can feed several fields (a full name split into first and last name), each
    // with its own clean-ups. Dates get the day/month question once per column; fixed values fill every row of a field.
    var dataset = readJson('import-dataset-page');
    if (dataset && !dataset.locked) {
        var fields = {};
        (dataset.fields || []).forEach(function (field) { fields[field.key] = field; });
        var tbody = document.querySelector('#import-mapping-table tbody');
        var extraCount = 0;

        function isDateField(key) {
            var field = fields[key];
            return !!field && (field.type === 'date' || field.type === 'datetime');
        }

        function bindRow(row) {
            row.querySelectorAll('select').forEach(function (select) { select.disabled = false; });
            var fieldSelect = row.querySelector('.import-map-field');
            var dates = row.querySelector('.import-map-dates');
            fieldSelect.addEventListener('change', function () {
                if (dates) dates.style.display = isDateField(fieldSelect.value) ? '' : 'none';
            });
            var add = row.querySelector('.import-map-add');
            if (add) {
                add.disabled = false;
                add.addEventListener('click', function () { addUse(row); });
            }
            var remove = row.querySelector('.import-map-remove');
            if (remove) {
                remove.disabled = false;
                remove.addEventListener('click', function () { row.parentNode.removeChild(row); });
            }
        }

        function addUse(row) {
            var source = row.getAttribute('data-source');
            var clone = row.cloneNode(true);
            clone.classList.add('import-map-extra');
            var first = clone.cells[0];
            first.textContent = '';
            var name = document.createElement('span');
            name.className = 'text-muted';
            name.textContent = '\u21B3 ' + source;
            var note = document.createElement('div');
            note.className = 'small text-muted';
            note.textContent = dataset.text.sameColumn;
            var remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'btn btn-xs btn-link import-map-remove';
            remove.textContent = dataset.text.remove;
            first.appendChild(name);
            first.appendChild(note);
            first.appendChild(remove);
            clone.cells[1].textContent = '';
            clone.querySelector('.import-map-field').value = '::ignore';
            Array.from(clone.querySelector('.import-map-transforms').options).forEach(function (option) { option.selected = false; });
            var dateSelect = clone.querySelector('.import-map-dateformat');
            if (dateSelect) dateSelect.value = row.querySelector('.import-map-dateformat').value;
            clone.querySelector('.import-map-dates').style.display = 'none';
            extraCount++;
            clone.querySelectorAll('[id]').forEach(function (node) {
                var label = clone.querySelector('label[for="' + node.id + '"]');
                node.id = node.id + '-x' + extraCount;
                if (label) label.setAttribute('for', node.id);
            });
            var after = row;
            while (after.nextElementSibling && after.nextElementSibling.classList.contains('import-map-extra') && after.nextElementSibling.getAttribute('data-source') === source) {
                after = after.nextElementSibling;
            }
            after.parentNode.insertBefore(clone, after.nextSibling);
            bindRow(clone);
            clone.querySelector('.import-map-field').focus();
        }

        Array.from(tbody.querySelectorAll('tr')).forEach(bindRow);
        var constantInputs = Array.from(document.querySelectorAll('.import-constant'));
        constantInputs.forEach(function (input) { input.disabled = false; });

        var saveButton = document.getElementById('import-mapping-save');
        var mappingStatus = document.getElementById('import-mapping-status');
        if (saveButton) {
            saveButton.disabled = false;
            saveButton.addEventListener('click', async function () {
                var body = dataset.body || {};
                var formats = {};
                body.Columns = Array.from(tbody.querySelectorAll('tr')).map(function (row) {
                    var source = row.getAttribute('data-source');
                    var extra = row.classList.contains('import-map-extra');
                    var choice = row.querySelector('.import-map-field').value;
                    var transforms = Array.from(row.querySelector('.import-map-transforms').selectedOptions).map(function (o) { return o.value; });
                    if (choice === '::ignore') return extra ? null : { SourceColumn: source, Kind: 0, Transforms: transforms };
                    if (choice === '::custom') return { SourceColumn: source, Kind: 3, CustomFieldLabel: source, Transforms: transforms };
                    var field = fields[choice] || {};
                    var dateSelect = row.querySelector('.import-map-dateformat');
                    if (isDateField(choice) && dateSelect && dateSelect.value) formats[source] = dateSelect.value;
                    var isReference = field.type === 'reference' || field.itemType === 'reference';
                    return { SourceColumn: source, Kind: isReference ? 2 : 1, FieldKey: choice, ReferencesEntity: field.references || null, Transforms: transforms, Confidence: 100 };
                }).filter(function (binding) { return binding; });
                body.Formats = formats;
                var constants = {};
                constantInputs.forEach(function (input) {
                    var value = (input.value || '').trim();
                    if (value) constants[input.getAttribute('data-field')] = value;
                });
                body.Constants = constants;
                var identity = body.Columns.filter(function (c) { return c.FieldKey === 'source_id'; })[0];
                body.SourceIdentityColumn = identity ? identity.SourceColumn : null;
                saveButton.disabled = true;
                try {
                    await postJson(dataset.saveUrl, { DatasetId: dataset.datasetId, ExpectedRevision: dataset.revision, Body: body });
                    setStatus(mappingStatus, dataset.text.saved);
                    window.setTimeout(function () { navigate(dataset.reloadUrl); }, 800);
                } catch (error) {
                    setStatus(mappingStatus, error.message, true);
                    saveButton.disabled = false;
                }
            });
        }
    }

    // ---- Values -----------------------------------------------------------------------------------------------------
    var values = readJson('import-values-page');
    if (values) {
        var valueRows = Array.from(document.querySelectorAll('#import-values-table tbody tr'));
        valueRows.forEach(function (row) { row.querySelector('select').disabled = false; });
        var bulk = document.getElementById('import-values-bulk');
        bulk.disabled = false;
        bulk.addEventListener('change', function () {
            valueRows.forEach(function (row) {
                var select = row.querySelector('select');
                if (bulk.value === 'suggested' && row.getAttribute('data-suggested')) select.value = 'map:' + row.getAttribute('data-suggested');
                else if (bulk.value === 'create' && select.querySelector('option[value="create"]')) select.value = 'create';
                else if (bulk.value === 'empty') select.value = 'empty';
            });
            bulk.value = '';
        });
        var valuesButton = document.getElementById('import-values-save');
        var valuesStatus = document.getElementById('import-values-status');
        if (valuesButton) {
            valuesButton.disabled = false;
            valuesButton.addEventListener('click', async function () {
                var decisions = valueRows.map(function (row) {
                    var choice = row.querySelector('select').value;
                    if (!choice) return null;
                    var decision = { SourceValue: row.getAttribute('data-value'), Confidence: parseInt(row.getAttribute('data-confidence'), 10) || 0 };
                    if (choice.indexOf('map:') === 0) { decision.Action = 1; decision.TargetCode = choice.substring(4); }
                    else if (choice === 'create') decision.Action = 2;
                    else if (choice === 'empty') decision.Action = 3;
                    else if (choice === 'label') decision.Action = 4;
                    return decision;
                }).filter(function (d) { return d; });
                valuesButton.disabled = true;
                try {
                    await postJson(values.saveUrl, { DatasetId: values.datasetId, FieldKey: values.fieldKey, Decisions: decisions });
                    setStatus(valuesStatus, values.text.saved);
                    window.setTimeout(function () { navigate(values.reloadUrl); }, 800);
                } catch (error) {
                    setStatus(valuesStatus, error.message, true);
                    valuesButton.disabled = false;
                }
            });
        }
    }

    // ---- Issues -------------------------------------------------------------------------------------------------------
    var issues = readJson('import-issues-page');
    if (issues) {
        document.querySelectorAll('.import-issue-group').forEach(function (group) {
            var ids = (group.getAttribute('data-issues') || '').split(',').filter(function (id) { return id; });
            group.querySelectorAll('.import-issue-value').forEach(function (input) { input.disabled = false; });
            group.querySelectorAll('.import-issue-action').forEach(function (button) {
                button.disabled = false;
                button.addEventListener('click', async function () {
                    var action = button.getAttribute('data-action');
                    if (action === 'exclude' && !window.confirm(issues.text.confirmExclude)) return;
                    var input = group.querySelector('.import-issue-value');
                    button.disabled = true;
                    try {
                        await postJson(issues.actionUrl, { WorkspaceId: issues.workspaceId, Action: action, IssueIds: ids, Value: input ? input.value : null });
                        navigate(issues.reloadUrl);
                    } catch (error) {
                        window.alert(error.message);
                        button.disabled = false;
                    }
                });
            });
        });
    }

    // ---- Review chart ----------------------------------------------------------------------------------------------
    var chart = readJson('import-review-chart');
    var canvas = document.getElementById('import-review-canvas');
    if (chart && canvas && window.Chart) {
        new window.Chart(canvas.getContext('2d'), {
            type: 'bar',
            data: {
                labels: chart.labels,
                datasets: [
                    { label: chart.text.create, data: chart.create, backgroundColor: '#1baf7a' },
                    { label: chart.text.update, data: chart.update, backgroundColor: '#2a78d6' },
                    { label: chart.text.skip, data: chart.skip, backgroundColor: '#8c8b86' },
                    { label: chart.text.blocked, data: chart.blocked, backgroundColor: '#e34948' }
                ]
            },
            options: { responsive: true, maintainAspectRatio: false, scales: { x: { stacked: true }, y: { stacked: true, beginAtZero: true } } }
        });
    }

    // ---- Verify: invite everyone toggle -----------------------------------------------------------------------------
    var inviteAll = document.getElementById('import-invite-all');
    if (inviteAll) {
        inviteAll.addEventListener('change', function () {
            document.querySelectorAll('.import-invite-person').forEach(function (box) { box.checked = inviteAll.checked; });
        });
    }

    // ---- Progress polling (overview and history), 3-5 s while background work runs ------------------------------------
    var history = document.getElementById('import-history');
    if (history) {
        var live = Array.from(history.querySelectorAll('tr[data-live="1"]'));
        if (live.length) {
            var timer = window.setInterval(async function () {
                for (var i = 0; i < live.length; i++) {
                    var row = live[i];
                    try {
                        var response = await fetch(history.getAttribute('data-status-url') + '?runId=' + encodeURIComponent(row.getAttribute('data-run')), { credentials: 'same-origin', cache: 'no-store' });
                        if (!response.ok) continue;
                        var run = await response.json();
                        if (run.Status !== 1 && run.Status !== 0 && run.Status !== 3) { window.clearInterval(timer); window.location.reload(); return; }
                    } catch (e) { /* the next tick tries again */ }
                }
            }, 3000);
        }
    }

    var overview = document.getElementById('import-overview');
    if (overview) {
        var lastStatus = null;
        window.setInterval(async function () {
            try {
                var response = await fetch(overview.getAttribute('data-summary-url'), { credentials: 'same-origin', cache: 'no-store' });
                if (!response.ok) return;
                var summary = await response.json();
                ['RowsAnalysed', 'RowsReady', 'RowsNeedingAttention', 'RowsImported', 'PeopleToInvite'].forEach(function (key) {
                    var node = overview.querySelector('[data-stat="' + key + '"]');
                    if (node && summary[key] !== undefined) node.textContent = summary[key];
                });
                if (lastStatus !== null && lastStatus !== summary.status) window.location.reload();
                lastStatus = summary.status;
            } catch (e) { /* the next tick tries again */ }
        }, 5000);
    }
})();
