$(document).ready(function () {
    if (!document.getElementById('boundsMap')) return;

    var lat = parseFloat(initialLat) || 39.7392;
    var lon = parseFloat(initialLon) || -104.9903;

    // Existing bounds, if present
    var neLat = parseFloat($('#Map_BoundsNELat').val());
    var neLon = parseFloat($('#Map_BoundsNELon').val());
    var swLat = parseFloat($('#Map_BoundsSWLat').val());
    var swLon = parseFloat($('#Map_BoundsSWLon').val());
    var hasBounds = !isNaN(neLat) && !isNaN(neLon) && !isNaN(swLat) && !isNaN(swLon) && neLat !== 0;

    function writeBounds(south, west, north, east, geometry) {
        $('#Map_BoundsNELat').val(north.toFixed(7));
        $('#Map_BoundsNELon').val(east.toFixed(7));
        $('#Map_BoundsSWLat').val(south.toFixed(7));
        $('#Map_BoundsSWLon').val(west.toFixed(7));

        $('#Map_CenterLatitude').val(((north + south) / 2).toFixed(7));
        $('#Map_CenterLongitude').val(((east + west) / 2).toFixed(7));

        $('#Map_BoundsGeoJson').val(JSON.stringify(geometry));
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

    function setCenter(position) {
        $('#Map_CenterLatitude').val(position[0].toFixed(7));
        $('#Map_CenterLongitude').val(position[1].toFixed(7));
    }

    // resgrid.maps: MapLibre (Terra Draw) or Leaflet (Geoman) by the department's Maps.MapLibreWeb flag.
    resgrid.maps.create('boundsMap', { center: [lat, lon], zoom: 16, maxZoom: 22 }).then(function (kitMap) {
        if (hasBounds) {
            kitMap.fitBounds([[swLat, swLon], [neLat, neLon]], { padding: 20, maxZoom: 22 });
        }

        if (kitMap.engine === 'maplibre') {
            startMapLibre(kitMap);
        } else {
            startGeoman(kitMap.native);
        }
    });

    function startMapLibre(kitMap) {
        var drawing = null;
        var initial = hasBounds
            ? { type: 'Polygon', coordinates: [[[swLon, swLat], [neLon, swLat], [neLon, neLat], [swLon, neLat], [swLon, swLat]]] }
            : null;

        kitMap.startDrawing({
            modes: ['rectangle'],
            single: true,
            initial: initial,
            onChange: function (features) {
                var rectangle = features.filter(function (f) { return f.geometry && f.geometry.type === 'Polygon'; })[0];
                if (!rectangle) {
                    clearBounds();
                    return;
                }

                var ring = rectangle.geometry.coordinates[0];
                var lons = ring.map(function (p) { return p[0]; });
                var lats = ring.map(function (p) { return p[1]; });
                writeBounds(Math.min.apply(null, lats), Math.min.apply(null, lons), Math.max.apply(null, lats), Math.max.apply(null, lons), rectangle.geometry);
            }
        }).then(function (handle) {
            drawing = handle;
        });

        // Click to set center point (when no rectangle is being drawn)
        kitMap.onClick(function (position) {
            if (!drawing || drawing.getMode() === 'select') {
                setCenter(position);
            }
        });
    }

    function startGeoman(map) {
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

    if (hasBounds) {
        currentRectLayer = L.rectangle([[swLat, swLon], [neLat, neLon]], { color: '#3388ff', weight: 2, fillOpacity: 0.1 }).addTo(map);
    }

    function updateBoundsFromLayer(layer) {
        var bounds = layer.getBounds();
        var ne = bounds.getNorthEast();
        var sw = bounds.getSouthWest();
        writeBounds(sw.lat, sw.lng, ne.lat, ne.lng, layer.toGeoJSON().geometry);
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
            setCenter([e.latlng.lat, e.latlng.lng]);
        }
    });
    }
});
