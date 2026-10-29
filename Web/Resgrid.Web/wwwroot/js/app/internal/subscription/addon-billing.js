(function () {
    'use strict';
    // Shared checkout for the add-on billing pages (_AddonBilling.cshtml). The server hands back either a Stripe Checkout URL (host
    // pinned to checkout.stripe.com) or a Paddle transaction id; anything else shows the error line. Stripe returns to the page with
    // ?result=complete from the Billing API's success URL; Paddle is sent there by the overlay's successUrl. While the provider
    // webhook has not activated the add-on yet, the page refreshes itself for about a minute.
    var config = JSON.parse(document.getElementById('addon-billing-config').textContent);
    var error = document.getElementById('addon-billing-error');
    var maxAttempts = 12;

    function showError() {
        error.textContent = config.error;
        error.hidden = false;
    }

    var cancel = document.getElementById('addon-cancel-renewal');
    if (cancel) {
        cancel.addEventListener('submit', function (event) {
            if (!window.confirm(cancel.getAttribute('data-confirm'))) event.preventDefault();
        });
    }

    // The server builds completeUrl as this page's absolute URL; the refresh follows nothing else. The host is compared rather than
    // the origin because the server can see plain http behind a TLS-terminating proxy.
    var next = null;
    try { next = new URL(config.completeUrl, window.location.href); } catch (_) { next = null; }
    if (config.pending && config.attempt < maxAttempts && next && (next.protocol === 'https:' || next.protocol === 'http:') && next.host === window.location.host) {
        next.searchParams.set('attempt', String(config.attempt + 1));
        window.setTimeout(function () {
            window.location.replace(next.href);
        }, 5000);
    }

    var form = document.getElementById('addon-checkout');
    if (!form) return;
    var paddleInitialized = false;

    function openPaddle(transactionId) {
        if (!window.Paddle || !config.token) throw new Error();
        // Paddle.Initialize may only run once per page; a second purchase attempt after closing the overlay reuses it.
        if (!paddleInitialized) {
            if (config.environment === 'sandbox') Paddle.Environment.set('sandbox');
            Paddle.Initialize({ token: config.token });
            paddleInitialized = true;
        }
        Paddle.Checkout.open({ transactionId: transactionId, settings: { successUrl: config.completeUrl } });
    }

    form.addEventListener('submit', async function (event) {
        event.preventDefault();
        var button = form.querySelector('button');
        if (button.disabled) return;
        button.disabled = true;
        error.hidden = true;
        var redirecting = false;
        try {
            // X-Requested-With lets a step-up check (ADP purchases) answer with where to verify instead of a redirect page.
            var response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', cache: 'no-store',
                headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (response.status === 403) {
                var stepUp = await response.json().catch(function () { return null; });
                if (stepUp && stepUp.error === 'step_up_required' && typeof stepUp.redirect === 'string' && stepUp.redirect.charAt(0) === '/' && stepUp.redirect.charAt(1) !== '/') {
                    redirecting = true;
                    window.location.assign(stepUp.redirect);
                    return;
                }
            }
            if (!response.ok) throw new Error();
            var result = await response.json();
            if (config.provider === 'Stripe' && result.url) {
                var url = new URL(result.url);
                if (url.protocol !== 'https:' || url.hostname !== 'checkout.stripe.com' || url.username) throw new Error();
                redirecting = true;
                window.location.assign(url.href);
            } else if (config.provider === 'Paddle' && /^txn_[a-z0-9]{26}$/.test(result.transactionId)) {
                openPaddle(result.transactionId);
            } else throw new Error();
        } catch (_) {
            showError();
        } finally {
            // Leave the button disabled while the browser leaves for Stripe so a second click cannot start another session.
            if (!redirecting) button.disabled = false;
        }
    });
}());
