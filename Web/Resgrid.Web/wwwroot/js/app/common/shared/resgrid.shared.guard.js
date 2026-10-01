// Shared workstation sessions (passkey plan section 12.5.4). Loaded in the page head: a page brought back from history (the
// back or forward button, or the browser's back-forward cache) stays hidden until the server confirms that this session is
// not locked, so the back button never shows a locked operator's work. resgrid.shared.session.js reveals it again or goes
// to the lock screen.
var resgridSharedGuard = (function () {
    'use strict';

    var CONCEALED = 'rg-shared-concealed';
    var root = document.documentElement;

    var style = document.createElement('style');
    style.textContent = 'html.' + CONCEALED + ' body { visibility: hidden !important; }';
    (document.head || root).appendChild(style);

    function concealed() {
        return (' ' + root.className + ' ').indexOf(' ' + CONCEALED + ' ') >= 0;
    }

    function conceal() {
        if (!concealed())
            root.className = (root.className ? root.className + ' ' : '') + CONCEALED;
    }

    function reveal() {
        root.className = (' ' + root.className + ' ').replace(' ' + CONCEALED + ' ', ' ').trim();
    }

    var entries = window.performance && performance.getEntriesByType ? performance.getEntriesByType('navigation') : [];
    var fromHistory = entries && entries.length
        ? entries[0].type === 'back_forward'
        : !!(window.performance && performance.navigation && performance.navigation.type === 2);
    if (fromHistory)
        conceal();

    window.addEventListener('pageshow', function (event) {
        if (event.persisted)
            conceal();
    });

    return { conceal: conceal, reveal: reveal, concealed: concealed };
})();
