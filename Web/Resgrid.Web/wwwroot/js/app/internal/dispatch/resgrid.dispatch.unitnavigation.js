// Live ETAs of units navigating to this call in the Unit app (maplibre-mapping-routing-navigation-plan.md §9.4).
// Each dispatched unit row carries a [data-unit-eta] badge; it shows the arrival time while the unit navigates, greys
// out after 60 s without a report, and hides when navigation ends. The hub event carries ids only, so the ETA is read
// through the BFF (Routing/GetActiveNavigations), which applies the viewer's call and unit-location visibility.
$(function () {
    if (typeof callId === 'undefined' || !callId) {
        return;
    }

    var badges = $('[data-unit-eta]');
    if (badges.length === 0) {
        return;
    }

    var STALE_MS = 60 * 1000;
    var navigations = {};
    var reloadTimer = null;

    function formatClock(date) {
        var hours = date.getHours();
        var minutes = date.getMinutes();
        return (hours < 10 ? '0' : '') + hours + ':' + (minutes < 10 ? '0' : '') + minutes;
    }

    function render() {
        var now = Date.now();

        badges.each(function () {
            var badge = $(this);
            var navigation = navigations[String(badge.data('unit-eta'))];

            if (!navigation) {
                badge.hide().text('');
                return;
            }

            var eta = new Date(navigation.EtaUtc);
            var reported = new Date(navigation.ReportedOn).getTime();
            var stale = !isFinite(reported) || now - reported > STALE_MS;
            var remainingMs = eta.getTime() - now;
            var minutes = Math.max(0, Math.round(remainingMs / 60000));

            badge
                .toggleClass('label-info', !stale)
                .toggleClass('label-default', stale)
                .attr('title', minutes + "'" + (navigation.IsTrafficAware ? ' • 🚦' : ''))
                .html('<i class="fa fa-location-arrow"></i> ' + formatClock(eta))
                .show();
        });
    }

    function load() {
        fetch('/api/web-bff/api/v4/Routing/GetActiveNavigations?callId=' + encodeURIComponent(callId), { credentials: 'same-origin' })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (body) {
                navigations = {};
                ((body && body.Data) || []).forEach(function (navigation) {
                    navigations[String(navigation.UnitId)] = navigation;
                });
                render();
            })
            .catch(function () {
                // A failed read leaves the badges as they were; the next event or tick retries.
            });
    }

    // Reports arrive every 15 s per navigating unit; one read covers a burst of them.
    function scheduleLoad() {
        if (reloadTimer) {
            return;
        }

        reloadTimer = setTimeout(function () {
            reloadTimer = null;
            load();
        }, 1500);
    }

    document.addEventListener('resgrid:unit-navigation-updated', function (event) {
        var detail = event.detail || {};
        var payload = null;
        try {
            payload = detail.payload ? JSON.parse(detail.payload) : null;
        } catch (e) {
            payload = null;
        }

        if (!payload || !payload.callId || String(payload.callId) === String(callId) || navigations[String(detail.unitId)]) {
            scheduleLoad();
        }
    });

    load();
    setInterval(render, 15000);
    setInterval(load, 120000);
});
