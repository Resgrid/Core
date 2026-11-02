
var resgrid;
(function (resgrid) {
    var groups;
    (function (groups) {
        var geofence;
        (function (geofence) {
            var map;
            var kitMap = null;
            var drawing = null;
            var drawnLayer = null;

            $(document).ready(function () {
                resgrid.common.analytics.track('Group Geofence');

                $("#colorPicker").minicolors({
                    animationSpeed: 50,
                    animationEasing: 'swing',
                    changeDelay: 0,
                    control: 'hue',
                    defaultValue: '#0080ff',
                    format: 'hex',
                    showSpeed: 100,
                    hideSpeed: 100,
                    inline: false,
                    theme: 'bootstrap'
                });

                // Stored outline: {lat, lng} points, or the old Google Maps {k, A} ones.
                var latLngs = [];
                if (polyCoordinates && polyCoordinates.length > 0) {
                    latLngs = polyCoordinates.map(function (c) {
                        if (c.lat !== undefined && c.lng !== undefined) {
                            return [Number(c.lat), Number(c.lng)];
                        } else if (c.k !== undefined && c.A !== undefined) {
                            return [Number(c.k), Number(c.A)];
                        }
                        return null;
                    }).filter(function (c) { return c !== null; });
                }

                // resgrid.maps: MapLibre (Terra Draw) or Leaflet (Geoman) by the department's Maps.MapLibreWeb flag.
                resgrid.maps.create('map_canvas', { center: [parseFloat(latitude) || 39.7392, parseFloat(longitude) || -104.9903], zoom: 13, maxZoom: 22 }).then(function (created) {
                    kitMap = created;
                    if (created.engine === 'maplibre') {
                        startMapLibreDrawing(created, latLngs);
                    } else {
                        map = created.native;
                        startGeomanDrawing(latLngs);
                    }
                });
            });

            function startMapLibreDrawing(created, latLngs) {
                var ring = latLngs.map(function (p) { return [p[1], p[0]]; });
                if (ring.length > 2) {
                    ring.push(ring[0]);
                    created.fitBounds(latLngs);
                }

                created.startDrawing({
                    modes: ['polygon'],
                    single: true,
                    color: $('#colorPicker').val() || '#f70c16',
                    initial: ring.length > 3 ? { type: 'Polygon', coordinates: [ring] } : null
                }).then(function (handle) {
                    drawing = handle;
                });
            }

            function startGeomanDrawing(latLngs) {
                map.pm.addControls({
                    position: 'topleft',
                    drawMarker: false,
                    drawPolyline: false,
                    drawCircle: false,
                    drawCircleMarker: false,
                    drawRectangle: false,
                    drawText: false,
                    cutPolygon: false,
                    rotateMode: false,
                    drawPolygon: true,
                    editMode: true,
                    dragMode: false,
                    removalMode: true
                });

                // Load existing geofence
                if (latLngs.length > 0) {
                    try {
                        if (latLngs.length > 0) {
                            drawnLayer = L.polygon(latLngs, {
                                color: $('#colorPicker').val() || '#f70c16',
                                fillOpacity: 0.3
                            }).addTo(map);
                            map.fitBounds(drawnLayer.getBounds());
                        }
                    } catch (e) {
                        console.error('Failed to load existing geofence', e);
                    }
                }

                map.on('pm:create', function (e) {
                    if (drawnLayer) {
                        map.removeLayer(drawnLayer);
                    }
                    drawnLayer = e.layer;
                });

                map.on('pm:remove', function (e) {
                    if (drawnLayer === e.layer) {
                        drawnLayer = null;
                    }
                });
            }

            /** The outline as {lat, lng} points (the stored format), from whichever editor the page has. */
            function currentCoordinates() {
                if (drawing) {
                    var polygon = drawing.getFeatures().filter(function (f) { return f.geometry && f.geometry.type === 'Polygon'; })[0];
                    if (!polygon) {
                        return [];
                    }
                    var ring = polygon.geometry.coordinates[0].slice(0, -1);
                    return ring.map(function (p) { return { lat: p[1], lng: p[0] }; });
                }

                if (drawnLayer) {
                    return drawnLayer.getLatLngs()[0].map(function (ll) {
                        return { lat: ll.lat, lng: ll.lng };
                    });
                }

                return [];
            }

            function saveGeofence() {
                var coords = currentCoordinates();

                var groupId = $('#Group_DepartmentGroupId').val();
                if (!groupId) {
                    $('#alertArea').html('<p>Unable to determine the group. Please reload the page and try again.</p>');
                    $('#successArea').hide();
                    $('#alertArea').show();
                    return;
                }

                $.ajax({
                    type: "POST",
                    async: true,
                    url: resgrid.absoluteBaseUrl + '/User/Groups/SaveGeofence',
                    contentType: 'application/json',
                    headers: { 'RequestVerificationToken': $('meta[name="request-verification-token"]').attr('content') },
                    cache: false,
                    processData: false,
                    data: JSON.stringify({
                        DepartmentGroupId: parseInt(groupId, 10),
                        Color: $('#colorPicker').val(),
                        GeoFence: JSON.stringify(coords)
                    })
                }).done(function (data) {
                    if (data && data.Success) {
                        $('#successArea').html('<p>' + (data.Message || 'Your geofence has been saved.') + '</p>');
                        $('#successArea').show();
                        $('#alertArea').hide();
                    } else {
                        $('#alertArea').html('<p>' + (data && data.Message ? data.Message : 'Your geofence could not be saved.') + '</p>');
                        $('#successArea').hide();
                        $('#alertArea').show();
                    }
                }).fail(function (error) {
                    $('#alertArea').html('<p>Your geofence could not be saved. Please correct the errors and try again.</p>');
                    $('#successArea').hide();
                    $('#alertArea').show();
                });
            }
            geofence.saveGeofence = saveGeofence;

            function resetGeofence() {
                if (drawing) {
                    drawing.clear();
                }
                if (drawnLayer) {
                    map.removeLayer(drawnLayer);
                    drawnLayer = null;
                }
            }
            geofence.resetGeofence = resetGeofence;

        })(geofence = groups.geofence || (groups.geofence = {}));
    })(groups = resgrid.groups || (resgrid.groups = {}));
})(resgrid || (resgrid = {}));
