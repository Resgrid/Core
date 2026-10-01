// The lock screen of a shared workstation (passkey plan section 12.5.3). Every other tab of this site locks with it, and once
// the same operator unlocks in any tab, the others go back to where they were. End shift and Switch operator sign every tab
// out. Nothing from the locked work is kept here.
var resgridSharedLocked = (function () {
    'use strict';

    var CHANNEL = 'resgrid-shared-session';
    var root = document.getElementById('sharedLocked');
    var channel = null;
    var leaving = false;

    function attribute(name) {
        return root ? root.getAttribute('data-' + name) : null;
    }

    function broadcast(message) {
        try {
            if (channel)
                channel.postMessage(message);
        } catch (e) {
            // Other tabs follow the server on their own.
        }
    }

    function leave(url) {
        if (leaving)
            return;
        leaving = true;
        window.location.assign(url);
    }

    function back() {
        leave(attribute('return-url') || '/User/Home/Dashboard');
    }

    // Unlocked elsewhere: go back. Ended: sign in. Still locked: stay.
    function check() {
        if (leaving || !root)
            return;
        window.fetch(attribute('status-url'), {
            method: 'GET', credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
        }).then(function (response) {
            if (response.status === 401) {
                leave('/Account/LogOn');
                return null;
            }
            return response.ok ? response.json() : null;
        }).then(function (status) {
            if (status && status.shared && !status.locked)
                back();
        }).catch(function () { });
    }

    if (window.BroadcastChannel) {
        channel = new window.BroadcastChannel(CHANNEL);
        channel.onmessage = function (event) {
            var message = event && event.data;
            if (!message)
                return;
            if (message.type === 'active')
                check();
            else if (message.type === 'ended')
                leave('/Account/LogOn');
        };
    }

    // Every tab of this site locks with this one.
    broadcast({ type: 'locked' });

    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible')
            check();
    });

    Array.prototype.forEach.call(document.querySelectorAll('form input[name="switchOperator"]'), function (input) {
        input.form.addEventListener('submit', function () { broadcast({ type: 'ended' }); });
    });

    return { leave: leave, check: check };
})();
