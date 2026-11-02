var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var offlinearea;
        (function (offlinearea) {
            // Mapping Settings, offline maps: the maximum response area editor (maplibre-mapping-routing-navigation-plan.md
            // §19.3). The area is drawn on a resgrid.maps map (Terra Draw on MapLibre, Geoman on Leaflet) or pasted as GeoJSON
            // into the textarea the form posts; every change re-measures the base-map download against the install limit.
            var settings;
            var textarea;
            var kitMap = null;
            var drawing = null;
            var drawingReady = null;
            var leafletLayers = [];
            var estimateTimer = null;
            var estimateRequest = 0;

            $(document).ready(function () {
                settings = window.offlineAreaSettings;
                textarea = document.getElementById('OfflineResponseAreaGeoJson');
                if (!settings || !textarea || !document.getElementById('offlineAreaMap') || !resgrid.maps) {
                    return;
                }

                resgrid.maps.create('offlineAreaMap', { center: settings.center, zoom: 9, scrollZoom: false }).then(function (created) {
                    kitMap = created;
                    if (created.engine !== 'maplibre') {
                        startGeoman(created.native);
                    }
                    showArea(parseArea(textarea.value));
                });

                // A pasted or typed area redraws the map.
                $(textarea).on('change', function () {
                    var polygons = parseArea(textarea.value);
                    if ($.trim(textarea.value) && !polygons) {
                        showEstimate('danger', settings.invalidJson);
                        return;
                    }

                    showArea(polygons);
                    queueEstimate();
                });

                $('#offlineAreaClear').on('click', function () {
                    textarea.value = '';
                    showArea(null);
                    queueEstimate();
                });

                estimate();
            });

            /** The area as a list of polygon coordinate arrays ([[[lng, lat], ...]]); null when empty or not GeoJSON polygons. */
            function parseArea(text) {
                if (!$.trim(text)) {
                    return null;
                }

                var parsed;
                try {
                    parsed = JSON.parse(text);
                } catch (e) {
                    return null;
                }

                var polygons = [];
                var collect = function (geometry) {
                    if (!geometry) {
                        return;
                    }
                    if (geometry.type === 'Polygon' && geometry.coordinates) {
                        polygons.push(geometry.coordinates);
                    } else if (geometry.type === 'MultiPolygon' && geometry.coordinates) {
                        geometry.coordinates.forEach(function (p) { polygons.push(p); });
                    } else if (geometry.type === 'Feature') {
                        collect(geometry.geometry);
                    } else if (geometry.type === 'FeatureCollection' && geometry.features) {
                        geometry.features.forEach(collect);
                    }
                };
                collect(parsed);

                return polygons.length > 0 ? polygons : null;
            }

            /** Polygon or MultiPolygon GeoJSON for the textarea, coordinates rounded to about 10 cm. */
            function writeArea(polygons) {
                if (!polygons || polygons.length === 0) {
                    textarea.value = '';
                    return;
                }

                var rounded = polygons.map(function (polygon) {
                    return polygon.map(function (ring) {
                        return ring.map(function (p) { return [Math.round(p[0] * 1e6) / 1e6, Math.round(p[1] * 1e6) / 1e6]; });
                    });
                });

                textarea.value = JSON.stringify(rounded.length === 1
                    ? { type: 'Polygon', coordinates: rounded[0] }
                    : { type: 'MultiPolygon', coordinates: rounded });
            }

            /** Puts the area on the map and fits it; null clears the map. */
            function showArea(polygons) {
                if (!kitMap) {
                    return;
                }

                if (kitMap.engine === 'maplibre') {
                    // Terra Draw takes shapes at start, so a new area restarts the editor.
                    drawingReady = (drawingReady || Promise.resolve()).then(function () {
                        if (drawing) {
                            drawing.destroy();
                            drawing = null;
                        }

                        return kitMap.startDrawing({
                            modes: ['polygon', 'rectangle'],
                            color: '#1c84c6',
                            initial: polygons ? {
                                type: 'FeatureCollection',
                                features: polygons.map(function (p) { return { type: 'Feature', properties: {}, geometry: { type: 'Polygon', coordinates: p } }; })
                            } : null,
                            onChange: function (features) {
                                writeArea(features
                                    .filter(function (f) { return f.geometry && f.geometry.type === 'Polygon'; })
                                    .map(function (f) { return f.geometry.coordinates; }));
                                queueEstimate();
                            }
                        });
                    }).then(function (handle) {
                        drawing = handle;
                    });
                } else {
                    var map = kitMap.native;
                    leafletLayers.forEach(function (layer) { map.removeLayer(layer); });
                    leafletLayers = [];

                    (polygons || []).forEach(function (p) {
                        var layer = L.polygon(p.map(function (ring) { return ring.map(function (c) { return [c[1], c[0]]; }); }), { color: '#1c84c6', fillOpacity: 0.1 }).addTo(map);
                        trackLeafletLayer(layer);
                    });
                }

                if (polygons) {
                    var points = [];
                    polygons.forEach(function (p) { p[0].forEach(function (c) { points.push([c[1], c[0]]); }); });
                    kitMap.fitBounds(points, { padding: 20 });
                }
            }

            function startGeoman(map) {
                map.pm.addControls({
                    position: 'topleft',
                    drawMarker: false,
                    drawPolyline: false,
                    drawCircle: false,
                    drawCircleMarker: false,
                    drawText: false,
                    cutPolygon: false,
                    rotateMode: false,
                    drawRectangle: true,
                    drawPolygon: true,
                    editMode: true,
                    dragMode: true,
                    removalMode: true
                });

                map.on('pm:create', function (e) {
                    trackLeafletLayer(e.layer);
                    leafletChanged();
                });

                map.on('pm:remove', function (e) {
                    leafletLayers = leafletLayers.filter(function (layer) { return layer !== e.layer; });
                    leafletChanged();
                });
            }

            function trackLeafletLayer(layer) {
                leafletLayers.push(layer);
                layer.on('pm:edit pm:dragend', leafletChanged);
            }

            function leafletChanged() {
                writeArea(leafletLayers.map(function (layer) { return layer.toGeoJSON().geometry; })
                    .filter(function (g) { return g && g.type === 'Polygon'; })
                    .map(function (g) { return g.coordinates; }));
                queueEstimate();
            }

            function queueEstimate() {
                if (estimateTimer) {
                    clearTimeout(estimateTimer);
                }
                estimateTimer = setTimeout(estimate, 500);
            }

            /** Measures the area being drawn (or the fallback circle when none is) and shows the result under the map. */
            function estimate() {
                var request = ++estimateRequest;
                showEstimate('info', settings.measuring);

                $.ajax({
                    url: settings.estimateUrl,
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify({ GeoJson: textarea.value }),
                    headers: {
                        'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val()
                    }
                }).done(function (result) {
                    if (request === estimateRequest) {
                        showEstimate(result.level, result.message, result.percent);
                    }
                }).fail(function () {
                    if (request === estimateRequest) {
                        showEstimate('warning', settings.failed);
                    }
                });
            }

            function showEstimate(level, message, percent) {
                var box = $('#offlineAreaEstimate');
                box.removeClass('alert-info alert-success alert-warning alert-danger').addClass('alert-' + (level || 'info')).empty();
                box.append($('<div></div>').text(message || ''));

                if (percent !== null && percent !== undefined) {
                    var bar = $('<div class="progress-bar" role="progressbar"></div>')
                        .addClass(level === 'danger' ? 'progress-bar-danger' : level === 'warning' ? 'progress-bar-warning' : 'progress-bar-success')
                        .attr({ 'aria-valuenow': percent, 'aria-valuemin': 0, 'aria-valuemax': 100 })
                        .css('width', percent + '%');
                    box.append($('<div class="progress progress-mini m-t-xs m-b-none"></div>').append(bar));
                }

                box.show();
            }
        })(offlinearea = department.offlinearea || (department.offlinearea = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
