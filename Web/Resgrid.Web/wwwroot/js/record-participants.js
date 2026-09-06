(function () {
    'use strict';
    const rows = document.getElementById('record-participant-rows');
    const template = document.getElementById('record-participant-template');
    const button = document.getElementById('add-record-participant');
    if (!rows || !template || !button) return;
    button.addEventListener('click', function () {
        const index = rows.children.length;
        const fragment = template.content.cloneNode(true);
        fragment.querySelectorAll('[name]').forEach(function (element) {
            element.name = element.name.replace('__index__', String(index));
        });
        rows.appendChild(fragment);
        rows.lastElementChild.querySelector('select').focus();
    });
}());
