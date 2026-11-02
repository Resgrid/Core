$(document).ready(function () {
    if (document.getElementById('boundsMap')) {
        var lat = parseFloat($('#IndoorMap_CenterLatitude').val()) || 39.7392;
        var lon = parseFloat($('#IndoorMap_CenterLongitude').val()) || -104.9903;

        // resgrid.maps: MapLibre or Leaflet by the department's Maps.MapLibreWeb flag (maplibre plan section 7.2).
        resgrid.maps.create('boundsMap', { center: [lat, lon], zoom: 16, maxZoom: 22 }).then(function (map) {
            var rect = null;

            function updateRect() {
                var neLat = parseFloat($('#IndoorMap_BoundsNELat').val());
                var neLon = parseFloat($('#IndoorMap_BoundsNELon').val());
                var swLat = parseFloat($('#IndoorMap_BoundsSWLat').val());
                var swLon = parseFloat($('#IndoorMap_BoundsSWLon').val());

                if (!isNaN(neLat) && !isNaN(neLon) && !isNaN(swLat) && !isNaN(swLon)) {
                    var outline = {
                        type: 'Polygon',
                        coordinates: [[[swLon, swLat], [neLon, swLat], [neLon, neLat], [swLon, neLat], [swLon, swLat]]]
                    };
                    if (rect) {
                        rect.setData(outline);
                    } else {
                        rect = map.addGeoJson('indoor-bounds', outline, { color: '#3388ff', weight: 2, fillOpacity: 0.1 });
                    }
                    map.fitBounds([[swLat, swLon], [neLat, neLon]], { padding: 20, maxZoom: 22 });
                }
            }

            $('#IndoorMap_BoundsNELat, #IndoorMap_BoundsNELon, #IndoorMap_BoundsSWLat, #IndoorMap_BoundsSWLon').on('change', updateRect);

            map.onClick(function (position) {
                $('#IndoorMap_CenterLatitude').val(position[0].toFixed(7));
                $('#IndoorMap_CenterLongitude').val(position[1].toFixed(7));
            });

            updateRect();
        });
    }
});
