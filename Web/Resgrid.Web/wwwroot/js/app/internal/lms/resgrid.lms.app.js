/*
 * Resgrid in-app lesson launch page (round 10 A8): /lms-app/... in the Omni app's web view (react-native-webview) or in an iframe of the app's
 * web and desktop builds. It asks the server for the lesson's state with the page's own session token (POST /lms-app/state, same origin, no
 * cookies) and, when the runtime records a result, posts the completion message to the app:
 *   window.ReactNativeWebView.postMessage(JSON string)        (native web view)
 *   window.parent.postMessage(object, origin)                  (framed: to each configured app origin only, never "*")
 * Message: { type: "resgrid.lms.lesson", version: 1, enrollmentId, lessonId, kind, status, satisfied, score?, error? }; the latest one is
 * window.RG_LMS_APP.state. The package itself runs in a sandboxed frame (opaque origin) and cannot read this page.
 */
(function (window, document) {
    'use strict';

    var root = document.getElementById('rg-lms-app');
    if (!root) {
        return;
    }

    var MESSAGE_TYPE = 'resgrid.lms.lesson';
    var RESULTS = ['completed', 'passed', 'failed', 'terminated', 'unavailable'];
    var FAST_MS = 4000;
    var SLOW_MS = 15000;
    var HIDDEN_MS = 30000;
    var FAST_FOR_MS = 120000;

    function parse(text) {
        if (!text) {
            return null;
        }
        try {
            return JSON.parse(text);
        } catch (e) {
            return null;
        }
    }

    function toInt(text) {
        var n = parseInt(text, 10);
        return isNaN(n) ? 0 : n;
    }

    var mode = root.getAttribute('data-mode') || 'error';
    var kind = root.getAttribute('data-kind') || null;
    var enrollmentId = toInt(root.getAttribute('data-enrollment'));
    var lessonId = toInt(root.getAttribute('data-lesson'));
    var session = root.getAttribute('data-session') || '';
    var stateUrl = root.getAttribute('data-state-url') || '';
    var origins = parse(root.getAttribute('data-origins')) || [];
    var postInitial = root.getAttribute('data-post-initial') === 'true';
    var autostart = root.getAttribute('data-autostart') === 'true';
    var statusLine = document.getElementById('rg-lms-app-status');
    var offlineNote = document.getElementById('rg-lms-app-offline');
    var started = new Date().getTime();
    var lastPosted = null;
    var timer = null;
    var stopped = !session || !stateUrl;

    var api = window.RG_LMS_APP = { state: null };

    // The launch link's token is used up; keep it out of the web view's address bar and history.
    try {
        if (window.history && window.history.replaceState && window.location.search && window.location.pathname === '/lms-app/launch') {
            window.history.replaceState(null, '', window.location.pathname);
        }
    } catch (e) { }

    function isResult(state) {
        return !!state && RESULTS.indexOf(state.status) >= 0;
    }

    function signature(state) {
        return [state.status, state.satisfied ? 1 : 0, state.score === undefined || state.score === null ? '' : state.score, state.error || ''].join('|');
    }

    function normalize(state) {
        if (!state || state.type !== MESSAGE_TYPE) {
            return null;
        }
        // The page knows which lesson it shows; a refusal from the server may not.
        if (!state.enrollmentId) {
            state.enrollmentId = enrollmentId;
        }
        if (!state.lessonId) {
            state.lessonId = lessonId;
        }
        if (!state.kind && kind) {
            state.kind = kind;
        }
        return state;
    }

    function post(state) {
        if (!state.enrollmentId || !state.lessonId) {
            return;
        }
        try {
            if (window.ReactNativeWebView && typeof window.ReactNativeWebView.postMessage === 'function') {
                window.ReactNativeWebView.postMessage(JSON.stringify(state));
            }
        } catch (e) { }
        if (window.parent && window.parent !== window) {
            for (var i = 0; i < origins.length; i++) {
                try {
                    // Delivered only when the parent's origin is this one: the message never goes to an origin that is not configured.
                    window.parent.postMessage(state, origins[i]);
                } catch (e2) { }
            }
        }
    }

    function describe(state) {
        if (!statusLine || !state || mode === 'error') {
            return;
        }
        var line = state.satisfied ? root.getAttribute('data-text-satisfied') : root.getAttribute('data-text-not-satisfied');
        if (typeof state.score === 'number') {
            line += ' · ' + root.getAttribute('data-text-score') + ' ' + (Math.round(state.score * 100) / 100) + '%';
        }
        if (state.status === 'unavailable') {
            line = root.getAttribute('data-text-ended') || line;
        }
        statusLine.textContent = line || '';
    }

    function consider(state, initial) {
        state = normalize(state);
        if (!state) {
            return;
        }
        api.state = state;
        root.setAttribute('data-status', state.status || '');
        describe(state);
        var sig = signature(state);
        if (initial) {
            lastPosted = sig;
            if (postInitial) {
                post(state);
            }
            return;
        }
        if (sig !== lastPosted) {
            lastPosted = sig;
            if (isResult(state)) {
                post(state);
            }
        }
    }

    function showOffline(offline) {
        if (offlineNote) {
            offlineNote.hidden = !offline;
        }
    }

    function stop() {
        stopped = true;
        if (timer) {
            window.clearTimeout(timer);
            timer = null;
        }
    }

    function schedule() {
        if (stopped) {
            return;
        }
        var elapsed = new Date().getTime() - started;
        var wait = document.hidden ? HIDDEN_MS : (elapsed < FAST_FOR_MS ? FAST_MS : SLOW_MS);
        timer = window.setTimeout(poll, wait);
    }

    function poll() {
        timer = null;
        if (stopped) {
            return;
        }
        if (window.navigator && window.navigator.onLine === false) {
            showOffline(true);
            schedule();
            return;
        }
        var xhr = new window.XMLHttpRequest();
        xhr.open('POST', stateUrl, true);
        xhr.setRequestHeader('Content-Type', 'application/x-www-form-urlencoded');
        xhr.onload = function () {
            showOffline(false);
            var state = parse(xhr.responseText);
            if (xhr.status === 200 && state) {
                consider(state, false);
                if (state.status === 'unavailable') {
                    stop();
                    return;
                }
            } else if (xhr.status === 401 || xhr.status === 403 || xhr.status === 404 || xhr.status === 410) {
                consider(state || { type: MESSAGE_TYPE, version: 1, status: 'unavailable', satisfied: !!(api.state && api.state.satisfied), error: 'CodeExpired' }, false);
                stop();
                return;
            }
            schedule();
        };
        xhr.onerror = function () {
            schedule();
        };
        xhr.send('session=' + encodeURIComponent(session));
    }

    function pollSoon() {
        if (stopped) {
            return;
        }
        if (timer) {
            window.clearTimeout(timer);
            timer = null;
        }
        poll();
    }

    consider(parse(root.getAttribute('data-state')), true);

    window.addEventListener('online', function () { showOffline(false); pollSoon(); });
    window.addEventListener('offline', function () { showOffline(true); });
    document.addEventListener('visibilitychange', function () {
        if (!document.hidden) {
            pollSoon();
        }
    });
    if (window.navigator && window.navigator.onLine === false) {
        showOffline(true);
    }

    if (mode === 'lti') {
        var form = document.getElementById('rg-lms-app-lti');
        var framed = false;
        try {
            framed = window.top !== window.self;
        } catch (e3) {
            framed = true;
        }
        if (form && framed && !window.ReactNativeWebView) {
            // In the app's iframe most tools refuse to be framed and their cookies would be third-party: the tool opens in its own window
            // (the button is the user gesture a popup needs) while this page keeps reporting the lesson's state.
            form.setAttribute('target', '_blank');
            var framedNote = document.getElementById('rg-lms-app-lti-framed');
            if (framedNote) {
                framedNote.hidden = false;
            }
        } else if (form && autostart) {
            // A native web view or a top-level window: go straight to the tool (the tool's return URL comes back to this site).
            var opening = document.getElementById('rg-lms-app-lti-opening');
            if (opening) {
                opening.hidden = false;
            }
            stop();
            form.submit();
            return;
        }
    }

    if (mode === 'done') {
        var close = document.getElementById('rg-lms-app-close');
        if (close) {
            close.addEventListener('click', function () {
                try {
                    window.close();
                } catch (e4) { }
            });
        }
    }

    if (mode === 'content' || mode === 'lti') {
        schedule();
    }
})(window, document);
