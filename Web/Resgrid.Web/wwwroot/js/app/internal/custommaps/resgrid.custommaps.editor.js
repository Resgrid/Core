$(document).ready(function () {
    if (!document.getElementById('boundsMap')) return;

    var lat = parseFloat(initialLat) || 39.7392;
    var lon = parseFloat(initialLon) || -104.9903;

    var map = L.map('boundsMap').setView([lat, lon], 16);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 22,
        attribution: '&copy; OpenStreetMap'
    }).addTo(map);

    // Initialize Leaflet.draw
    var drawnItems = new L.FeatureGroup();
    map.addLayer(drawnItems);

    var drawControl = new L.Control.Draw({
        draw: {
            polyline: false,
            polygon: false,
            circle: false,
            circlemarker: false,
            marker: false,
            rectangle: {
                shapeOptions: {
                    color: '#3388ff',
                    weight: 2,
                    fillOpacity: 0.1
                }
            }
        },
        edit: {
            featureGroup: drawnItems,
            remove: true
        }
    });
    map.addControl(drawControl);

    // Load existing bounds if present
    var neLat = parseFloat($('#Map_BoundsNELat').val());
    var neLon = parseFloat($('#Map_BoundsNELon').val());
    var swLat = parseFloat($('#Map_BoundsSWLat').val());
    var swLon = parseFloat($('#Map_BoundsSWLon').val());

    if (!isNaN(neLat) && !isNaN(neLon) && !isNaN(swLat) && !isNaN(swLon) && neLat !== 0) {
        var bounds = [[swLat, swLon], [neLat, neLon]];
        var rect = L.rectangle(bounds, { color: '#3388ff', weight: 2, fillOpacity: 0.1 });
        drawnItems.addLayer(rect);
        map.fitBounds(bounds);
    }

    function updateBoundsFromLayer(layer) {
        var bounds = layer.getBounds();
        var ne = bounds.getNorthEast();
        var sw = bounds.getSouthWest();

        $('#Map_BoundsNELat').val(ne.lat.toFixed(7));
        $('#Map_BoundsNELon').val(ne.lng.toFixed(7));
        $('#Map_BoundsSWLat').val(sw.lat.toFixed(7));
        $('#Map_BoundsSWLon').val(sw.lng.toFixed(7));

        // Center = midpoint
        var centerLat = (ne.lat + sw.lat) / 2;
        var centerLon = (ne.lng + sw.lng) / 2;
        $('#Map_CenterLatitude').val(centerLat.toFixed(7));
        $('#Map_CenterLongitude').val(centerLon.toFixed(7));

        // GeoJSON polygon
        var geoJson = JSON.stringify(layer.toGeoJSON().geometry);
        $('#Map_BoundsGeoJson').val(geoJson);
    }

    map.on(L.Draw.Event.CREATED, function (e) {
        // Remove existing rectangles
        drawnItems.clearLayers();
        drawnItems.addLayer(e.layer);
        updateBoundsFromLayer(e.layer);
    });

    map.on(L.Draw.Event.EDITED, function (e) {
        e.layers.eachLayer(function (layer) {
            updateBoundsFromLayer(layer);
        });
    });

    map.on(L.Draw.Event.DELETED, function () {
        $('#Map_BoundsNELat').val('');
        $('#Map_BoundsNELon').val('');
        $('#Map_BoundsSWLat').val('');
        $('#Map_BoundsSWLon').val('');
        $('#Map_CenterLatitude').val('');
        $('#Map_CenterLongitude').val('');
        $('#Map_BoundsGeoJson').val('');
    });

    // Click to set center
    map.on('click', function (e) {
        $('#Map_CenterLatitude').val(e.latlng.lat.toFixed(7));
        $('#Map_CenterLongitude').val(e.latlng.lng.toFixed(7));
    });
});
