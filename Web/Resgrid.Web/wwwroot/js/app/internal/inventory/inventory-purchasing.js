(function () {
    'use strict';
    let identity = 0;
    document.querySelectorAll('form.m4-lines-form').forEach(form => {
        const lines = form.querySelector('.m4-lines');
        function refresh() {
            Array.from(lines.children).forEach((row, index) => {
                row.querySelectorAll('[name]').forEach(field => { field.name = field.name.replace(/^Lines\[\d+\]/, 'Lines[' + index + ']'); });
                row.querySelector('.m4-remove-line').disabled = lines.children.length === 1;
            });
            form.querySelectorAll('.m4-add-line, .m4-copy-line').forEach(button => { button.disabled = lines.children.length >= 100; });
        }
        form.addEventListener('click', event => {
            const remove = event.target.closest('.m4-remove-line');
            const add = event.target.closest('.m4-add-line');
            const copy = event.target.closest('.m4-copy-line');
            if (remove && lines.children.length > 1) remove.closest('.m4-line').remove();
            if ((add || copy) && lines.children.length < 100) {
                const source = copy ? copy.closest('.m4-line') : lines.firstElementChild;
                const row = source.cloneNode(true);
                if (copy && form.dataset.receipt === 'true') {
                    source.querySelectorAll('select[name]').forEach(field => {
                        row.querySelector('select[name="' + field.name + '"]').value = field.value;
                    });
                }
                row.querySelectorAll('[name]').forEach(field => {
                    const label = field.id && row.querySelector('label[for="' + field.id + '"]');
                    if (field.id) { field.id = 'purchase-added-' + (++identity); if (label) label.htmlFor = field.id; }
                    if (form.dataset.receipt === 'true') {
                        if (field.name.endsWith('.Quantity')) field.value = '1';
                        else if (field.name.includes('.Asset.')) field.value = '';
                    } else {
                        field.value = field.name.endsWith('.QuantityOrdered') ? '1' : field.name.endsWith('.UnitCost') ? '0' : '';
                    }
                });
                lines.appendChild(row);
            }
            refresh();
        });
        refresh();
    });
})();
