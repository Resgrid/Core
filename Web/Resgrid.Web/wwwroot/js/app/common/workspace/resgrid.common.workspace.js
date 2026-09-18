/*
 * Shared workspace UX: step wizards, the review step and modal housekeeping.
 *
 * The wizards are plain markup driven. A form only needs:
 *   <form class="inventory-command rgw-wizard">
 *     <ol class="rgw-steps"></ol>                        <- indicator, filled in here
 *     <div class="rgw-step" data-title="...">...</div>   <- one per step
 *     <div class="rgw-wizard-actions">
 *        <button class="rgw-step-prev">   <button class="rgw-step-next">
 *        <button type="submit" class="rgw-step-submit">
 *
 * Native validation is turned off on wizard forms because the browser refuses to
 * report a required field that lives on a hidden step; the step validation below
 * replaces it and jumps the user back to the step that is actually incomplete.
 *
 * The submit handler is registered immediately (rather than on ready) so that it runs
 * before any page handler that posts the form, and can cancel an incomplete submit.
 */
(function () {
    'use strict';

    var text = {};
    var settings = document.getElementById('workspace-settings');
    if (settings) {
        try { text = JSON.parse(settings.textContent) || {}; } catch (error) { text = {}; }
    }

    function steps(form) {
        return Array.prototype.filter.call(form.querySelectorAll('.rgw-step'), function (step) {
            return step.closest('form') === form;
        });
    }

    function fields(step) {
        return Array.prototype.filter.call(step.querySelectorAll('input, select, textarea'), function (field) {
            return !field.disabled && field.type !== 'hidden';
        });
    }

    function group(field) {
        return field.closest('.form-group') || field.parentElement;
    }

    function clearErrors(step) {
        Array.prototype.forEach.call(step.querySelectorAll('.rgw-invalid'), function (node) {
            node.classList.remove('rgw-invalid');
        });
        var error = step.querySelector('.rgw-step-error');
        if (error) { error.textContent = ''; error.hidden = true; }
    }

    function validate(step) {
        clearErrors(step);
        var bad = fields(step).filter(function (field) { return !field.checkValidity(); });
        if (bad.length === 0) { return true; }

        bad.forEach(function (field) { group(field).classList.add('rgw-invalid'); });

        var error = step.querySelector('.rgw-step-error');
        if (!error) {
            error = document.createElement('p');
            error.className = 'rgw-step-error';
            step.insertBefore(error, step.firstChild);
        }
        error.textContent = bad[0].validationMessage || text.required || '';
        error.hidden = false;

        bad[0].focus();
        return false;
    }

    // Human readable value for the review step: the chosen option text for a
    // select, the checked state for a checkbox, the raw value for anything else.
    function display(field) {
        if (field.tagName === 'SELECT') {
            var option = field.options[field.selectedIndex];
            var label = option ? option.text.trim() : '';
            return label === '—' ? '' : label;
        }
        if (field.type === 'checkbox') { return field.checked ? (text.yes || 'Yes') : (text.no || 'No'); }
        return (field.value || '').trim();
    }

    function caption(field) {
        var owner = field.id ? field.form.querySelector('label[for="' + field.id + '"]') : null;
        if (!owner) { owner = group(field).querySelector('label'); }
        return owner ? owner.textContent.trim() : field.name;
    }

    function review(form) {
        var target = form.querySelector('.rgw-review');
        if (!target) { return; }
        target.textContent = '';

        steps(form).forEach(function (step) {
            if (step.querySelector('.rgw-review')) { return; }
            fields(step).forEach(function (field) {
                if (field.type === 'checkbox' && field.dataset.reviewSkip === 'true') { return; }
                var value = display(field);
                if (!value) { return; }

                var row = document.createElement('div');
                row.className = 'rgw-review-row';
                var term = document.createElement('dt');
                term.textContent = caption(field);
                var definition = document.createElement('dd');
                definition.textContent = value;
                row.appendChild(term);
                row.appendChild(definition);
                target.appendChild(row);
            });
        });

        if (!target.hasChildNodes()) {
            var empty = document.createElement('div');
            empty.className = 'rgw-review-row';
            empty.textContent = text.nothingEntered || '';
            target.appendChild(empty);
        }
    }

    function indicator(form, all, index) {
        var list = form.querySelector('.rgw-steps');
        if (!list) { return; }

        if (!list.hasChildNodes()) {
            all.forEach(function (step, position) {
                var entry = document.createElement('li');
                var number = document.createElement('span');
                number.className = 'rgw-step-number';
                number.textContent = String(position + 1);
                var title = document.createElement('span');
                title.textContent = step.dataset.title || '';
                entry.appendChild(number);
                entry.appendChild(title);
                list.appendChild(entry);
            });
        }

        Array.prototype.forEach.call(list.children, function (entry, position) {
            entry.className = position === index ? 'current' : (position < index ? 'done' : '');
            entry.setAttribute('aria-current', position === index ? 'step' : 'false');
        });
    }

    function show(form, index) {
        var all = steps(form);
        if (all.length === 0) { return; }

        index = Math.max(0, Math.min(index, all.length - 1));
        all.forEach(function (step, position) { step.hidden = position !== index; });
        form.dataset.step = String(index);

        indicator(form, all, index);

        var previous = form.querySelector('.rgw-step-prev');
        var next = form.querySelector('.rgw-step-next');
        var submit = form.querySelector('.rgw-step-submit');
        var last = index === all.length - 1;

        if (previous) { previous.hidden = index === 0; }
        if (next) { next.hidden = last; }
        if (submit) { submit.hidden = !last; }
        if (last) { review(form); }

        var heading = all[index].querySelector('h4');
        if (heading && form.dataset.wizardStarted === 'true') { heading.scrollIntoView({ block: 'nearest' }); }
        form.dataset.wizardStarted = 'true';
    }

    function firstInvalid(form) {
        var all = steps(form);
        for (var position = 0; position < all.length; position++) {
            var incomplete = fields(all[position]).some(function (field) { return !field.checkValidity(); });
            if (incomplete) { return position; }
        }
        return -1;
    }

    function wire(form) {
        if (form.dataset.wizardReady === 'true') { return; }
        form.dataset.wizardReady = 'true';
        form.setAttribute('novalidate', 'novalidate');

        form.addEventListener('click', function (event) {
            var next = event.target.closest('.rgw-step-next');
            var previous = event.target.closest('.rgw-step-prev');
            if (!next && !previous) { return; }
            if (next && next.form !== form) { return; }
            if (previous && previous.form !== form) { return; }

            event.preventDefault();
            var index = parseInt(form.dataset.step || '0', 10);
            if (previous) { show(form, index - 1); return; }

            var all = steps(form);
            if (validate(all[index])) { show(form, index + 1); }
        });

        form.addEventListener('submit', function (event) {
            var broken = firstInvalid(form);
            if (broken < 0) { return; }
            event.preventDefault();
            show(form, broken);
            validate(steps(form)[broken]);
        });

        // Re-enable a step once the user starts fixing it.
        form.addEventListener('input', function (event) {
            var owner = event.target.closest('.rgw-invalid');
            if (owner) { owner.classList.remove('rgw-invalid'); }
        });

        show(form, 0);
    }

    document.querySelectorAll('form.rgw-wizard').forEach(wire);

    // Bootstrap 3 modals: start each wizard at step one every time it opens, and
    // clear whatever error the previous attempt left behind.
    if (window.jQuery) {
        window.jQuery(document).on('shown.bs.modal', '.rgw-modal', function () {
            var host = this;
            host.querySelectorAll('form.rgw-wizard').forEach(function (form) {
                delete form.dataset.wizardStarted;
                show(form, 0);
            });
            var message = host.querySelector('.rgw-modal-message');
            if (message) { message.style.display = 'none'; }
            var focusable = host.querySelector('.rgw-step:not([hidden]) input:not([type=hidden]), .rgw-step:not([hidden]) select, .modal-body input:not([type=hidden]), .modal-body select');
            if (focusable) { focusable.focus(); }
        });
    }
})();
