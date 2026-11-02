// Route plan View / Archived View: the generated route line and numbered stops (resgrid.maps; MapLibre or Leaflet by the
// department's Maps.MapLibreWeb flag).
$(document).ready(function () {
    function escapeHtml(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    if (!document.getElementById('routeMap')) {
        return;
    }

    resgrid.maps.create('routeMap', { center: [39.8283, -98.5795], zoom: 4, scrollZoom: false, traffic: 'off' }).then(function (map) {
        var points = [];

        if (typeof routeGeometry !== 'undefined' && routeGeometry) {
            try {
                var line = map.addLine('route', JSON.parse(routeGeometry), { color: routeColor || '#3388ff', weight: 4, opacity: 0.8 });
                var lineBounds = line.bounds();
                if (lineBounds) {
                    points.push(lineBounds[0], lineBounds[1]);
                }
            } catch (e) { }
        }

        if (typeof routeStops !== 'undefined' && routeStops.length > 0) {
            routeStops.forEach(function (stop, index) {
                if (stop.lat != null && stop.lng != null && Number.isFinite(Number(stop.lat)) && Number.isFinite(Number(stop.lng))) {
                    var position = [Number(stop.lat), Number(stop.lng)];
                    map.addMarker(position, {
                        label: (index + 1) + '. ' + stop.Name,
                        popupHtml: '<strong>' + (index + 1) + '. ' + escapeHtml(stop.Name) + '</strong>'
                    });
                    points.push(position);
                }
            });
        }

        if (points.length > 0) {
            map.fitBounds(points, { padding: 50 });
        }
    });
});
