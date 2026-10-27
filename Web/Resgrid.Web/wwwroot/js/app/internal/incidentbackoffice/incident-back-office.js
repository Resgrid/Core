// Incident Back Office desk behaviour (large-incident-logistics-finance-plan.md §14.7). Bound from page script, never from inline
// handlers: confirmation prompts on destructive forms and a one-shot submit guard so a double click cannot post a row twice.
(function () {
    'use strict';

    function bindConfirm(form) {
        form.addEventListener('submit', function (event) {
            var message = form.getAttribute('data-confirm');
            if (message && !window.confirm(message)) {
                event.preventDefault();
            }
        });
    }

    function bindSubmitGuard(form) {
        form.addEventListener('submit', function (event) {
            if (event.defaultPrevented) {
                return;
            }
            if (form.getAttribute('data-submitting') === 'true') {
                event.preventDefault();
                return;
            }
            form.setAttribute('data-submitting', 'true');
            var buttons = form.querySelectorAll('button[type="submit"]');
            for (var i = 0; i < buttons.length; i++) {
                buttons[i].disabled = true;
            }
        });
    }

    // Admin Assist links a policy as ?aa=<PolicyClass>; each policy form sits in an ibox whose id is that name.
    function focusAdminAssistTarget() {
        var target;
        try {
            target = new URLSearchParams(window.location.search).get('aa');
        } catch (e) {
            return;
        }
        if (!target || !/^[A-Za-z]+$/.test(target)) {
            return;
        }
        var element = document.getElementById(target);
        if (element) {
            element.scrollIntoView({ block: 'start' });
            var first = element.querySelector('input, select, textarea');
            if (first) {
                first.focus({ preventScroll: true });
            }
        }
    }

    function init() {
        focusAdminAssistTarget();
        var confirmForms = document.querySelectorAll('form.ibo-confirm');
        for (var i = 0; i < confirmForms.length; i++) {
            bindConfirm(confirmForms[i]);
        }
        var forms = document.querySelectorAll('form.ibo-ledger-form, form.ibo-transition, form.ibo-confirm, form.ibo-action');
        for (var j = 0; j < forms.length; j++) {
            bindSubmitGuard(forms[j]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
