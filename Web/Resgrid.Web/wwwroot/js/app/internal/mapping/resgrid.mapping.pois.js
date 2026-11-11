var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var pois;
        (function (pois) {
            // While a POI import runs in the background (worker 79), re-fetch the imports panel so its status and report
            // update without a page refresh. When the last running import finishes, reload the page once so the POI
            // counts in the types table include the new points.
            var pollInterval = 10000;
            // An expired sign-in answers with the login page, never the panel; stop after a few of those in a row.
            var maxMisses = 6;
            var misses = 0;
            var timer = null;

            function panel() {
                return $('#poiImportsPanel');
            }

            function isActive(element) {
                return element.length > 0 && element.attr('data-active') === 'true';
            }

            function schedule() {
                if (timer) {
                    clearTimeout(timer);
                }

                timer = setTimeout(poll, pollInterval);
            }

            function missed() {
                misses++;
                if (misses < maxMisses) {
                    schedule();
                }
            }

            function poll() {
                timer = null;

                // A background tab has no one to show the progress to; check again when it is visible.
                if (document.hidden) {
                    schedule();
                    return;
                }

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Mapping/PoiImportsPanel',
                    type: 'GET',
                    cache: false,
                    dataType: 'html'
                }).done(function (html) {
                    var updated = $($.parseHTML($.trim(html))).filter('#poiImportsPanel');
                    if (updated.length === 0) {
                        missed();
                        return;
                    }

                    misses = 0;
                    if (!isActive(updated)) {
                        window.location.reload();
                        return;
                    }

                    panel().replaceWith(updated);
                    schedule();
                }).fail(function () {
                    // A dropped request (network blip, deploy) is not a reason to stop following the import at once.
                    missed();
                });
            }

            $(document).ready(function () {
                if (isActive(panel())) {
                    schedule();
                }

                document.addEventListener('visibilitychange', function () {
                    if (!document.hidden && isActive(panel()) && !timer && misses < maxMisses) {
                        poll();
                    }
                });
            });
        })(pois = mapping.pois || (mapping.pois = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
