(function () {
    'use strict';
    const rows = document.getElementById('record-participant-rows');
    const template = document.getElementById('record-participant-template');
    const button = document.getElementById('add-record-participant');
    if (!rows || !template || !button) return;
    function reindexRows() {
        // MVC collection binding stops at a gap, so keep the remaining rows consecutive.
        Array.from(rows.children).forEach(function (row, index) {
            row.querySelectorAll('[name]').forEach(function (element) {
                element.name = element.name.replace(/^ParticipantRows\[(?:\d+|__index__)\]/, 'ParticipantRows[' + index + ']');
                if (element.id) element.id = element.id.replace(/^ParticipantRows_(?:\d+|__index__)__/, 'ParticipantRows_' + index + '__');
            });
        });
    }
    button.addEventListener('click', function () {
        rows.appendChild(template.content.cloneNode(true));
        reindexRows();
        rows.lastElementChild.querySelector('select').focus();
    });
    rows.addEventListener('click', function (event) {
        const remove = event.target.closest('[data-remove-record-participant]');
        if (!remove) return;
        const row = remove.closest('tr');
        const adjacentRow = row.nextElementSibling || row.previousElementSibling;
        row.remove();
        reindexRows();
        (adjacentRow ? adjacentRow.querySelector('select') : button).focus();
        // Removal changes the draft just like editing a participant field.
        rows.dispatchEvent(new Event('input', { bubbles: true }));
    });
}());
