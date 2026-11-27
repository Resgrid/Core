/*
 * Training T3 Programs pages (LmsPrograms/*): asks before a destructive form submits (data-confirm), shows the requirement editor's
 * fields for the chosen requirement type and recurrence, and adds or removes learning-path step rows. Progressive enhancement only:
 * every form posts and validates on the server without it.
 */
var resgrid;
(function (resgrid) {
    var lmsprograms;
    (function (lmsprograms) {
        function confirmations() {
            document.addEventListener('submit', function (e) {
                var form = e.target;
                var button = form && form.querySelector ? form.querySelector('[data-confirm]') : null;
                var message = form && form.getAttribute ? form.getAttribute('data-confirm') : null;
                if (!message && button) {
                    message = button.getAttribute('data-confirm');
                }
                if (message && !window.confirm(message)) {
                    e.preventDefault();
                }
            }, true);
        }

        function toggle(root, selector, show) {
            var nodes = root.querySelectorAll(selector);
            for (var i = 0; i < nodes.length; i++) {
                nodes[i].hidden = !show;
            }
        }

        function requirementEditor() {
            var form = document.getElementById('lms-requirement-form');
            if (!form) {
                return;
            }
            var kind = form.querySelector('[name="Kind"]');
            var recurrence = form.querySelector('[name="Recurrence"]');
            function refresh() {
                var k = kind ? kind.value : '0';
                toggle(form, '[data-kind]', false);
                toggle(form, '[data-kind~="' + k + '"]', true);
                toggle(form, '[data-recurrence]', recurrence && recurrence.value === '1');
            }
            if (kind) {
                kind.addEventListener('change', refresh);
            }
            if (recurrence) {
                recurrence.addEventListener('change', refresh);
            }
            refresh();
        }

        function pathEditor() {
            var table = document.getElementById('lms-path-steps');
            var add = document.getElementById('lms-path-add-step');
            var template = document.getElementById('lms-path-step-template');
            if (!table || !add || !template) {
                return;
            }
            function renumber() {
                var rows = table.querySelectorAll('tbody tr');
                for (var i = 0; i < rows.length; i++) {
                    var number = rows[i].querySelector('.lms-step-number');
                    if (number) {
                        number.textContent = String(i + 1);
                    }
                    var fields = rows[i].querySelectorAll('[data-name]');
                    for (var j = 0; j < fields.length; j++) {
                        fields[j].name = 'Steps[' + i + '].' + fields[j].getAttribute('data-name');
                    }
                }
            }
            function wire(row) {
                var kind = row.querySelector('[data-name="Kind"]');
                var remove = row.querySelector('.lms-step-remove');
                function refresh() {
                    toggle(row, '[data-step-kind]', false);
                    toggle(row, '[data-step-kind="' + (kind ? kind.value : '0') + '"]', true);
                }
                if (kind) {
                    kind.addEventListener('change', refresh);
                }
                if (remove) {
                    remove.addEventListener('click', function () {
                        row.parentNode.removeChild(row);
                        renumber();
                    });
                }
                refresh();
            }
            var existing = table.querySelectorAll('tbody tr');
            for (var i = 0; i < existing.length; i++) {
                wire(existing[i]);
            }
            add.addEventListener('click', function () {
                var row = template.content ? template.content.firstElementChild.cloneNode(true) : null;
                if (!row) {
                    return;
                }
                table.querySelector('tbody').appendChild(row);
                wire(row);
                renumber();
            });
            renumber();
        }

        function init() {
            confirmations();
            requirementEditor();
            pathEditor();
        }

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', init);
        } else {
            init();
        }
    })(lmsprograms = resgrid.lmsprograms || (resgrid.lmsprograms = {}));
})(resgrid || (resgrid = {}));
