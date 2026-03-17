$(document).ready(function () {
    if (!document.getElementById('boundsMap')) return;

    var lat = parseFloat(initialLat) || 39.7392;
    var lon = parseFloat(initialLon) || -104.9903;

    var map = L.map('boundsMap').setView([lat, lon], 16);
    L.tileLayer(osmTileUrl, {
        maxZoom: 22,
        attribution: osmTileAttribution
    }).addTo(map);

    // Enable only rectangle drawing via Geoman
    map.pm.addControls({
        position: 'topleft',
        drawMarker: false,
        drawPolyline: false,
        drawPolygon: false,
        drawCircle: false,
        drawCircleMarker: false,
        drawText: false,
        drawRectangle: true,
        editMode: true,
        dragMode: false,
        cutPolygon: false,
        rotateMode: false,
        removalMode: true
    });

    var currentRectLayer = null;

    // Load existing bounds if present
    var neLat = parseFloat($('#Map_BoundsNELat').val());
    var neLon = parseFloat($('#Map_BoundsNELon').val());
    var swLat = parseFloat($('#Map_BoundsSWLat').val());
    var swLon = parseFloat($('#Map_BoundsSWLon').val());

    if (!isNaN(neLat) && !isNaN(neLon) && !isNaN(swLat) && !isNaN(swLon) && neLat !== 0) {
        var bounds = [[swLat, swLon], [neLat, neLon]];
        currentRectLayer = L.rectangle(bounds, { color: '#3388ff', weight: 2, fillOpacity: 0.1 }).addTo(map);
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

        var centerLat = (ne.lat + sw.lat) / 2;
        var centerLon = (ne.lng + sw.lng) / 2;
        $('#Map_CenterLatitude').val(centerLat.toFixed(7));
        $('#Map_CenterLongitude').val(centerLon.toFixed(7));

        $('#Map_BoundsGeoJson').val(JSON.stringify(layer.toGeoJSON().geometry));
    }

    function clearBounds() {
        $('#Map_BoundsNELat').val('');
        $('#Map_BoundsNELon').val('');
        $('#Map_BoundsSWLat').val('');
        $('#Map_BoundsSWLon').val('');
        $('#Map_CenterLatitude').val('');
        $('#Map_CenterLongitude').val('');
        $('#Map_BoundsGeoJson').val('');
    }

    map.on('pm:create', function (e) {
        // Remove any previous rectangle
        if (currentRectLayer) {
            map.removeLayer(currentRectLayer);
        }
        currentRectLayer = e.layer;
        updateBoundsFromLayer(currentRectLayer);
    });

    map.on('pm:edit', function (e) {
        if (e.layer) {
            updateBoundsFromLayer(e.layer);
        }
    });

    map.on('pm:remove', function (e) {
        if (e.layer === currentRectLayer) {
            currentRectLayer = null;
            clearBounds();
        }
    });

    // Click to set center point (when no rectangle is being drawn)
    map.on('click', function (e) {
        if (!map.pm.globalDrawModeEnabled()) {
            $('#Map_CenterLatitude').val(e.latlng.lat.toFixed(7));
            $('#Map_CenterLongitude').val(e.latlng.lng.toFixed(7));
        }
    });
});
