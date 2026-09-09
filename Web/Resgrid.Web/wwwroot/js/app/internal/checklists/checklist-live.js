(function () {
    'use strict';
    var timer = null, pending = false;
    function refresh() {
        timer = null;
        if (!pending || document.hidden) return;
        // A live hint must never discard an excuse being typed or submit another checklist form.
        if (document.querySelector('textarea:focus, input:not([type="hidden"]):focus') ||
            Array.from(document.querySelectorAll('textarea')).some(function (x) { return x.value.length > 0; })) return;
        pending = false;
        var form = document.getElementById('checklist-live-refresh');
        if (form) form.requestSubmit();
        else if (document.getElementById('include-checklists')?.checked) resgrid.calendar.index.getCalendar()?.refetchEvents();
    }
    function changed() { pending = true; clearTimeout(timer); timer = setTimeout(refresh, 500); }
    document.addEventListener('resgrid:checklists-updated', changed);
    document.addEventListener('visibilitychange', changed);
    document.addEventListener('focusout', function () { if (pending) changed(); });
    document.addEventListener('input', function () { if (pending) changed(); });
    window.addEventListener('pagehide', function () { clearTimeout(timer); });
    $(function () { resgrid.common.signalr.init(function () {}, function () {}, function () {}, function () {}); });
})();
