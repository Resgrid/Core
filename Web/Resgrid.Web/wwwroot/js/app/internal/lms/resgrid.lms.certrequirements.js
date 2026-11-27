/*
 * Round 9A: the certification requirement-set editor (LmsPrograms/EditCertificationRequirements). For each item row, shows the fields of
 * the chosen kind (the target list filtered to that kind, the hours of a category, the evaluation count and skill code, the supervised
 * practical box for trainings and courses), and asks before the set is removed (data-confirm). Progressive enhancement only: the form
 * posts and is validated on the server without it.
 */
var resgrid;
(function (resgrid) {
    var certrequirements;
    (function (certrequirements) {
        function show(nodes, visible) {
            for (var i = 0; i < nodes.length; i++) {
                nodes[i].hidden = !visible;
                var inputs = nodes[i].querySelectorAll('input, select');
                for (var j = 0; j < inputs.length; j++) {
                    inputs[j].disabled = !visible;
                }
            }
        }

        function refreshRow(row) {
            var kind = row.querySelector('[data-item-kind]');
            var value = kind ? kind.value : '-1';
            var target = row.querySelector('[data-item-target]');
            if (target) {
                var groups = target.querySelectorAll('optgroup');
                var keep = false;
                for (var i = 0; i < groups.length; i++) {
                    var match = groups[i].getAttribute('data-kind') === value;
                    groups[i].hidden = !match;
                    groups[i].disabled = !match;
                    if (match) {
                        var options = groups[i].querySelectorAll('option');
                        for (var o = 0; o < options.length; o++) {
                            if (options[o].selected) {
                                keep = true;
                            }
                        }
                    }
                }
                if (!keep) {
                    target.value = '';
                }
            }
            show(row.querySelectorAll('[data-for-kinds]'), false);
            var fields = row.querySelectorAll('[data-for-kinds]');
            for (var f = 0; f < fields.length; f++) {
                var kinds = (fields[f].getAttribute('data-for-kinds') || '').split(',');
                show([fields[f]], kinds.indexOf(value) >= 0);
            }
        }

        function init() {
            var form = document.getElementById('lms-cert-requirements-form');
            if (form) {
                var rows = form.querySelectorAll('[data-item-row]');
                for (var i = 0; i < rows.length; i++) {
                    (function (row) {
                        var kind = row.querySelector('[data-item-kind]');
                        if (kind) {
                            kind.addEventListener('change', function () { refreshRow(row); });
                        }
                        refreshRow(row);
                    })(rows[i]);
                }
            }

            document.addEventListener('submit', function (e) {
                var target = e.target;
                var message = target && target.getAttribute ? target.getAttribute('data-confirm') : null;
                if (message && !window.confirm(message)) {
                    e.preventDefault();
                }
            }, true);
        }

        certrequirements.init = init;
    })(certrequirements = resgrid.certrequirements || (resgrid.certrequirements = {}));
})(resgrid || (resgrid = {}));

document.addEventListener('DOMContentLoaded', function () { resgrid.certrequirements.init(); });
