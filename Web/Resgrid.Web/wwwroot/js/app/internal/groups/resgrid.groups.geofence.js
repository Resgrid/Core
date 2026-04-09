
var resgrid;
(function (resgrid) {
    var groups;
    (function (groups) {
        var geofence;
        (function (geofence) {
            var map;
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

                map = L.map('map_canvas').setView([parseFloat(latitude) || 39.7392, parseFloat(longitude) || -104.9903], 13);
                L.tileLayer(osmTileUrl, {
                    maxZoom: 22,
                    attribution: osmTileAttribution
                }).addTo(map);

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
                if (polyCoordinates && polyCoordinates.length > 0) {
                    try {
                        var latLngs = polyCoordinates.map(function (c) {
                            // Handle new format {lat, lng} or old Google Maps format {k, A}
                            if (c.lat !== undefined && c.lng !== undefined) {
                                return [c.lat, c.lng];
                            } else if (c.k !== undefined && c.A !== undefined) {
                                return [c.k, c.A];
                            }
                            return null;
                        }).filter(function (c) { return c !== null; });

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
            });

            function saveGeofence() {
                var coords = [];
                if (drawnLayer) {
                    var latLngs = drawnLayer.getLatLngs()[0];
                    coords = latLngs.map(function (ll) {
                        return { lat: ll.lat, lng: ll.lng };
                    });
                }

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
                if (drawnLayer) {
                    map.removeLayer(drawnLayer);
                    drawnLayer = null;
                }
            }
            geofence.resetGeofence = resetGeofence;

        })(geofence = groups.geofence || (groups.geofence = {}));
    })(groups = resgrid.groups || (resgrid.groups = {}));
})(resgrid || (resgrid = {}));
