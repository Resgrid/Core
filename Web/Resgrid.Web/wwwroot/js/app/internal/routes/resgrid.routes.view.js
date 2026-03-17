$(document).ready(function () {
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
                if (stop.lat && stop.lng) {
                    var marker = L.marker([stop.lat, stop.lng]).addTo(map);
                    marker.bindPopup('<strong>' + (index + 1) + '. ' + stop.Name + '</strong>');
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
