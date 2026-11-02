// Posts the sign-in, registration, password recovery and invitation forms once. A form marked data-submit-once ignores a second
// click, double-click or Enter press while its first post is on the way, and its submit buttons show that it is working. A form
// the page's own validation stops (jQuery Validate cancels the submit event) is never locked, so the user can correct it and try
// again. Plain script with no library: it runs under the recovery pages' script-src 'self' policy and on pages whose jQuery failed.
(function (window, document) {
    'use strict';

    var SUBMITTING = 'data-submitting';
    var DISABLED_HERE = 'data-submit-once-disabled';
    var SPINNER = 'data-submit-once-spinner';

    // A post that never answers (the network dropped, the user pressed Stop) gives the buttons back eventually.
    var RELEASE_AFTER_MS = 30000;

    function isSubmitButton(control) {
        var type = (control.type || '').toLowerCase();
        return (control.tagName === 'BUTTON' || control.tagName === 'INPUT') && (type === 'submit' || type === 'image');
    }

    function submitButtons(form) {
        var buttons = [];
        var controls = form.elements || [];
        for (var i = 0; i < controls.length; i++) {
            if (isSubmitButton(controls[i]))
                buttons.push(controls[i]);
        }
        return buttons;
    }

    function submitterOf(event, form) {
        if (event.submitter && isSubmitButton(event.submitter))
            return event.submitter;

        var active = document.activeElement;
        if (active && active.form === form && isSubmitButton(active))
            return active;

        var buttons = submitButtons(form);
        return buttons.length > 0 ? buttons[0] : null;
    }

    function release(form) {
        if (!form || form.getAttribute(SUBMITTING) !== 'true')
            return;

        window.clearTimeout(form.submitOnceTimer);
        form.removeAttribute(SUBMITTING);
        form.removeAttribute('aria-busy');

        var buttons = submitButtons(form);
        for (var i = 0; i < buttons.length; i++) {
            if (buttons[i].getAttribute(DISABLED_HERE) === 'true') {
                buttons[i].removeAttribute(DISABLED_HERE);
                buttons[i].disabled = false;
            }
        }

        var spinners = form.querySelectorAll('[' + SPINNER + ']');
        for (var j = 0; j < spinners.length; j++)
            spinners[j].parentNode.removeChild(spinners[j]);
    }

    function lock(form, submitter, event) {
        form.setAttribute(SUBMITTING, 'true');
        form.setAttribute('aria-busy', 'true');

        // Disabled after this turn of the event loop: a button disabled while the browser builds the post would drop the clicked
        // button's own name and value (and its formaction) from it. The SUBMITTING mark already refuses a second submit meanwhile.
        window.setTimeout(function () {
            if (form.getAttribute(SUBMITTING) !== 'true')
                return;

            // A handler that ran after this one (a page check bound later on the document) stopped the post after all.
            if (event.defaultPrevented) {
                release(form);
                return;
            }

            var buttons = submitButtons(form);
            for (var i = 0; i < buttons.length; i++) {
                if (!buttons[i].disabled) {
                    buttons[i].setAttribute(DISABLED_HERE, 'true');
                    buttons[i].disabled = true;
                }
            }

            if (submitter && submitter.tagName === 'BUTTON' && !submitter.querySelector('[' + SPINNER + ']')) {
                var spinner = document.createElement('i');
                spinner.className = 'fa fa-spinner fa-spin';
                spinner.setAttribute('aria-hidden', 'true');
                spinner.setAttribute(SPINNER, 'true');
                submitter.insertBefore(document.createTextNode(' '), submitter.firstChild);
                submitter.insertBefore(spinner, submitter.firstChild);
            }
        }, 0);

        form.submitOnceTimer = window.setTimeout(function () { release(form); }, RELEASE_AFTER_MS);
    }

    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!form || typeof form.hasAttribute !== 'function' || !form.hasAttribute('data-submit-once'))
            return;

        if (form.getAttribute(SUBMITTING) === 'true') {
            event.preventDefault();
            return;
        }

        // Something before this (the page's validation) already stopped the post: nothing is on the way, so nothing is locked.
        if (event.defaultPrevented)
            return;

        lock(form, submitterOf(event, form), event);
    }, false);

    // Back/forward restores the page as it was left, locked mid-post; it is a fresh page to the user, so it posts again.
    window.addEventListener('pageshow', function (event) {
        if (!event.persisted)
            return;

        var forms = document.querySelectorAll('form[' + SUBMITTING + ']');
        for (var i = 0; i < forms.length; i++)
            release(forms[i]);
    });

    window.resgridSubmitOnce = { release: release };
})(window, document);
