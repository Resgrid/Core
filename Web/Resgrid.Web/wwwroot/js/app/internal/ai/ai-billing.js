(function () {
    'use strict';
    // Enhanced AI add-on checkout (clone of business-operations-billing.js): the server hands back either a Stripe
    // Checkout URL (host pinned to checkout.stripe.com) or a Paddle transaction id; anything else shows the error line.
    // The renewal confirmation text comes from a data attribute so translated quotes never reach inline script.
    var cancel = document.getElementById('ai-cancel-renewal');
    if (cancel) {
        cancel.addEventListener('submit', function (event) {
            if (!window.confirm(cancel.getAttribute('data-confirm'))) event.preventDefault();
        });
    }
    var form = document.getElementById('ai-checkout');
    if (!form) return;
    var config = JSON.parse(document.getElementById('ai-billing-config').textContent);
    form.addEventListener('submit', async function (event) {
        event.preventDefault();
        var button = form.querySelector('button');
        if (button.disabled) return;
        button.disabled = true;
        try {
            var response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) throw new Error();
            var result = await response.json();
            if (config.provider === 'Stripe' && result.url) {
                var url = new URL(result.url);
                if (url.protocol !== 'https:' || url.hostname !== 'checkout.stripe.com' || url.username) throw new Error();
                window.location.assign(url.href);
            } else if (config.provider === 'Paddle' && /^txn_[a-z0-9]{26}$/.test(result.transactionId) && window.Paddle && config.token) {
                if (config.environment === 'sandbox') Paddle.Environment.set('sandbox');
                Paddle.Initialize({ token: config.token });
                Paddle.Checkout.open({ transactionId: result.transactionId });
            } else throw new Error();
        } catch (_) {
            var error = document.getElementById('ai-billing-error'); error.textContent = config.error; error.hidden = false;
        } finally { button.disabled = false; }
    });
}());
