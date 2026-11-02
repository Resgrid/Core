// Route instance detail (maplibre plan section 10.6): the planned line, the driven track, check-in points and
// recorded deviations (resgrid.maps).
$(document).ready(function () {
    if (!document.getElementById('instanceMap')) {
        return;
    }

    function escapeHtml(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    resgrid.maps.create('instanceMap', { center: [39.8283, -98.5795], zoom: 4, scrollZoom: false, traffic: 'off' }).then(function (map) {
        var points = [];

        function addLine(id, geometry, options) {
            if (!geometry) {
                return;
            }

            try {
                var line = map.addLine(id, JSON.parse(geometry), options);
                var bounds = line.bounds();
                if (bounds) {
                    points.push(bounds[0], bounds[1]);
                }
            } catch (e) { }
        }

        // Planned route dashed under the driven track.
        addLine('planned', typeof plannedGeometry !== 'undefined' ? plannedGeometry : null, { color: '#9e9e9e', weight: 4, opacity: 0.7, dashed: true });
        addLine('actual', typeof actualGeometry !== 'undefined' ? actualGeometry : null, { color: '#1c84c6', weight: 4, opacity: 0.85 });

        if (typeof instanceStops !== 'undefined' && instanceStops.length > 0) {
            instanceStops.forEach(function (stop, index) {
                if (stop.lat != null && stop.lng != null && Number.isFinite(Number(stop.lat)) && Number.isFinite(Number(stop.lng))) {
                    var statusText = ['Pending', 'Checked In', 'Completed', 'Skipped'][stop.Status] || 'Unknown';
                    var position = [Number(stop.lat), Number(stop.lng)];
                    map.addMarker(position, { popupHtml: '<strong>Stop ' + (index + 1) + '</strong><br/>' + statusText });
                    points.push(position);
                }
            });
        }

        if (typeof instanceDeviations !== 'undefined' && instanceDeviations.length > 0) {
            instanceDeviations.forEach(function (deviation) {
                var position = [Number(deviation.lat), Number(deviation.lng)];
                if (!Number.isFinite(position[0]) || !Number.isFinite(position[1]) || (position[0] === 0 && position[1] === 0)) {
                    return;
                }

                map.addMarker(position, {
                    html: '<span style="display:block;width:14px;height:14px;border-radius:50%;background:#ed5565;border:2px solid #fff;box-shadow:0 0 2px rgba(0,0,0,.5);"></span>',
                    iconSize: [14, 14],
                    iconAnchor: [7, 7],
                    popupHtml: '<strong>' + escapeHtml(deviation.type) + '</strong>' + (deviation.notes ? '<br/>' + escapeHtml(deviation.notes) : '')
                });
                points.push(position);
            });
        }

        if (points.length > 0) {
            map.fitBounds(points, { padding: 50 });
        }
    });
});
