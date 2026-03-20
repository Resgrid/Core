$(document).ready(function () {
    function escapeHtml(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    if (document.getElementById('routeMap')) {
        var tiles = L.tileLayer(
            osmTileUrl,
            {
                maxZoom: 19,
                attribution: osmTileAttribution
            }
        );

        var map = L.map('routeMap', {
            scrollWheelZoom: false
        }).setView([39.8283, -98.5795], 4).addLayer(tiles);

        // Add route geometry if available
        if (typeof routeGeometry !== 'undefined' && routeGeometry) {
            try {
                var geojson = JSON.parse(routeGeometry);
                L.geoJSON(geojson, {
                    style: {
                        color: routeColor || '#3388ff',
                        weight: 4,
                        opacity: 0.8
                    }
                }).addTo(map);
            } catch (e) { }
        }

        // Add stop markers
        if (typeof routeStops !== 'undefined' && routeStops.length > 0) {
            var group = [];

            routeStops.forEach(function (stop, index) {
                if (stop.lat != null && stop.lng != null && Number.isFinite(Number(stop.lat)) && Number.isFinite(Number(stop.lng))) {
                    var marker = L.marker([stop.lat, stop.lng]).addTo(map);
                    marker.bindTooltip((index + 1) + '. ' + escapeHtml(stop.Name), { permanent: true, direction: 'right' });
                    marker.bindPopup('<strong>' + (index + 1) + '. ' + escapeHtml(stop.Name) + '</strong>');
                    group.push(marker);
                }
            });

            if (group.length > 0) {
                var bounds = L.featureGroup(group).getBounds();
                map.fitBounds(bounds, { padding: [50, 50] });
            }
        }
    }
});
