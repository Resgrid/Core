
var resgrid;
(function (resgrid) {
    var protectedworkflows;
    (function (protectedworkflows) {
        // Protected Workflows commands are JSON POSTs carrying the page's antiforgery token. The server re-checks the
        // ADP egress permission, the step-up recency, the two-person rule and the release state on every one; this
        // script only renders the value-free result codes and sends the user through step-up when one is required.
        var messages = {};

        function antiForgeryToken() {
            var input = document.querySelector('input[name="__RequestVerificationToken"]');
            return input ? input.value : '';
        }

        function base() {
            return (resgrid.absoluteBaseUrl || '') + '/User/ProtectedWorkflows/';
        }

        protectedworkflows.setMessages = function (localized) {
            messages = localized || {};
        };

        protectedworkflows.text = function (key, fallback) {
            return Object.prototype.hasOwnProperty.call(messages, key) ? messages[key] : (fallback || key);
        };

        protectedworkflows.post = function (action, payload) {
            return fetch(base() + action, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest',
                    'RequestVerificationToken': antiForgeryToken()
                },
                body: JSON.stringify(payload || {})
            }).then(function (response) {
                if (!response.ok) {
                    throw new Error(response.statusText || String(response.status));
                }
                return response.json();
            });
        };

        // Sends the user through the verification page and back here; they repeat the action afterwards.
        protectedworkflows.stepUp = function () {
            if (!window.confirm(protectedworkflows.text('StepUpRequiredPrompt'))) {
                return;
            }
            var returnUrl = window.location.pathname + window.location.search;
            window.location.href = base() + 'StepUp?returnUrl=' + encodeURIComponent(returnUrl);
        };

        // Human text for a failed command: the error code plus any validation codes.
        protectedworkflows.describe = function (result) {
            if (!result) {
                return protectedworkflows.text('Err_command_failed');
            }
            var lines = [protectedworkflows.text('Err_' + (result.error || 'command_failed'), protectedworkflows.text('Err_command_failed'))];
            (result.validationErrors || []).forEach(function (code) {
                lines.push('• ' + protectedworkflows.text('ValidationError_' + code, code));
            });
            return lines.join('\n');
        };

        // Runs a command; on step_up_required offers the verification round trip; otherwise reports and optionally reloads.
        protectedworkflows.run = function (action, payload, onDone) {
            return protectedworkflows.post(action, payload).then(function (result) {
                if (result && result.success) {
                    if (result.pendingConfirmation) {
                        window.alert(protectedworkflows.text('SecondApproverRelaxRequested'));
                    }
                    if (onDone) { onDone(result); } else { window.location.reload(); }
                    return result;
                }
                if (result && result.error === 'step_up_required') {
                    protectedworkflows.stepUp();
                    return result;
                }
                window.alert(protectedworkflows.describe(result));
                return result;
            }).catch(function () {
                window.alert(protectedworkflows.text('Err_command_failed'));
            });
        };
    })(protectedworkflows = resgrid.protectedworkflows || (resgrid.protectedworkflows = {}));
})(resgrid || (resgrid = {}));
