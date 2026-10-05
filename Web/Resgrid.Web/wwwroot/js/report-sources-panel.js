/*
 * Report editors' "Call data" panel. Loads a call's report sources (ReportSourcesController.Call) and lets the author look
 * up and apply them: any time into the time field they last clicked, a unit's times into its row, the command's tactic
 * timestamps, another station's run report times and narrative, and (on run reports) a fill of the blank fields from the
 * call. Everything from the server is set with textContent; nothing it returns is parsed as markup.
 *
 * The page marks what can be filled:
 *   [data-rs-unit="{unitId}"]         a unit row; inside it [data-rs-field="dispatched|enroute|onScene|staging|cancelled|
 *                                     cleared|inService|staffing"] and an optional [data-rs-select] checkbox to tick
 *   [data-rs-tactic="{field}"]        a NERIS tactic timestamp input
 *   [data-rs-call="started|ended|location|initialReport"]  run report header fields
 *   [data-rs-participants]            the run report's participant rows (with #add-record-participant)
 */
(function () {
    'use strict';

    const panel = document.getElementById('report-sources');
    if (!panel) return;

    const form = document.getElementById(panel.getAttribute('data-form') || '');
    const strings = JSON.parse(panel.getAttribute('data-strings') || '{}');
    const mode = panel.getAttribute('data-mode') || 'incident';
    const narrativeSelector = panel.getAttribute('data-narrative-target');
    const body = panel.querySelector('[data-rs-body]');
    const status = panel.querySelector('[data-rs-status]');
    const tabs = panel.querySelector('[data-rs-tabs]');
    let baseUrl = panel.getAttribute('data-url');
    let callId = panel.getAttribute('data-call-id');
    let data = null;
    let target = null;
    let activeTab = 'timeline';
    let timelineFilter = 'all';

    function s(key) { return strings[key] || key; }
    function fmt(key) { const args = Array.prototype.slice.call(arguments, 1); return s(key).replace(/\{(\d+)\}/g, function (m, i) { return args[+i] != null ? args[+i] : ''; }); }

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    function say(message, kind) {
        status.textContent = message || '';
        status.className = 'small ' + (kind === 'error' ? 'text-danger' : kind === 'ok' ? 'text-success' : 'text-muted');
    }

    function fire(input) {
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
        input.classList.add('rs-filled');
        setTimeout(function () { input.classList.remove('rs-filled'); }, 1500);
    }

    function setValue(input, value, overwrite) {
        if (!input || value == null || value === '') return false;
        if (!overwrite && input.value) return false;
        if (input.value === String(value)) return false;
        input.value = value;
        fire(input);
        return true;
    }

    // ---- target tracking -------------------------------------------------------------------------------------------

    if (form) {
        form.addEventListener('focusin', function (event) {
            const input = event.target;
            if (input && input.matches && input.matches('input[type="datetime-local"]')) {
                if (target) target.classList.remove('rs-target');
                target = input;
                target.classList.add('rs-target');
            }
        });
    }

    function useTime(time) {
        if (!time) return;
        if (!target || !document.body.contains(target)) { say(s('NoTarget'), 'error'); return; }
        target.value = time.input;
        fire(target);
        say(fmt('UsedTime', time.display), 'ok');
    }

    function timeButton(time) {
        const wrap = el('span', 'rs-time');
        wrap.appendChild(el('span', null, time.display));
        const use = el('button', 'btn btn-xs btn-white rs-use', s('Use'));
        use.type = 'button';
        use.title = s('UseHint');
        use.addEventListener('click', function () { useTime(time); });
        wrap.appendChild(use);
        return wrap;
    }

    // ---- applying data to the form ----------------------------------------------------------------------------------

    function unitRow(unitId) {
        return form ? form.querySelector('[data-rs-unit="' + String(unitId).replace(/[^0-9]/g, '') + '"]') : null;
    }

    function applyUnit(unitId, values, overwrite) {
        const row = unitRow(unitId);
        if (!row) return -1;
        let changed = 0;
        Object.keys(values).forEach(function (field) {
            const value = values[field];
            if (value == null) return;
            const input = row.querySelector('[data-rs-field="' + field + '"]');
            if (input && setValue(input, typeof value === 'object' ? value.input : value, overwrite)) changed++;
        });
        const select = row.querySelector('[data-rs-select]');
        if (changed > 0 && select && !select.checked) { select.checked = true; fire(select); }
        return changed;
    }

    function unitValues(times, staffing) {
        return {
            dispatched: times.dispatched, enroute: times.enroute, onScene: times.onScene, staging: times.staging,
            cancelled: times.cancelled, cleared: times.cleared, inService: times.inService || times.inQuarters,
            staffing: staffing != null && staffing > 0 ? String(staffing) : null
        };
    }

    function reportUnitResult(name, changed) {
        if (changed < 0) say(fmt('UnitNotOnReport', name), 'error');
        else if (changed === 0) say(fmt('NothingToFill', name));
        else say(fmt('FilledUnit', changed, name), 'ok');
    }

    function fillTactics(overwrite) {
        if (!data || !data.command || !form) return 0;
        let changed = 0;
        data.command.tactics.forEach(function (t) {
            const input = form.querySelector('[data-rs-tactic="' + t.field + '"]');
            if (input && setValue(input, t.time.input, overwrite)) changed++;
        });
        return changed;
    }

    function appendNarrative(heading, text) {
        const area = narrativeSelector ? document.querySelector(narrativeSelector) : null;
        if (!area || !text) return false;
        area.value = (area.value ? area.value.replace(/\s+$/, '') + '\n\n' : '') + heading + '\n' + text;
        fire(area);
        return true;
    }

    function selectOption(select, value) {
        if (!select || value == null) return false;
        const wanted = String(value).toLowerCase();
        for (let i = 0; i < select.options.length; i++) {
            if (select.options[i].value.toLowerCase() === wanted) {
                if (select.selectedIndex === i) return false;
                select.selectedIndex = i;
                fire(select);
                return true;
            }
        }
        return false;
    }

    /** Run reports: fills the blank header fields, every source unit's blank times and the engaged personnel from the call. */
    function fillRunFromCall(replaceStart) {
        if (!data || !form) return;
        let changed = 0;
        const started = form.querySelector('[data-rs-call="started"]');
        if (started && data.loggedOn) {
            const replaceable = replaceStart && started.getAttribute('data-rs-replaceable') === 'true';
            if (setValue(started, data.loggedOn.input, replaceable)) { changed++; started.removeAttribute('data-rs-replaceable'); }
        }
        if (data.closedOn && setValue(form.querySelector('[data-rs-call="ended"]'), data.closedOn.input, false)) changed++;
        if (setValue(form.querySelector('[data-rs-call="location"]'), data.address, false)) changed++;
        if (setValue(form.querySelector('[data-rs-call="initialReport"]'), data.nature, false)) changed++;

        data.units.forEach(function (u) {
            if (!u.wasDispatched && !u.assignedByCommand && !u.times.enroute && !u.times.onScene) return;
            const result = applyUnit(u.unitId, unitValues(u.times, null), false);
            if (result > 0) changed += result;
        });

        const rows = form.querySelector('[data-rs-participants]');
        const add = document.getElementById('add-record-participant');
        if (rows && add) {
            const listed = Array.prototype.map.call(rows.querySelectorAll('select[name$=".UserId"]'), function (sel) { return (sel.value || '').toLowerCase(); });
            data.personnel.filter(function (p) { return p.engaged; }).forEach(function (p) {
                if (listed.indexOf(p.userId.toLowerCase()) >= 0) return;
                let row = Array.prototype.find.call(rows.children, function (r) { const sel = r.querySelector('select[name$=".UserId"]'); return sel && !sel.value; });
                if (!row) { add.click(); row = rows.lastElementChild; }
                if (!row) return;
                if (!selectOption(row.querySelector('select[name$=".UserId"]'), p.userId)) return;
                if (p.unitId) selectOption(row.querySelector('select[name$=".UnitId"]'), p.unitId);
                const role = row.querySelector('input[name$=".Role"]');
                if (role && p.role && !role.value) { role.value = p.role; fire(role); }
                listed.push(p.userId.toLowerCase());
                changed++;
            });
        }

        say(changed > 0 ? fmt('FilledFromCall', changed) : s('NothingNew'), changed > 0 ? 'ok' : null);
    }

    // ---- rendering --------------------------------------------------------------------------------------------------

    function badge(text, kind) { return el('span', 'label label-' + (kind || 'default') + ' rs-badge', text); }

    function originText(entry) {
        if (!entry.origin || entry.origin === 'Unknown') return entry.setBy && !entry.setBySelf ? fmt('SetBy', entry.setBy) : null;
        const via = s('Origin' + entry.origin);
        if (entry.setBy && !entry.setBySelf) return fmt('SetByVia', entry.setBy, via);
        return fmt('Via', via);
    }

    function renderTimeline(container) {
        const filters = el('div', 'btn-group btn-group-xs rs-filters');
        [['all', 'FilterAll'], ['units', 'FilterUnits'], ['personnel', 'FilterPersonnel'], ['dispatch', 'FilterDispatch'], ['command', 'FilterCommand']].forEach(function (f) {
            const b = el('button', 'btn btn-' + (timelineFilter === f[0] ? 'primary' : 'white'), s(f[1]));
            b.type = 'button';
            b.addEventListener('click', function () { timelineFilter = f[0]; render(); });
            filters.appendChild(b);
        });
        container.appendChild(filters);

        const match = {
            all: function () { return true; },
            units: function (e) { return e.kind === 'UnitStatus' || e.kind === 'UnitDispatch'; },
            personnel: function (e) { return e.kind === 'PersonnelStatus' || e.kind === 'PersonnelDispatch' || e.kind === 'CheckIn'; },
            dispatch: function (e) { return e.kind === 'Call' || e.kind === 'UnitDispatch' || e.kind === 'PersonnelDispatch'; },
            command: function (e) { return e.kind === 'Command' || e.kind === 'Objective'; }
        }[timelineFilter];

        const entries = data.entries.filter(match);
        if (entries.length === 0) { container.appendChild(el('p', 'text-muted', s('NoEntries'))); return; }

        const list = el('ul', 'list-unstyled rs-timeline');
        entries.forEach(function (e) {
            const item = el('li', 'rs-entry rs-kind-' + e.kind.toLowerCase());
            const head = el('div', 'rs-entry-head');
            head.appendChild(timeButton(e.time));
            item.appendChild(head);

            const line = el('div', 'rs-entry-line');
            line.appendChild(badge(s('Kind' + e.kind), e.kind === 'Command' || e.kind === 'Objective' ? 'warning' : e.kind.indexOf('Dispatch') >= 0 || e.kind === 'Call' ? 'info' : 'default'));
            line.appendChild(el('strong', null, ' ' + (e.subject || '') + ' '));
            const label = el('span', 'rs-status', e.label || '');
            if (e.color && /^#[0-9a-fA-F]{3,8}$/.test(e.color)) label.style.borderLeft = '4px solid ' + e.color;
            line.appendChild(label);
            if (e.linkage) {
                const link = badge(s(e.linkage === 'inferred' ? 'LinkInferred' : 'LinkAuto'), 'warning');
                link.title = s('LinkHint');
                line.appendChild(document.createTextNode(' '));
                line.appendChild(link);
            }
            if (e.tactic) { line.appendChild(document.createTextNode(' ')); line.appendChild(badge(s('Tactic_' + e.tactic), 'primary')); }
            item.appendChild(line);

            const origin = originText(e);
            if (origin) item.appendChild(el('div', 'small text-muted', origin));
            if (e.detail) item.appendChild(el('div', 'small rs-detail', e.detail));
            list.appendChild(item);
        });
        container.appendChild(list);
    }

    function timesTable(rows) {
        const table = el('table', 'table table-condensed rs-times');
        const tbody = el('tbody');
        rows.forEach(function (r) {
            if (!r[1]) return;
            const tr = el('tr');
            tr.appendChild(el('th', null, s(r[0])));
            const td = el('td');
            td.appendChild(timeButton(r[1]));
            tr.appendChild(td);
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        return tbody.children.length ? table : null;
    }

    function unitActions(name, values) {
        const actions = el('div', 'rs-actions');
        [['FillBlanks', false], ['Replace', true]].forEach(function (a) {
            const b = el('button', 'btn btn-xs ' + (a[1] ? 'btn-white' : 'btn-primary'), s(a[0]));
            b.type = 'button';
            b.addEventListener('click', function () { reportUnitResult(name, applyUnit(values.unitId, values.values, a[1])); });
            actions.appendChild(b);
        });
        return actions;
    }

    function renderUnits(container) {
        if (data.units.length === 0) { container.appendChild(el('p', 'text-muted', s('NoUnits'))); return; }
        data.units.forEach(function (u) {
            const card = el('div', 'rs-card');
            const title = el('div', 'rs-card-title');
            title.appendChild(el('strong', null, u.name));
            if (u.wasDispatched) { title.appendChild(document.createTextNode(' ')); title.appendChild(badge(s('Dispatched'), 'info')); }
            if (u.assignedByCommand) { title.appendChild(document.createTextNode(' ')); title.appendChild(badge(s('AssignedByCommand'), 'warning')); }
            if (u.timesSource === 'AutoLinked' || u.timesSource === 'Inferred') { title.appendChild(document.createTextNode(' ')); const b = badge(s(u.timesSource === 'Inferred' ? 'LinkInferred' : 'LinkAuto'), 'warning'); b.title = s('LinkHint'); title.appendChild(b); }
            card.appendChild(title);
            const table = timesTable([['Dispatched', u.times.dispatched], ['Enroute', u.times.enroute], ['OnScene', u.times.onScene], ['Staging', u.times.staging],
                ['Cancelled', u.times.cancelled], ['Cleared', u.times.cleared], ['InService', u.times.inService], ['CommandAssigned', u.times.commandAssigned], ['CommandReleased', u.times.commandReleased]]);
            if (table) card.appendChild(table);
            if (u.crew && u.crew.length) card.appendChild(el('div', 'small', fmt('CrewList', u.crew.length, u.crew.join(', '))));
            if (form && unitRow(u.unitId)) card.appendChild(unitActions(u.name, { unitId: u.unitId, values: unitValues(u.times, u.staffing) }));
            container.appendChild(card);
        });
    }

    function renderPersonnel(container) {
        if (data.personnel.length === 0) { container.appendChild(el('p', 'text-muted', s('NoPersonnel'))); return; }
        data.personnel.forEach(function (p) {
            const card = el('div', 'rs-card' + (p.engaged ? '' : ' rs-muted'));
            const title = el('div', 'rs-card-title');
            title.appendChild(el('strong', null, p.name));
            if (p.unit) title.appendChild(el('span', 'text-muted', ' · ' + p.unit + (p.role ? ' (' + p.role + ')' : '')));
            if (!p.engaged) { title.appendChild(document.createTextNode(' ')); title.appendChild(badge(s('NotEngaged'))); }
            if (p.assignedByCommand) { title.appendChild(document.createTextNode(' ')); title.appendChild(badge(s('AssignedByCommand'), 'warning')); }
            card.appendChild(title);
            const table = timesTable([['Responding', p.times.responding], ['OnScene', p.times.onScene], ['Cleared', p.times.cleared], ['First', p.times.first], ['Last', p.times.last]]);
            if (table) card.appendChild(table);
            container.appendChild(card);
        });
    }

    function renderCommand(container) {
        const c = data.command;
        if (!c) { container.appendChild(el('p', 'text-muted', s('NoCommand'))); return; }
        if (c.name) container.appendChild(el('h4', null, c.name));
        const table = timesTable([['CommandEstablished', c.established], ['CommandClosed', c.closed]]);
        if (table) container.appendChild(table);
        if (c.commanders.length) container.appendChild(el('p', 'small', fmt('Commanders', c.commanders.join(', '))));

        container.appendChild(el('h5', null, s('TacticTimestamps')));
        if (c.tactics.length === 0) container.appendChild(el('p', 'text-muted small', s('NoTactics')));
        else {
            const tactics = timesTable(c.tactics.map(function (t) { return ['Tactic_' + t.field, t.time]; }));
            if (tactics) container.appendChild(tactics);
            if (form && form.querySelector('[data-rs-tactic]')) {
                const fill = el('button', 'btn btn-xs btn-primary', s('FillTactics'));
                fill.type = 'button';
                fill.addEventListener('click', function () { const n = fillTactics(false); say(n > 0 ? fmt('FilledCount', n) : s('NothingNew'), n > 0 ? 'ok' : null); });
                container.appendChild(fill);
            }
        }

        container.appendChild(el('h5', null, s('MutualAid')));
        if (c.mutualAid.length === 0) container.appendChild(el('p', 'text-muted small', s('NoMutualAid')));
        c.mutualAid.forEach(function (a) {
            const row = el('div', 'rs-card');
            row.appendChild(el('strong', null, a.agency));
            if (a.resources.length) row.appendChild(el('div', 'small', a.resources.join(', ')));
            container.appendChild(row);
        });
    }

    function renderRunReports(container) {
        if (data.runReports.length === 0) { container.appendChild(el('p', 'text-muted', s('NoRunReports'))); return; }
        data.runReports.forEach(function (r) {
            const card = el('div', 'rs-card');
            const title = el('div', 'rs-card-title');
            const link = el('a', null, r.type + ' ' + (r.reference || ''));
            link.href = r.url;
            link.target = '_blank';
            link.rel = 'noopener';
            title.appendChild(link);
            title.appendChild(document.createTextNode(' '));
            title.appendChild(badge(r.isFinal ? s('Final') : s('NotFinal'), r.isFinal ? 'primary' : 'default'));
            card.appendChild(title);
            const by = [r.author ? fmt('ByAuthor', r.author) : null, r.station].filter(Boolean).join(' · ');
            if (by) card.appendChild(el('div', 'small text-muted', by));
            if (r.withheld) card.appendChild(el('div', 'small text-warning', s('Withheld')));

            const header = timesTable([['Started', r.started], ['Ended', r.ended]]);
            if (header) card.appendChild(header);

            r.units.forEach(function (u) {
                const unit = el('div', 'rs-subcard');
                unit.appendChild(el('strong', null, u.name + (u.crew ? ' · ' + fmt('CrewCount', u.crew) : '')));
                const times = timesTable([['Dispatched', u.times.dispatched], ['Enroute', u.times.enroute], ['OnScene', u.times.onScene], ['Cleared', u.times.cleared], ['InService', u.times.inQuarters]]);
                if (times) unit.appendChild(times);
                if (form && unitRow(u.unitId)) unit.appendChild(unitActions(u.name, { unitId: u.unitId, values: unitValues(u.times, u.crew) }));
                card.appendChild(unit);
            });

            if (r.personnel.length) {
                card.appendChild(el('div', 'small', fmt('PersonnelList', r.personnel.map(function (p) { return p.name + (p.role ? ' (' + p.role + ')' : ''); }).join(', '))));
            }
            [['Location', r.location], ['InitialReport', r.initialReport], ['Cause', r.cause], ['OtherAgencies', r.otherAgencies], ['OtherUnits', r.otherUnits]].forEach(function (f) {
                if (f[1]) card.appendChild(el('div', 'small', s(f[0]) + ': ' + f[1]));
            });

            if (r.narrative) {
                const details = el('details', 'rs-narrative');
                details.appendChild(el('summary', null, s('Narrative')));
                details.appendChild(el('div', 'rs-narrative-text', r.narrative));
                card.appendChild(details);
                if (narrativeSelector && document.querySelector(narrativeSelector)) {
                    const add = el('button', 'btn btn-xs btn-white', s('AddToNarrative'));
                    add.type = 'button';
                    add.addEventListener('click', function () {
                        const heading = '[' + [r.type + ' ' + (r.reference || ''), r.station, r.author].filter(Boolean).join(' · ') + ']';
                        if (appendNarrative(heading, r.narrative)) say(s('NarrativeAdded'), 'ok');
                    });
                    card.appendChild(add);
                }
            }
            container.appendChild(card);
        });
    }

    function render() {
        body.textContent = '';
        tabs.querySelectorAll('[data-rs-tab]').forEach(function (tab) {
            tab.parentElement.classList.toggle('active', tab.getAttribute('data-rs-tab') === activeTab);
        });
        if (!data) return;
        const count = tabs.querySelector('[data-rs-count="runReports"]');
        if (count) count.textContent = data.runReports.length ? String(data.runReports.length) : '';
        ({ timeline: renderTimeline, units: renderUnits, personnel: renderPersonnel, command: renderCommand, runReports: renderRunReports }[activeTab] || renderTimeline)(body);
    }

    tabs.addEventListener('click', function (event) {
        const tab = event.target.closest('[data-rs-tab]');
        if (!tab) return;
        event.preventDefault();
        activeTab = tab.getAttribute('data-rs-tab');
        render();
    });

    function load(autoFill) {
        if (!callId) { data = null; body.textContent = ''; say(s('PickCall')); return; }
        say(s('Loading'));
        const url = baseUrl + (baseUrl.indexOf('?') >= 0 ? '&' : '?') + 'callId=' + encodeURIComponent(callId);
        fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) { if (!response.ok) throw new Error(String(response.status)); return response.json(); })
            .then(function (json) {
                data = json;
                say(json.warnings && json.warnings.length ? fmt('PartialSources', json.warnings.join(', ')) : '');
                render();
                if (autoFill && mode === 'run') fillRunFromCall(true);
            })
            .catch(function () { data = null; body.textContent = ''; say(s('LoadFailed'), 'error'); });
    }

    const reload = panel.querySelector('[data-rs-reload]');
    if (reload) reload.addEventListener('click', function (event) { event.preventDefault(); load(false); });

    const fill = panel.querySelector('[data-rs-fill]');
    if (fill) fill.addEventListener('click', function () { if (data) fillRunFromCall(false); else say(s('PickCall')); });

    // Run reports pick their call on the form: follow the picker.
    const picker = form ? form.querySelector('select[name="CallId"]') : null;
    if (picker && mode === 'run') {
        picker.addEventListener('change', function () {
            callId = picker.value;
            if (fill) fill.disabled = !callId;
            // A newly picked call fills only what is still blank, so the author's entries are never replaced.
            load(!!callId);
        });
    }

    // A refresh re-reads the stored draft: warn before leaving edits made on this page unsaved.
    let dirty = false;
    if (form) form.addEventListener('input', function () { dirty = true; });
    document.querySelectorAll('form[data-confirm-unsaved]').forEach(function (other) {
        other.addEventListener('submit', function (event) {
            if (dirty && !window.confirm(other.getAttribute('data-confirm-unsaved'))) event.preventDefault();
        });
    });

    load(panel.getAttribute('data-auto-fill') === 'true');
}());
