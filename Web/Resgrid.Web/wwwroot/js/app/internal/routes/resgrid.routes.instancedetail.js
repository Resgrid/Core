$(document).ready(function () {
    if (document.getElementById('instanceMap')) {
        var tiles = L.tileLayer(
            osmTileUrl,
            {
                maxZoom: 19,
                attribution: osmTileAttribution
            }
        );

        var map = L.map('instanceMap', {
            scrollWheelZoom: false
        }).setView([39.8283, -98.5795], 4).addLayer(tiles);

        // Add instance stop markers if available
        if (typeof instanceStops !== 'undefined' && instanceStops.length > 0) {
            var group = [];

            instanceStops.forEach(function (stop, index) {
                if (stop.lat != null && stop.lng != null && Number.isFinite(Number(stop.lat)) && Number.isFinite(Number(stop.lng))) {
                    var statusText = ['Pending', 'Checked In', 'Completed', 'Skipped'][stop.Status] || 'Unknown';
                    var marker = L.marker([stop.lat, stop.lng]).addTo(map);
                    marker.bindPopup('<strong>Stop ' + (stop.StopOrder + 1) + '</strong><br/>' + statusText);
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
