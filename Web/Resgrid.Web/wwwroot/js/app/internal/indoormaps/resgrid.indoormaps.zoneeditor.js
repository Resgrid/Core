$(document).ready(function () {
    var imageBounds = [boundsSW, boundsNE];
    var map = L.map('zoneMap', {
        crs: L.CRS.Simple,
        minZoom: -3,
        maxZoom: 5
    });

    // Use CRS.Simple with pixel-based bounds
    var bounds = [[0, 0], [1000, 1000]];

    // If we have geo bounds, use EPSG3857
    if (boundsNE[0] !== 0 && boundsSW[0] !== 0) {
        map.remove();
        map = L.map('zoneMap', {
            minZoom: 14,
            maxZoom: 22
        });

        L.tileLayer(osmTileUrl, {
            maxZoom: 22,
            attribution: osmTileAttribution
        }).addTo(map);

        imageBounds = [boundsSW, boundsNE];
    } else {
        imageBounds = [[0, 0], [1000, 1000]];
    }

    var imageOverlay = L.imageOverlay(floorImageUrl, imageBounds, {
        opacity: 0.8,
        interactive: false
    }).addTo(map);

    map.fitBounds(imageBounds);

    // Add Geoman controls
    map.pm.addControls({
        position: 'topleft',
        drawCircle: false,
        drawCircleMarker: false,
        drawMarker: false,
        drawPolyline: false,
        drawText: false,
        cutPolygon: false,
        rotateMode: false
    });

    var currentLayer = null;
    var zoneLayerMap = {};

    // Load existing zones
    if (existingZones && existingZones.length > 0) {
        existingZones.forEach(function (zone) {
            if (zone.PixelGeometry) {
                try {
                    var coords = JSON.parse(zone.PixelGeometry);
                    var latLngs = coords.map(function(c) { return [c[1], c[0]]; });
                    var polygon = L.polygon(latLngs, {
                        color: zone.Color || '#3388ff',
                        fillOpacity: 0.3
                    }).addTo(map);

                    polygon.bindTooltip(zone.Name, { permanent: false, direction: 'center' });
                    polygon.zoneData = zone;
                    zoneLayerMap[zone.IndoorMapZoneId] = polygon;

                    polygon.on('click', function () {
                        selectZone(zone, polygon);
                    });
                } catch (e) {
                    console.error('Failed to parse zone geometry', e);
                }
            }
        });
    }

    // When a new shape is created
    map.on('pm:create', function (e) {
        currentLayer = e.layer;
        $('#zoneProperties').show();
        $('#zoneName').val('');
        $('#zoneDescription').val('');
        $('#zoneType').val('0');
        $('#zoneColor').val('#3388ff');
        $('#zoneSearchable').prop('checked', true);
    });

    function selectZone(zone, layer) {
        currentLayer = layer;
        $('#zoneProperties').show();
        $('#zoneName').val(zone.Name);
        $('#zoneDescription').val(zone.Description || '');
        $('#zoneType').val(zone.ZoneType);
        $('#zoneColor').val(zone.Color || '#3388ff');
        $('#zoneSearchable').prop('checked', zone.IsSearchable);
        currentLayer.existingZoneId = zone.IndoorMapZoneId;
    }

    // Save zone
    $('#saveZoneBtn').on('click', function () {
        if (!currentLayer) return;

        var latLngs = currentLayer.getLatLngs()[0];
        var pixelCoords = latLngs.map(function (ll) { return [ll.lng, ll.lat]; });

        // Calculate centroid
        var cx = 0, cy = 0;
        pixelCoords.forEach(function (c) { cx += c[0]; cy += c[1]; });
        cx /= pixelCoords.length;
        cy /= pixelCoords.length;

        var zoneData = {
            IndoorMapZoneId: currentLayer.existingZoneId || '',
            IndoorMapFloorId: floorId,
            Name: $('#zoneName').val(),
            Description: $('#zoneDescription').val(),
            ZoneType: parseInt($('#zoneType').val()),
            PixelGeometry: JSON.stringify(pixelCoords),
            GeoGeometry: '',
            CenterPixelX: cx,
            CenterPixelY: cy,
            CenterLatitude: cy,
            CenterLongitude: cx,
            Color: $('#zoneColor').val(),
            Metadata: '',
            IsSearchable: $('#zoneSearchable').is(':checked'),
            IsDeleted: false
        };

        $.ajax({
            url: saveZoneUrl,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(zoneData),
            headers: {
                'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val()
            },
            success: function (result) {
                if (result.success) {
                    currentLayer.existingZoneId = result.zoneId;
                    currentLayer.setStyle({ color: $('#zoneColor').val() });
                    currentLayer.bindTooltip($('#zoneName').val(), { permanent: false, direction: 'center' });
                    location.reload();
                } else {
                    alert('Failed to save zone: ' + (result.message || ''));
                }
            },
            error: function () {
                alert('Error saving zone');
            }
        });
    });

    // Delete zone
    $(document).on('click', '.delete-zone', function (e) {
        e.stopPropagation();
        var zoneId = $(this).data('zone-id');
        if (!confirm('Delete this zone?')) return;

        $.ajax({
            url: deleteZoneUrl,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ ZoneId: zoneId }),
            headers: {
                'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val()
            },
            success: function (result) {
                if (result.success) {
                    if (zoneLayerMap[zoneId]) {
                        map.removeLayer(zoneLayerMap[zoneId]);
                        delete zoneLayerMap[zoneId];
                    }
                    $('[data-zone-id="' + zoneId + '"]').remove();
                }
            }
        });
    });
});
