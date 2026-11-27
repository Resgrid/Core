/*
 * Training T4 Skills pages (LmsSkills/*): asks before a destructive form submits (data-confirm), the skill sheet step editor (rows built
 * from the draft's JSON and written back to it on save), the task book task rows, and the evaluation timer on the run page. The run page
 * itself posts plain form fields; the step editor needs script (a notice says so without it).
 */
var resgrid;
(function (resgrid) {
    var lmsskills;
    (function (lmsskills) {
        function confirmations() {
            document.addEventListener('submit', function (e) {
                var form = e.target;
                var submitter = e.submitter || null;
                var message = form && form.getAttribute ? form.getAttribute('data-confirm') : null;
                if (!message && submitter && submitter.getAttribute) {
                    // Only the button that submitted asks (Save and Pass All never ask; Submit does).
                    message = submitter.getAttribute('data-confirm');
                } else if (!message && !submitter && form && form.querySelectorAll) {
                    // Browsers without SubmitEvent.submitter: a form with exactly one confirming button asks.
                    var buttons = form.querySelectorAll('[data-confirm]');
                    message = buttons.length === 1 && form.querySelectorAll('button[type="submit"]').length === 1 ? buttons[0].getAttribute('data-confirm') : null;
                }
                if (message && !window.confirm(message)) {
                    e.preventDefault();
                }
            }, true);
        }

        function el(tag, attrs, text) {
            var node = document.createElement(tag);
            if (attrs) {
                for (var key in attrs) {
                    if (Object.prototype.hasOwnProperty.call(attrs, key) && attrs[key] !== null && attrs[key] !== undefined) {
                        node.setAttribute(key, attrs[key]);
                    }
                }
            }
            if (text) {
                node.textContent = text;
            }
            return node;
        }

        function newKey(prefix) {
            return prefix + Math.random().toString(36).substring(2, 8);
        }

        function labelled(labelText, input) {
            var wrap = el('div', { 'class': 'form-group' });
            var id = newKey('f');
            input.id = id;
            var label = el('label', { 'for': id }, labelText);
            wrap.appendChild(label);
            wrap.appendChild(input);
            return wrap;
        }

        function check(labelText, checked, name) {
            var wrap = el('div', { 'class': 'checkbox' });
            var label = el('label');
            var box = el('input', { type: 'checkbox', 'data-field': name });
            box.checked = !!checked;
            label.appendChild(box);
            label.appendChild(document.createTextNode(' ' + labelText));
            wrap.appendChild(label);
            return wrap;
        }

        function numberInput(value, field, step) {
            var input = el('input', { type: 'number', 'class': 'form-control', 'data-field': field, step: step || 'any' });
            if (value !== null && value !== undefined) {
                input.value = value;
            }
            return input;
        }

        /* ---- Skill sheet step editor ---- */

        function stepEditor() {
            var host = document.getElementById('skill-step-editor');
            var hidden = document.getElementById('skill-content-json');
            var form = document.getElementById('skill-draft-form');
            var add = document.getElementById('skill-step-add');
            if (!host || !hidden || !form) {
                return;
            }
            var L = function (k) { return host.getAttribute('data-l-' + k) || k; };
            var types = JSON.parse(host.getAttribute('data-types') || '[]');
            var ops = JSON.parse(host.getAttribute('data-ops') || '[]');
            var content;
            try {
                content = JSON.parse(hidden.value || '{}');
            } catch (err) {
                content = {};
            }
            var steps = (content && content.Steps) || [];

            function optionsText(options) {
                return (options || []).map(function (o) {
                    return (o.Label || '') + ' | ' + (o.Points || 0) + (o.Passing === false ? ' | fail' : '');
                }).join('\n');
            }

            function parseOptions(text) {
                return (text || '').split('\n').map(function (line) { return line.trim(); }).filter(function (line) { return line.length > 0; }).map(function (line, i) {
                    var parts = line.split('|').map(function (p) { return p.trim(); });
                    return { Key: 'o' + (i + 1), Label: parts[0], Points: parseFloat(parts[1]) || 0, Passing: (parts[2] || '').toLowerCase() !== 'fail' };
                });
            }

            function row(step) {
                var card = el('div', { 'class': 'rgw-builder-item panel panel-default', 'data-key': step.Key || newKey('s') });
                var body = el('div', { 'class': 'panel-body' });
                card.appendChild(body);

                var head = el('div', { 'class': 'row' });
                var typeSelect = el('select', { 'class': 'form-control', 'data-field': 'Type' });
                types.forEach(function (t) {
                    var option = el('option', { value: t.v }, t.n);
                    if (t.v === (step.Type || 0)) { option.selected = true; }
                    typeSelect.appendChild(option);
                });
                var typeCol = el('div', { 'class': 'col-sm-3' });
                typeCol.appendChild(labelled(L('type'), typeSelect));
                var labelInput = el('input', { 'class': 'form-control', 'data-field': 'Label', maxlength: '300' });
                labelInput.value = step.Label || '';
                var labelCol = el('div', { 'class': 'col-sm-7' });
                labelCol.appendChild(labelled(L('label'), labelInput));
                var keyCol = el('div', { 'class': 'col-sm-2' });
                var keyInput = el('input', { 'class': 'form-control', readonly: 'readonly' });
                keyInput.value = card.getAttribute('data-key');
                keyCol.appendChild(labelled(L('key'), keyInput));
                head.appendChild(typeCol);
                head.appendChild(labelCol);
                head.appendChild(keyCol);
                body.appendChild(head);

                var help = el('textarea', { 'class': 'form-control', rows: '1', 'data-field': 'Help', maxlength: '4000' });
                help.value = step.Help || '';
                body.appendChild(labelled(L('help'), help));

                var flags = el('div', { 'class': 'row' });
                var flagCol = el('div', { 'class': 'col-sm-8' });
                flagCol.appendChild(check(L('critical'), step.Critical, 'Critical'));
                flagCol.appendChild(check(L('required'), step.Required, 'Required'));
                flagCol.appendChild(check(L('na'), step.AllowNotApplicable, 'AllowNotApplicable'));
                flagCol.appendChild(check(L('note'), step.RequireNoteOnFail, 'RequireNoteOnFail'));
                flagCol.appendChild(check(L('evidence'), step.RequireEvidenceOnFail, 'RequireEvidenceOnFail'));
                var weightCol = el('div', { 'class': 'col-sm-4' });
                weightCol.appendChild(labelled(L('weight'), numberInput(step.Weight === undefined ? 1 : step.Weight, 'Weight', '0.01')));
                flags.appendChild(flagCol);
                flags.appendChild(weightCol);
                body.appendChild(flags);

                var options = el('textarea', { 'class': 'form-control', rows: '3', 'data-field': 'Options', 'data-for': '5,6' });
                options.value = optionsText(step.Options);
                var optionsGroup = labelled(L('options'), options);
                optionsGroup.setAttribute('data-for', '5,6');
                optionsGroup.appendChild(el('span', { 'class': 'help-block' }, L('optionshelp')));
                body.appendChild(optionsGroup);

                var rating = el('div', { 'class': 'row', 'data-for': '4' });
                var rMin = el('div', { 'class': 'col-sm-4' }); rMin.appendChild(labelled(L('rating') + ' (min)', numberInput(step.RatingMin, 'RatingMin', '1')));
                var rMax = el('div', { 'class': 'col-sm-4' }); rMax.appendChild(labelled(L('rating') + ' (max)', numberInput(step.RatingMax, 'RatingMax', '1')));
                var rPass = el('div', { 'class': 'col-sm-4' }); rPass.appendChild(labelled(L('passing'), numberInput(step.PassingRating, 'PassingRating', '1')));
                rating.appendChild(rMin); rating.appendChild(rMax); rating.appendChild(rPass);
                body.appendChild(rating);

                var bounds = el('div', { 'class': 'row', 'data-for': '11,12,13' });
                var bMin = el('div', { 'class': 'col-sm-6' }); bMin.appendChild(labelled(L('min'), numberInput(step.Min, 'Min')));
                var bMax = el('div', { 'class': 'col-sm-6' }); bMax.appendChild(labelled(L('max'), numberInput(step.Max, 'Max')));
                bounds.appendChild(bMin); bounds.appendChild(bMax);
                body.appendChild(bounds);

                var compute = el('div', { 'class': 'row', 'data-for': '11' });
                var opSelect = el('select', { 'class': 'form-control', 'data-field': 'ComputeOp' });
                ops.forEach(function (o) {
                    var option = el('option', { value: o.v }, o.n);
                    if (o.v === (step.ComputeOp || 0)) { option.selected = true; }
                    opSelect.appendChild(option);
                });
                var cOp = el('div', { 'class': 'col-sm-4' }); cOp.appendChild(labelled(L('compute'), opSelect));
                var keysInput = el('input', { 'class': 'form-control', 'data-field': 'ComputeKeys' });
                keysInput.value = (step.ComputeKeys || []).join(', ');
                var cKeys = el('div', { 'class': 'col-sm-8' }); cKeys.appendChild(labelled(L('computekeys'), keysInput));
                compute.appendChild(cOp); compute.appendChild(cKeys);
                body.appendChild(compute);

                var actions = el('div', { 'class': 'rgw-row-actions' });
                var up = el('button', { type: 'button', 'class': 'btn btn-white btn-xs' });
                up.innerHTML = '<i class="fa fa-arrow-up" aria-hidden="true"></i> ';
                up.appendChild(document.createTextNode(L('up')));
                up.addEventListener('click', function () { if (card.previousElementSibling) { host.insertBefore(card, card.previousElementSibling); } });
                var down = el('button', { type: 'button', 'class': 'btn btn-white btn-xs' });
                down.innerHTML = '<i class="fa fa-arrow-down" aria-hidden="true"></i> ';
                down.appendChild(document.createTextNode(L('down')));
                down.addEventListener('click', function () { if (card.nextElementSibling) { host.insertBefore(card.nextElementSibling, card); } });
                var remove = el('button', { type: 'button', 'class': 'btn btn-white btn-xs' });
                remove.innerHTML = '<i class="fa fa-trash" aria-hidden="true"></i> ';
                remove.appendChild(document.createTextNode(L('remove')));
                remove.addEventListener('click', function () { host.removeChild(card); });
                actions.appendChild(up); actions.appendChild(down); actions.appendChild(remove);
                body.appendChild(actions);

                function refresh() {
                    var t = typeSelect.value;
                    var parts = card.querySelectorAll('[data-for]');
                    for (var i = 0; i < parts.length; i++) {
                        parts[i].hidden = parts[i].getAttribute('data-for').split(',').indexOf(t) < 0;
                    }
                }
                typeSelect.addEventListener('change', refresh);
                refresh();
                return card;
            }

            function field(card, name) {
                return card.querySelector('[data-field="' + name + '"]');
            }

            function num(card, name) {
                var input = field(card, name);
                if (!input || input.value === '') { return null; }
                var value = parseFloat(input.value);
                return isNaN(value) ? null : value;
            }

            function collect() {
                var result = [];
                var cards = host.querySelectorAll('.rgw-builder-item');
                for (var i = 0; i < cards.length; i++) {
                    var card = cards[i];
                    var type = parseInt(field(card, 'Type').value, 10) || 0;
                    result.push({
                        Key: card.getAttribute('data-key'),
                        Type: type,
                        Label: field(card, 'Label').value,
                        Help: field(card, 'Help').value || null,
                        Critical: field(card, 'Critical').checked,
                        Required: field(card, 'Required').checked,
                        AllowNotApplicable: field(card, 'AllowNotApplicable').checked,
                        RequireNoteOnFail: field(card, 'RequireNoteOnFail').checked,
                        RequireEvidenceOnFail: field(card, 'RequireEvidenceOnFail').checked,
                        Weight: num(card, 'Weight') === null ? 1 : num(card, 'Weight'),
                        Options: (type === 5 || type === 6) ? parseOptions(field(card, 'Options').value) : [],
                        RatingMin: type === 4 ? num(card, 'RatingMin') : null,
                        RatingMax: type === 4 ? num(card, 'RatingMax') : null,
                        PassingRating: type === 4 ? num(card, 'PassingRating') : null,
                        Min: (type === 11 || type === 12 || type === 13) ? num(card, 'Min') : null,
                        Max: (type === 11 || type === 12 || type === 13) ? num(card, 'Max') : null,
                        ComputeOp: type === 11 ? (parseInt(field(card, 'ComputeOp').value, 10) || 0) : 0,
                        ComputeKeys: type === 11 ? field(card, 'ComputeKeys').value.split(',').map(function (k) { return k.trim(); }).filter(function (k) { return k.length > 0; }) : []
                    });
                }
                return result;
            }

            steps.forEach(function (step) { host.appendChild(row(step)); });
            if (add) {
                add.addEventListener('click', function () {
                    var card = row({ Type: 2, Required: true, Weight: 1 });
                    host.appendChild(card);
                    var label = field(card, 'Label');
                    if (label) { label.focus(); }
                });
            }
            form.addEventListener('submit', function () {
                hidden.value = JSON.stringify({ Schema: 1, Steps: collect() });
            });
        }

        /* ---- Task book task rows ---- */

        function taskRows() {
            var table = document.getElementById('task-rows');
            var add = document.getElementById('task-row-add');
            var template = document.getElementById('task-row-template');
            if (!table || !add || !template) {
                return;
            }
            table.addEventListener('click', function (e) {
                var button = e.target.closest ? e.target.closest('[data-remove-row]') : null;
                if (button) {
                    var row = button.closest('tr');
                    if (row && row.parentNode) { row.parentNode.removeChild(row); }
                }
            });
            add.addEventListener('click', function () {
                var body = table.querySelector('tbody');
                var key = newKey('t');
                var html = template.innerHTML.split('__KEY__').join(key);
                var holder = document.createElement('tbody');
                holder.innerHTML = html;
                while (holder.firstElementChild) {
                    body.appendChild(holder.firstElementChild);
                }
            });
        }

        /* ---- Evaluation timer ---- */

        function timer() {
            var node = document.getElementById('skill-timer');
            if (!node) {
                return;
            }
            var started = Date.parse(node.getAttribute('data-started'));
            if (isNaN(started)) {
                return;
            }
            var limit = parseInt(node.getAttribute('data-limit') || '0', 10) || 0;
            function pad(n) { return (n < 10 ? '0' : '') + n; }
            function tick() {
                var seconds = Math.max(0, Math.floor((Date.now() - started) / 1000));
                node.textContent = pad(Math.floor(seconds / 3600)) + ':' + pad(Math.floor(seconds / 60) % 60) + ':' + pad(seconds % 60);
                if (limit > 0 && seconds > limit) {
                    node.className = 'label label-danger';
                }
            }
            tick();
            window.setInterval(tick, 1000);
        }

        lmsskills.init = function () {
            confirmations();
            stepEditor();
            taskRows();
            timer();
        };
    })(lmsskills = resgrid.lmsskills || (resgrid.lmsskills = {}));
})(resgrid || (resgrid = {}));

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', resgrid.lmsskills.init);
} else {
    resgrid.lmsskills.init();
}
