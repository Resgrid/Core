$(document).ready(function () {
    var imageBounds = [boundsSW, boundsNE];
    var map;

    if (isTiled) {
        // Tiled layer — use standard map with tile overlay
        map = L.map('regionMap', {
            minZoom: tileMinZoom,
            maxZoom: Math.max(tileMaxZoom, 22)
        });

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 22,
            attribution: '&copy; OpenStreetMap'
        }).addTo(map);

        // Add custom tile layer overlay
        L.tileLayer(tileUrlTemplate, {
            minZoom: tileMinZoom,
            maxZoom: tileMaxZoom,
            opacity: 0.8,
            tms: false
        }).addTo(map);

        map.fitBounds(imageBounds);
    } else if (boundsNE[0] !== 0 && boundsSW[0] !== 0) {
        // Non-tiled with geo bounds
        map = L.map('regionMap', {
            minZoom: 14,
            maxZoom: 22
        });

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 22,
            attribution: '&copy; OpenStreetMap'
        }).addTo(map);

        L.imageOverlay(layerImageUrl, imageBounds, {
            opacity: 0.8,
            interactive: false
        }).addTo(map);

        map.fitBounds(imageBounds);
    } else {
        // Simple CRS (pixel-based)
        map = L.map('regionMap', {
            crs: L.CRS.Simple,
            minZoom: -3,
            maxZoom: 5
        });

        imageBounds = [[0, 0], [1000, 1000]];
        L.imageOverlay(layerImageUrl, imageBounds, {
            opacity: 0.8,
            interactive: false
        }).addTo(map);

        map.fitBounds(imageBounds);
    }

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
    var regionLayerMap = {};

    // Load existing regions
    if (existingRegions && existingRegions.length > 0) {
        existingRegions.forEach(function (region) {
            var geom = region.GeoGeometry || region.PixelGeometry;
            if (geom) {
                try {
                    var coords = JSON.parse(geom);
                    var latLngs;

                    // Check if it's GeoJSON or simple coordinate array
                    if (coords.type) {
                        // GeoJSON object
                        var geoLayer = L.geoJSON(coords, {
                            style: { color: region.Color || '#3388ff', fillOpacity: 0.3 }
                        });
                        geoLayer.addTo(map);
                        geoLayer.bindTooltip(region.Name, { permanent: false, direction: 'center' });
                        geoLayer.regionData = region;
                        regionLayerMap[region.IndoorMapZoneId] = geoLayer;
                        geoLayer.on('click', function () {
                            selectRegion(region, geoLayer);
                        });
                    } else {
                        // Simple coordinate array
                        latLngs = coords.map(function(c) { return [c[1], c[0]]; });
                        var polygon = L.polygon(latLngs, {
                            color: region.Color || '#3388ff',
                            fillOpacity: 0.3
                        }).addTo(map);

                        polygon.bindTooltip(region.Name, { permanent: false, direction: 'center' });
                        polygon.regionData = region;
                        regionLayerMap[region.IndoorMapZoneId] = polygon;

                        polygon.on('click', function () {
                            selectRegion(region, polygon);
                        });
                    }
                } catch (e) {
                    console.error('Failed to parse region geometry', e);
                }
            }
        });
    }

    // When a new shape is created
    map.on('pm:create', function (e) {
        currentLayer = e.layer;
        $('#regionProperties').show();
        $('#regionName').val('');
        $('#regionDescription').val('');
        $('#regionType').val('0');
        $('#regionColor').val('#3388ff');
        $('#regionSearchable').prop('checked', true);
        $('#regionDispatchable').prop('checked', true);
    });

    function selectRegion(region, layer) {
        currentLayer = layer;
        $('#regionProperties').show();
        $('#regionName').val(region.Name);
        $('#regionDescription').val(region.Description || '');
        $('#regionType').val(region.ZoneType);
        $('#regionColor').val(region.Color || '#3388ff');
        $('#regionSearchable').prop('checked', region.IsSearchable);
        $('#regionDispatchable').prop('checked', region.IsDispatchable);
        currentLayer.existingRegionId = region.IndoorMapZoneId;
    }

    // Save region
    $('#saveRegionBtn').on('click', function () {
        if (!currentLayer) return;

        var latLngs;
        if (currentLayer.getLatLngs) {
            latLngs = currentLayer.getLatLngs()[0];
        } else if (currentLayer.getLayers) {
            var layers = currentLayer.getLayers();
            if (layers.length > 0 && layers[0].getLatLngs) {
                latLngs = layers[0].getLatLngs()[0];
            }
        }

        if (!latLngs) return;

        var pixelCoords = latLngs.map(function (ll) { return [ll.lng, ll.lat]; });

        // Calculate centroid
        var cx = 0, cy = 0;
        pixelCoords.forEach(function (c) { cx += c[0]; cy += c[1]; });
        cx /= pixelCoords.length;
        cy /= pixelCoords.length;

        var regionData = {
            IndoorMapZoneId: currentLayer.existingRegionId || '',
            IndoorMapFloorId: layerId,
            Name: $('#regionName').val(),
            Description: $('#regionDescription').val(),
            ZoneType: parseInt($('#regionType').val()),
            PixelGeometry: JSON.stringify(pixelCoords),
            GeoGeometry: '',
            CenterPixelX: cx,
            CenterPixelY: cy,
            CenterLatitude: cy,
            CenterLongitude: cx,
            Color: $('#regionColor').val(),
            Metadata: '',
            IsSearchable: $('#regionSearchable').is(':checked'),
            IsDispatchable: $('#regionDispatchable').is(':checked'),
            IsDeleted: false
        };

        $.ajax({
            url: saveRegionUrl,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(regionData),
            headers: {
                'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val()
            },
            success: function (result) {
                if (result.success) {
                    currentLayer.existingRegionId = result.regionId;
                    if (currentLayer.setStyle) {
                        currentLayer.setStyle({ color: $('#regionColor').val() });
                    }
                    if (currentLayer.bindTooltip) {
                        currentLayer.bindTooltip($('#regionName').val(), { permanent: false, direction: 'center' });
                    }
                    location.reload();
                } else {
                    alert('Failed to save region: ' + (result.message || ''));
                }
            },
            error: function () {
                alert('Error saving region');
            }
        });
    });

    // Delete region
    $(document).on('click', '.delete-region', function (e) {
        e.stopPropagation();
        var regionId = $(this).data('region-id');
        if (!confirm('Delete this region?')) return;

        $.ajax({
            url: deleteRegionUrl,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ RegionId: regionId }),
            headers: {
                'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val()
            },
            success: function (result) {
                if (result.success) {
                    if (regionLayerMap[regionId]) {
                        map.removeLayer(regionLayerMap[regionId]);
                        delete regionLayerMap[regionId];
                    }
                    $('[data-region-id="' + regionId + '"]').remove();
                }
            }
        });
    });
});
