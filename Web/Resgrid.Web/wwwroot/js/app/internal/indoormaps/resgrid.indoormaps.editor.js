$(document).ready(function () {
    if (document.getElementById('boundsMap')) {
        var lat = parseFloat($('#IndoorMap_CenterLatitude').val()) || 39.7392;
        var lon = parseFloat($('#IndoorMap_CenterLongitude').val()) || -104.9903;

        var map = L.map('boundsMap').setView([lat, lon], 16);
        L.tileLayer(osmTileUrl, {
            maxZoom: 22,
            attribution: osmTileAttribution
        }).addTo(map);

        var rect = null;

        function updateRect() {
            var neLat = parseFloat($('#IndoorMap_BoundsNELat').val());
            var neLon = parseFloat($('#IndoorMap_BoundsNELon').val());
            var swLat = parseFloat($('#IndoorMap_BoundsSWLat').val());
            var swLon = parseFloat($('#IndoorMap_BoundsSWLon').val());

            if (!isNaN(neLat) && !isNaN(neLon) && !isNaN(swLat) && !isNaN(swLon)) {
                var bounds = [[swLat, swLon], [neLat, neLon]];
                if (rect) {
                    rect.setBounds(bounds);
                } else {
                    rect = L.rectangle(bounds, { color: '#3388ff', weight: 2, fillOpacity: 0.1 }).addTo(map);
                }
                map.fitBounds(bounds);
            }
        }

        $('#IndoorMap_BoundsNELat, #IndoorMap_BoundsNELon, #IndoorMap_BoundsSWLat, #IndoorMap_BoundsSWLon').on('change', updateRect);

        map.on('click', function (e) {
            $('#IndoorMap_CenterLatitude').val(e.latlng.lat.toFixed(7));
            $('#IndoorMap_CenterLongitude').val(e.latlng.lng.toFixed(7));
        });

        updateRect();
    }
});
