// Finishes a submission the step-up guard held while the user verified (StepUpFormReplay). A page form is posted back as it was;
// a script call is replayed in the background and the user is returned to the page they were on.
(function () {
    'use strict';

    function showFailure(form) {
        form.style.display = 'none';
        var failed = document.getElementById('stepUpResumeFailed');
        if (failed) { failed.style.display = ''; }
    }

    function replayScriptCall(form) {
        var back = form.getAttribute('data-back') || '/';
        var body = new URLSearchParams(new FormData(form));

        fetch(form.getAttribute('action'), {
            method: 'POST',
            body: body,
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        }).then(function (response) {
            if (!response.ok) {
                showFailure(form);
                return null;
            }

            return response.text().then(function (text) {
                var result = null;
                try { result = JSON.parse(text); } catch (e) { result = null; }

                if (result && result.success === false) {
                    showFailure(form);
                    return;
                }

                window.location.replace(back);
            });
        }, function () {
            showFailure(form);
        });
    }

    function resume() {
        var form = document.getElementById('stepUpResumeForm');
        if (!form || form.getAttribute('data-sent') === 'true') { return; }

        form.setAttribute('data-sent', 'true');
        var button = form.querySelector('button[type="submit"]');
        if (button) { button.disabled = true; }

        if (form.getAttribute('data-script') === 'true') {
            replayScriptCall(form);
        } else {
            form.submit();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', resume);
    } else {
        resume();
    }
})();
