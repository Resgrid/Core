(function () {
    'use strict';
    var form = document.getElementById('readiness-checkout');
    if (!form) return;
    var config = JSON.parse(document.getElementById('readiness-billing-config').textContent);
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
            var error = document.getElementById('readiness-billing-error'); error.textContent = config.error; error.hidden = false;
        } finally { button.disabled = false; }
    });
}());
