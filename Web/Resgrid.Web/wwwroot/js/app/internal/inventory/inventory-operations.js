(function () {
    'use strict';
    const form = document.getElementById('inventory-report-form');
    if (!form) return;
    const kind = form.querySelector('[name="Kind"]');
    function range() {
        const snapshot = ['0', '3', '6'].includes(kind.value);
        form.querySelectorAll('[name="FromUtc"], [name="UntilUtc"]').forEach(field => { field.disabled = snapshot; });
    }
    kind.addEventListener('change', range);
    const person = form.querySelector('[name="UserId"]');
    const unit = form.querySelector('[name="UnitId"]');
    if (person && unit) {
        person.addEventListener('change', () => { if (person.value) unit.value = ''; });
        unit.addEventListener('change', () => { if (unit.value) person.value = ''; });
    }
    range();
})();
