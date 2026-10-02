// The shared workstation bar (passkey plan sections 10.5 and 12.5.4). The server owns the idle lock and the shift end; this
// page only follows them. Real input (keys, pointer, wheel, touch) is reported as operator activity at most every 30 seconds
// across all of this site's tabs, and polling never is. The page warns before the idle lock, goes to the lock screen as soon
// as the server says the session is locked, locks every tab together, and shows a page brought back from history only once
// the server confirms the session is still unlocked.
//
//   resgridSharedSession.init({ messages: { locksIn, idleWarning, shiftEndsSoon }, navigate: function (url) { ... } });
//
// Markup: #rgSharedSession (data-status-url, data-locked-url, data-shift-ended-url), #rgSharedCountdown, #rgSharedWarning,
// #rgSharedWarningText, #rgSharedStay, #rgSharedLockForm, and End shift forms posting switchOperator.
var resgridSharedSession = (function () {
    'use strict';

    var ACTIVITY_HEADER = 'X-Resgrid-Operator-Activity';
    var ACTIVITY_INTERVAL_MS = 30000;
    var POLL_INTERVAL_MS = 60000;
    var RECHECK_MS = 5000;
    var WARNING_SECONDS = 60;
    var SHIFT_NOTICE_SECONDS = 600;
    var ACTIVITY_KEY = 'resgrid.sharedSession.lastActivity';
    var CHANNEL = 'resgrid-shared-session';
    var INPUT_EVENTS = ['keydown', 'pointerdown', 'wheel', 'touchstart'];

    var settings = null;
    var bar = null;
    var channel = null;
    var idleDeadline = null;
    var shiftDeadline = null;
    var leaving = false;
    var checking = false;
    var lastCheck = 0;
    var lastActivity = 0;

    function attribute(name) {
        return bar.getAttribute('data-' + name);
    }

    function here() {
        return window.location.pathname + window.location.search;
    }

    function format(template, value) {
        return String(template || '').replace('{0}', value);
    }

    function clockText(seconds) {
        var minutes = Math.floor(seconds / 60);
        var rest = seconds % 60;
        return minutes + ':' + (rest < 10 ? '0' : '') + rest;
    }

    function broadcast(message) {
        try {
            if (channel)
                channel.postMessage(message);
        } catch (e) {
            // A closed channel only means no other tab hears it; each one still follows the server.
        }
    }

    function go(url) {
        if (leaving)
            return;
        leaving = true;
        settings.navigate(url);
    }

    function goLocked(tell) {
        if (leaving)
            return;
        if (tell !== false)
            broadcast({ type: 'locked' });
        go(attribute('locked-url') + '?returnUrl=' + encodeURIComponent(here()));
    }

    // The session ended or its shift ran out: sign in again.
    function goSignIn() {
        go(shiftDeadline !== null && Date.now() >= shiftDeadline
            ? attribute('shift-ended-url')
            : '/Account/LogOn?returnUrl=' + encodeURIComponent(here()));
    }

    function fetchStatus(activity) {
        var headers = { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' };
        if (activity)
            headers[ACTIVITY_HEADER] = '1';

        lastCheck = Date.now();
        return window.fetch(attribute('status-url'), { method: 'GET', credentials: 'same-origin', cache: 'no-store', headers: headers })
            .then(function (response) {
                if (response.status === 401)
                    return response.json().catch(function () { return {}; }).then(function (body) { return { unauthorized: true, body: body || {} }; });
                return response.ok ? response.json() : null;
            })
            .catch(function () { return null; });
    }

    // What the server said; an unreachable server leaves the last deadlines in place, and the server enforces them anyway.
    function apply(status) {
        if (!status || leaving)
            return false;

        if (status.unauthorized) {
            if (status.body.error === 'shared_session_locked')
                goLocked();
            else
                goSignIn();
            return false;
        }

        if (status.locked) {
            goLocked();
            return false;
        }

        var now = Date.now();
        if (typeof status.idleLocksInSeconds === 'number')
            idleDeadline = now + status.idleLocksInSeconds * 1000;
        if (typeof status.shiftEndsInSeconds === 'number')
            shiftDeadline = now + status.shiftEndsInSeconds * 1000;
        if (window.resgridSharedGuard)
            window.resgridSharedGuard.reveal();
        render();
        return true;
    }

    function check(activity) {
        if (checking)
            return;
        checking = true;
        fetchStatus(activity).then(function (status) {
            checking = false;
            if (apply(status) && activity)
                broadcast({ type: 'activity', idleLocksInSeconds: status.idleLocksInSeconds });
        });
    }

    function readActivity() {
        try {
            return parseInt(window.localStorage.getItem(ACTIVITY_KEY), 10) || 0;
        } catch (e) {
            return lastActivity;
        }
    }

    function writeActivity(now) {
        lastActivity = now;
        try {
            window.localStorage.setItem(ACTIVITY_KEY, String(now));
        } catch (e) {
            // Private mode: this tab still throttles itself.
        }
    }

    // Real input from the operator. Reported at most once per interval across tabs; "Stay signed in" always reports.
    function activity(force) {
        if (leaving)
            return;
        var now = Date.now();
        if (!force && now - Math.max(readActivity(), lastActivity) < ACTIVITY_INTERVAL_MS)
            return;
        writeActivity(now);
        check(true);
    }

    function render() {
        var countdown = document.getElementById('rgSharedCountdown');
        var warning = document.getElementById('rgSharedWarning');
        var warningText = document.getElementById('rgSharedWarningText');
        if (idleDeadline === null)
            return;

        var now = Date.now();
        var remaining = Math.max(0, Math.ceil((idleDeadline - now) / 1000));
        if (countdown)
            countdown.textContent = format(settings.messages.locksIn, clockText(remaining));

        var notices = [];
        if (remaining <= WARNING_SECONDS)
            notices.push(format(settings.messages.idleWarning, remaining));
        if (shiftDeadline !== null && shiftDeadline - now <= SHIFT_NOTICE_SECONDS * 1000)
            notices.push(format(settings.messages.shiftEndsSoon, Math.max(0, Math.ceil((shiftDeadline - now) / 60000))));

        if (warning && warningText) {
            warningText.textContent = notices.join(' ');
            warning.style.display = notices.length ? '' : 'none';
        }
    }

    function tick() {
        if (leaving)
            return;
        var now = Date.now();
        var due = (idleDeadline !== null && now >= idleDeadline) || (shiftDeadline !== null && now >= shiftDeadline);
        if ((due && now - lastCheck >= 1000) || now - lastCheck >= POLL_INTERVAL_MS ||
            (window.resgridSharedGuard && window.resgridSharedGuard.concealed() && now - lastCheck >= RECHECK_MS))
            check(false);
        render();
    }

    function onMessage(event) {
        var message = event && event.data;
        if (!message || leaving)
            return;
        if (message.type === 'locked')
            goLocked(false);
        else if (message.type === 'ended')
            goSignIn();
        else if (message.type === 'activity' && typeof message.idleLocksInSeconds === 'number') {
            idleDeadline = Date.now() + message.idleLocksInSeconds * 1000;
            render();
        }
    }

    function init(options) {
        settings = options || {};
        settings.messages = settings.messages || {};
        settings.navigate = settings.navigate || function (url) { window.location.assign(url); };
        bar = document.getElementById('rgSharedSession');
        if (!bar)
            return;

        if (window.BroadcastChannel) {
            channel = new window.BroadcastChannel(CHANNEL);
            channel.onmessage = onMessage;
        }

        INPUT_EVENTS.forEach(function (name) {
            document.addEventListener(name, function () { activity(false); }, { passive: true, capture: true });
        });

        var stay = document.getElementById('rgSharedStay');
        if (stay)
            stay.addEventListener('click', function () { activity(true); });

        var lockForm = document.getElementById('rgSharedLockForm');
        if (lockForm)
            lockForm.addEventListener('submit', function () { broadcast({ type: 'locked' }); leaving = true; });

        Array.prototype.forEach.call(document.querySelectorAll('form input[name="switchOperator"]'), function (input) {
            input.form.addEventListener('submit', function () { broadcast({ type: 'ended' }); leaving = true; });
        });

        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'visible')
                check(false);
        });

        window.addEventListener('pageshow', function (event) {
            if (event.persisted) {
                leaving = false;
                check(false);
            }
        });

        // A request this page makes after the session locked is refused; go to the lock screen rather than show an error.
        if (window.jQuery) {
            window.jQuery(document).ajaxError(function (event, xhr) {
                if (xhr && xhr.status === 401 && xhr.responseJSON && xhr.responseJSON.error === 'shared_session_locked')
                    goLocked();
            });
        }

        // This page came from the server for an unlocked session: tabs still on the lock screen can go back.
        fetchStatus(false).then(function (status) {
            if (apply(status))
                broadcast({ type: 'active' });
        });
        window.setInterval(tick, 1000);
    }

    return { init: init };
})();
