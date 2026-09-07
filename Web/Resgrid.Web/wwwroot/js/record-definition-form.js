// Definition-driven authoring form (RMS plan section 4.1, RMS-1B): repeating rows, list-index renumbering for
// MVC binding, signature acknowledgements, date/time offsets, and the bounded Show/Require rules mirrored from
// the schema so the form reacts before the server evaluates the same rules at finalize.
(function () {
    'use strict';
    var form = document.getElementById('record-edit-form');
    var schemaNode = document.getElementById('record-definition-schema');
    if (!form || !schemaNode) return;
    var schema;
    try { schema = JSON.parse(schemaNode.textContent || '{}'); } catch (_) { schema = { sections: [] }; }
    var sections = schema.sections || [];
    var fieldsByKey = {};
    sections.forEach(function (s) { (s.fields || []).forEach(function (f) { fieldsByKey[(f.key || '').toLowerCase()] = { field: f, section: s }; }); });

    function renumber() {
        var index = 0;
        form.querySelectorAll('.rms-def-field').forEach(function (group) {
            group.querySelectorAll('[name^="Values["]').forEach(function (input) {
                input.name = input.name.replace(/^Values\[[^\]]*\]/, 'Values[' + index + ']');
            });
            var signature = group.querySelector('.rms-def-signature');
            if (signature) signature.dataset.target = 'Values[' + index + '].Value';
            index++;
        });
        form.querySelectorAll('.rms-def-section[data-repeating="true"]').forEach(function (section) {
            var ordinal = 0;
            section.querySelectorAll('.rms-def-rows .rms-def-row').forEach(function (row) {
                var key = row.dataset.rowKey;
                row.querySelectorAll('.rms-def-rowkey').forEach(function (input) { input.value = key; });
                row.querySelectorAll('.rms-def-ordinal').forEach(function (input) { input.value = ordinal; });
                ordinal++;
            });
        });
    }

    function newRowKey() { return 'row-' + Math.random().toString(36).substring(2, 10); }

    form.addEventListener('click', function (event) {
        var add = event.target.closest('.rms-def-add-row');
        if (add) {
            var section = add.closest('.rms-def-section');
            var template = section.querySelector('.rms-def-row-template');
            var rows = section.querySelector('.rms-def-rows');
            var max = parseInt(add.dataset.maxRows || '0', 10);
            if (max && rows.querySelectorAll('.rms-def-row').length >= max) return;
            var key = newRowKey();
            var html = template.innerHTML.replace(/__ROWKEY__/g, key).replace(/__INDEX__/g, '0');
            var holder = document.createElement('div');
            holder.innerHTML = html;
            var row = holder.firstElementChild;
            rows.appendChild(row);
            renumber();
            form.dispatchEvent(new Event('input', { bubbles: true }));
            return;
        }
        var remove = event.target.closest('.rms-def-remove-row');
        if (remove) {
            var row = remove.closest('.rms-def-row');
            var rows = row.parentElement;
            row.remove();
            if (rows.querySelectorAll('.rms-def-row').length === 0) rows.closest('.rms-def-section').querySelector('.rms-def-add-row').click();
            renumber();
            form.dispatchEvent(new Event('input', { bubbles: true }));
        }
    });

    // A signature is an acknowledgement: the checkbox writes the statement into the posted value.
    form.addEventListener('change', function (event) {
        var box = event.target.closest('.rms-def-signature');
        if (!box) return;
        var hidden = box.closest('.rms-def-field').querySelector('input[type=hidden][name$=".Value"]');
        if (hidden) hidden.value = box.checked ? 'Acknowledged ' + new Date().toISOString() : '';
    });

    // Local date/time inputs post the browser's offset so the server can store UTC plus the captured offset.
    function stampOffsets() {
        form.querySelectorAll('.rms-def-offset').forEach(function (input) { input.value = String(-new Date().getTimezoneOffset()); });
    }
    stampOffsets();

    // ---- rules --------------------------------------------------------------------------------------
    // Mirrors RecordTypedValuesService.EvaluateRules: section rules and scalar-field rules see scalars only; a rule on
    // a field inside a repeating section evaluates per row and may look at its own row's cells first.
    function readGroup(group) {
        if (!group) return { value: null, values: [] };
        var multi = group.querySelector('select[multiple]');
        if (multi) { var picked = Array.from(multi.selectedOptions).map(function (o) { return o.value; }); return { value: picked.join(','), values: picked }; }
        var control = group.querySelector('[name$=".Value"], [name$=".ReferenceId"]');
        var value = control ? control.value : null;
        return { value: value, values: value ? [value] : [] };
    }
    function fieldGroup(fieldKey, rowNode) {
        var key = (fieldKey || '').toLowerCase();
        if (rowNode) {
            var own = Array.from(rowNode.querySelectorAll('.rms-def-field')).find(function (g) { return (g.dataset.fieldKey || '').toLowerCase() === key; });
            if (own) return own;
        }
        return Array.from(form.querySelectorAll('.rms-def-field')).find(function (g) { return (g.dataset.fieldKey || '').toLowerCase() === key && !g.closest('.rms-def-row') && !g.closest('template'); }) || null;
    }
    function currentValue(fieldKey, rowNode) {
        if (!fieldsByKey[(fieldKey || '').toLowerCase()]) return { value: null, values: [] };
        return readGroup(fieldGroup(fieldKey, rowNode));
    }
    function numeric(v) { var n = parseFloat(v); return isNaN(n) ? null : n; }
    function evaluate(condition, depth, rowNode) {
        if (!condition) return true;
        if (depth > 6) return false;
        var op = condition.operator;
        if (op === 20 || op === 'And') return (condition.conditions || []).every(function (c) { return evaluate(c, depth + 1, rowNode); });
        if (op === 21 || op === 'Or') return (condition.conditions || []).some(function (c) { return evaluate(c, depth + 1, rowNode); });
        var current = currentValue(condition.fieldKey, rowNode);
        var empty = !current.value && current.values.length === 0;
        var eq = function (a, b) { if (a == null || b == null) return a == b; var x = numeric(a), y = numeric(b); if (x !== null && y !== null) return x === y; return String(a).trim().toLowerCase() === String(b).trim().toLowerCase(); };
        switch (op) {
            case 5: case 'IsEmpty': return empty;
            case 6: case 'IsNotEmpty': return !empty;
            case 1: case 'Equals': return current.values.some(function (v) { return eq(v, condition.value); });
            case 2: case 'NotEquals': return !current.values.some(function (v) { return eq(v, condition.value); });
            case 3: case 'InSet': return current.values.some(function (v) { return (condition.values || []).some(function (x) { return eq(v, x); }); });
            case 4: case 'NotInSet': return !current.values.some(function (v) { return (condition.values || []).some(function (x) { return eq(v, x); }); });
            case 7: case 'InRange': {
                var n = numeric(current.value);
                if (n !== null) return (condition.min == null || n >= condition.min) && (condition.max == null || n <= condition.max);
                var d = Date.parse(current.value);
                if (!isNaN(d)) return (!condition.minDate || d >= Date.parse(condition.minDate)) && (!condition.maxDate || d <= Date.parse(condition.maxDate));
                return false;
            }
            default: return false;
        }
    }
    function isShow(rule) { return rule.effect === 1 || rule.effect === 'Show'; }
    function isRequire(rule) { return rule.effect === 2 || rule.effect === 'Require'; }
    function applyField(field, group, visible, rowNode) {
        var shown = visible && (field.rules || []).filter(isShow).every(function (r) { return evaluate(r.condition, 0, rowNode); });
        var required = field.required || field.requiredToFinalize || (field.rules || []).filter(isRequire).some(function (r) { return evaluate(r.condition, 0, rowNode); });
        group.hidden = !shown;
        var label = group.querySelector('label.control-label');
        if (label) label.classList.toggle('required', required);
    }
    function applyRules() {
        sections.forEach(function (section) {
            var node = form.querySelector('.rms-def-section[data-section-key="' + section.key + '"]');
            if (!node) return;
            var visible = (section.rules || []).filter(isShow).every(function (r) { return evaluate(r.condition, 0, null); });
            node.hidden = !visible;
            if (section.repeating) {
                node.querySelectorAll('.rms-def-rows > .rms-def-row').forEach(function (rowNode) {
                    (section.fields || []).forEach(function (field) {
                        rowNode.querySelectorAll('.rms-def-field[data-field-key="' + field.key + '"]').forEach(function (group) { applyField(field, group, visible, rowNode); });
                    });
                });
                return;
            }
            (section.fields || []).forEach(function (field) {
                node.querySelectorAll('.rms-def-field[data-field-key="' + field.key + '"]').forEach(function (group) { applyField(field, group, visible, null); });
            });
        });
    }
    form.addEventListener('input', applyRules);
    form.addEventListener('change', applyRules);
    applyRules();
    renumber();
    form.addEventListener('submit', function () { stampOffsets(); renumber(); });
})();
