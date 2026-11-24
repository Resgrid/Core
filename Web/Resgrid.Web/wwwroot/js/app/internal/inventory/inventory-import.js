(function () {
    'use strict';
    // Inventory -> Import review: "Only rows needing attention" hides the rows that will import cleanly.
    const toggle = document.getElementById('inventory-import-problems');
    const table = document.getElementById('inventory-import-rows');
    if (toggle && table) {
        toggle.addEventListener('change', () => {
            table.querySelectorAll('tbody tr').forEach(row => {
                row.hidden = toggle.checked && row.dataset.importProblem !== 'true';
            });
        });
    }
    // Applying can take a while for a large file; one click is enough. Bound after inventory-modern.js, so a submit the
    // protected-data step-up holds back (defaultPrevented) keeps its button until it is really sent.
    $(function () {
        document.querySelectorAll('form.inventory-import-apply').forEach(form => form.addEventListener('submit', event => {
            if (event.defaultPrevented) return;
            const button = form.querySelector('button[type="submit"]');
            if (button) setTimeout(() => { button.disabled = true; }, 0);
        }));
    });
})();
