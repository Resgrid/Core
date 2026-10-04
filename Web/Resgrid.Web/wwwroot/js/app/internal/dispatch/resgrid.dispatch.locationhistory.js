// Call location history widget (Views/Shared/_CallLocationHistory): previous calls at a location, on the call, contact
// and occupancy pages. Every value is placed with textContent, never as HTML.
(function () {
    'use strict';

    if (window.resgridLocationHistory) {
        window.resgridLocationHistory.scan();
        return;
    }

    var badgeClasses = { address: 'label-primary', similar: 'label-warning', nearby: 'label-info', contact: 'label-success' };

    function get(obj, key) {
        if (!obj) { return undefined; }
        if (obj[key] !== undefined) { return obj[key]; }
        return obj[key.charAt(0).toLowerCase() + key.slice(1)];
    }

    function el(tag, value, className) {
        var node = document.createElement(tag);
        if (className) { node.className = className; }
        if (value !== null && value !== undefined && value !== '') { node.textContent = value; }
        return node;
    }

    function format(template, value) {
        return (template || '').replace('{0}', value);
    }

    function clear(node) {
        while (node.firstChild) { node.removeChild(node.firstChild); }
    }

    function strings(container) {
        try {
            return JSON.parse(container.getAttribute('data-strings') || '{}');
        } catch (e) {
            return {};
        }
    }

    function notesCell(entry, s, detailRow) {
        var cell = el('td');
        var notes = get(entry, 'Notes') || [];
        var closing = get(entry, 'CompletedNotes');
        if (notes.length === 0 && !closing) {
            cell.appendChild(el('span', '—', 'text-muted'));
            return cell;
        }

        var button = el('button', format(s.ShowNotes, notes.length), 'btn btn-xs btn-default');
        button.type = 'button';
        button.addEventListener('click', function () {
            var open = detailRow.style.display !== 'none';
            detailRow.style.display = open ? 'none' : '';
            button.textContent = open ? format(s.ShowNotes, notes.length) : s.HideNotes;
            if (!open) {
                // On a narrow screen the table scrolls sideways; keep the notes inside the visible width.
                var scroller = button.closest ? button.closest('.table-responsive') : null;
                var content = detailRow.querySelector('.rg-location-history-notes');
                if (scroller && content) { content.style.maxWidth = Math.max(240, scroller.clientWidth - 24) + 'px'; }
            }
        });
        cell.appendChild(button);
        return cell;
    }

    function detail(entry, s, columns) {
        var row = el('tr', null, 'rg-location-history-detail');
        row.style.display = 'none';
        var td = el('td');
        td.colSpan = columns;
        var cell = el('div', null, 'rg-location-history-notes');
        cell.style.position = 'sticky';
        cell.style.left = '0';
        td.appendChild(cell);

        var closing = get(entry, 'CompletedNotes');
        if (closing) {
            cell.appendChild(el('strong', s.ClosingNotes));
            var closingText = el('p', closing);
            closingText.style.whiteSpace = 'pre-wrap';
            cell.appendChild(closingText);
        }

        var notes = get(entry, 'Notes') || [];
        if (notes.length > 0) {
            cell.appendChild(el('strong', s.CallNotes));
            var list = el('ul', null, 'list-unstyled');
            notes.forEach(function (note) {
                var item = el('li', null, 'm-b-xs');
                item.appendChild(el('small', get(note, 'Timestamp') + ' — ' + get(note, 'Name'), 'text-muted'));
                var body = el('div', get(note, 'Note'));
                body.style.whiteSpace = 'pre-wrap';
                item.appendChild(body);
                list.appendChild(item);
            });
            cell.appendChild(list);
        } else if (!closing) {
            cell.appendChild(el('p', s.NoNotes, 'text-muted'));
        }

        row.appendChild(td);
        return row;
    }

    function render(container, data, s) {
        var body = container.querySelector('.rg-location-history-body');
        clear(body);

        if (!get(data, 'AddressMatchingAvailable')) {
            body.appendChild(el('div', s.AddressMatchingOff, 'alert alert-info'));
        } else if (!get(data, 'IndexComplete')) {
            body.appendChild(el('p', s.IndexIncomplete, 'text-muted small'));
        }

        var interpreted = get(data, 'InterpretedAddress');
        if (interpreted) {
            var matched = el('p', null, 'small');
            matched.appendChild(el('span', s.MatchedAs + ': ', 'text-muted'));
            matched.appendChild(el('strong', interpreted));
            body.appendChild(matched);
        }

        var entries = get(data, 'Entries') || [];
        if (entries.length === 0) {
            body.appendChild(el('p', s.NoCalls, 'text-muted'));
            return;
        }

        var filter = el('input', null, 'form-control input-sm m-b-sm');
        filter.type = 'text';
        filter.placeholder = s.FilterPlaceholder || '';
        body.appendChild(filter);

        var wrapper = el('div', null, 'table-responsive');
        var table = el('table', null, 'table table-striped table-condensed');
        var head = el('thead');
        var headRow = el('tr');
        [s.ColumnCall, s.ColumnLoggedOn, s.ColumnName, s.ColumnAddress, s.ColumnState, s.ColumnPriority, s.ColumnMatch, s.ColumnNotes]
            .forEach(function (title) { headRow.appendChild(el('th', title)); });
        head.appendChild(headRow);
        table.appendChild(head);

        var callUrl = container.getAttribute('data-call-url') || '';
        var tbody = el('tbody');
        var pairs = [];
        entries.forEach(function (entry) {
            var row = el('tr');

            var callCell = el('td');
            var link = el('a', get(entry, 'Number') || String(get(entry, 'CallId')));
            link.href = callUrl + (callUrl.indexOf('?') >= 0 ? '&' : '?') + 'callId=' + encodeURIComponent(get(entry, 'CallId'));
            callCell.appendChild(link);
            row.appendChild(callCell);

            row.appendChild(el('td', get(entry, 'LoggedOn')));

            var nameCell = el('td');
            nameCell.appendChild(el('div', get(entry, 'Name')));
            if (get(entry, 'Nature')) {
                var nature = el('small', get(entry, 'Nature'), 'text-muted');
                nature.style.whiteSpace = 'pre-wrap';
                nameCell.appendChild(nature);
            }
            row.appendChild(nameCell);

            var addressCell = el('td', get(entry, 'Address'));
            if (get(entry, 'Distance')) {
                addressCell.appendChild(el('br'));
                addressCell.appendChild(el('small', get(entry, 'Distance'), 'text-muted'));
            }
            row.appendChild(addressCell);

            row.appendChild(el('td', get(entry, 'State'), get(entry, 'IsActive') ? 'text-danger' : null));

            var priorityCell = el('td');
            var priority = el('span', get(entry, 'PriorityName'), 'label');
            priority.style.backgroundColor = get(entry, 'PriorityColor') || '#777777';
            priorityCell.appendChild(priority);
            row.appendChild(priorityCell);

            var matchCell = el('td');
            (get(entry, 'Matches') || []).forEach(function (match) {
                matchCell.appendChild(el('span', get(match, 'Label'), 'label ' + (badgeClasses[get(match, 'Kind')] || 'label-default')));
                matchCell.appendChild(document.createTextNode(' '));
            });
            row.appendChild(matchCell);

            var detailRow = detail(entry, s, 8);
            row.appendChild(notesCell(entry, s, detailRow));

            tbody.appendChild(row);
            tbody.appendChild(detailRow);
            pairs.push({ row: row, detail: detailRow, text: (row.textContent + ' ' + detailRow.textContent).toLowerCase() });
        });

        table.appendChild(tbody);
        wrapper.appendChild(table);
        body.appendChild(wrapper);

        filter.addEventListener('input', function () {
            var term = filter.value.trim().toLowerCase();
            pairs.forEach(function (pair) {
                var show = term.length === 0 || pair.text.indexOf(term) >= 0;
                pair.row.style.display = show ? '' : 'none';
                if (!show) { pair.detail.style.display = 'none'; }
            });
        });

        if (get(data, 'HasMore')) {
            body.appendChild(el('p', s.HasMore, 'text-muted small'));
        }
    }

    function load(container) {
        if (!container || container.getAttribute('data-loaded') === 'true') { return; }
        container.setAttribute('data-loaded', 'true');

        var s = strings(container);
        var body = container.querySelector('.rg-location-history-body');
        clear(body);
        var loading = el('p', null, 'text-muted');
        loading.appendChild(el('i', null, 'fa fa-spinner fa-spin'));
        loading.appendChild(document.createTextNode(' ' + (s.Loading || '')));
        body.appendChild(loading);

        fetch(container.getAttribute('data-url'), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (response) {
                if (!response.ok) { throw new Error('HTTP ' + response.status); }
                return response.json();
            })
            .then(function (data) { render(container, data, s); })
            .catch(function () {
                clear(body);
                body.appendChild(el('p', s.LoadFailed, 'text-danger'));
                container.setAttribute('data-loaded', 'false');
            });
    }

    function wire(container) {
        if (container.getAttribute('data-wired') === 'true') { return; }
        container.setAttribute('data-wired', 'true');

        if (container.getAttribute('data-autoload') === 'true') {
            load(container);
            return;
        }

        var selector = container.getAttribute('data-trigger');
        if (!selector) { return; }
        var triggers = document.querySelectorAll(selector);
        for (var i = 0; i < triggers.length; i++) {
            triggers[i].addEventListener('click', function () { load(container); });
            var tab = triggers[i].parentNode;
            if (tab && tab.classList && tab.classList.contains('active')) { load(container); }
        }
    }

    function scan() {
        var containers = document.querySelectorAll('.rg-location-history');
        for (var i = 0; i < containers.length; i++) { wire(containers[i]); }
    }

    window.resgridLocationHistory = { load: load, scan: scan };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', scan);
    } else {
        scan();
    }
})();
