var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var layereditor;
        (function (layereditor) {
            // Mapping New/Edit Layer (maplibre-mapping-routing-navigation-plan.md §22.4): the layer's shapes on a
            // resgrid.maps map, Terra Draw with the Resgrid text/cut/rotate extensions on MapLibre and the full Geoman
            // toolbar on Leaflet. Both save the same FeatureCollection: {shape, color, name, category, radius, text}.
            var settings;
            var kitMap = null;
            var drawing = null;
            var leafletMap = null;

            $(document).ready(function () {
                settings = window.layerEditorSettings;
                if (!settings || !document.getElementById('mapContainer') || !resgrid.maps) {
                    return;
                }

                $('#Color').minicolors({
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

                var initial = settings.geoJson && settings.geoJson.features && settings.geoJson.features.length > 0 ? settings.geoJson : null;

                resgrid.maps.create('mapContainer', { center: settings.center, zoom: 13, maxZoom: 22 }).then(function (created) {
                    kitMap = created;

                    if (created.engine === 'maplibre') {
                        created.startDrawing({
                            modes: ['point', 'linestring', 'polygon', 'rectangle', 'circle', 'text'],
                            cut: true,
                            rotate: true,
                            snapping: true,
                            circleAsPoint: true,
                            color: layerColor(),
                            initial: initial,
                            labels: settings.labels
                        }).then(function (handle) {
                            drawing = handle;
                        });
                    } else {
                        leafletMap = created.native;
                        startGeoman(initial);
                    }

                    var points = [];
                    if (initial) {
                        initial.features.forEach(function (f) { collectPoints(f && f.geometry ? f.geometry.coordinates : null, points); });
                    }
                    if (points.length > 0) {
                        created.fitBounds(points, { padding: 30, maxZoom: 17 });
                    }
                });

                $('#' + settings.formId).on('submit', function () {
                    $('#GeoJson').val(JSON.stringify({ type: 'FeatureCollection', features: collect() }));
                    return true;
                });
            });

            function layerColor() {
                return $('#Color').val() || '#0080ff';
            }

            function collectPoints(coordinates, points) {
                if (!coordinates) {
                    return;
                }
                if (typeof coordinates[0] === 'number') {
                    points.push([coordinates[1], coordinates[0]]);
                } else {
                    coordinates.forEach(function (c) { collectPoints(c, points); });
                }
            }

            function startGeoman(initial) {
                // Circles, circle markers and text come back as what they were (they used to reload as plain markers).
                if (initial) {
                    L.geoJSON(initial, {
                        style: function () { return { color: layerColor() }; },
                        pointToLayer: function (feature, latlng) {
                            var p = feature.properties || {};
                            if (p.shape === 'Circle' && Number(p.radius) > 0) {
                                return L.circle(latlng, { radius: Number(p.radius), color: layerColor() });
                            }
                            if (p.shape === 'CircleMarker') {
                                return L.circleMarker(latlng, { color: layerColor() });
                            }
                            if (p.shape === 'Text' && p.text) {
                                return L.marker(latlng, { textMarker: true, text: p.text });
                            }
                            return L.marker(latlng);
                        }
                    }).addTo(leafletMap);
                }

                leafletMap.pm.addControls({ position: 'topleft' });
            }

            /** The layer's shapes in the stored format, from whichever editor the page has. */
            function collect() {
                var common = { color: layerColor(), name: $('#Name').val() };

                if (drawing) {
                    return drawing.getFeatures().map(function (f) {
                        var p = f.properties || {};
                        var shape;
                        switch (p.mode) {
                            case 'polygon': shape = p.shape === 'Rectangle' ? 'Rectangle' : 'Polygon'; break;
                            case 'rectangle': shape = 'Rectangle'; break;
                            case 'linestring': shape = 'Line'; break;
                            case 'circle': shape = 'Circle'; break;
                            case 'text': shape = 'Text'; break;
                            case 'point': shape = p.shape === 'CircleMarker' ? 'CircleMarker' : 'Marker'; break;
                            default: shape = p.shape || 'Polygon';
                        }
                        return feature(f.geometry, shape, p.category, p.radius, p.text, common);
                    });
                }

                if (leafletMap) {
                    return leafletMap.pm.getGeomanLayers().map(function (layer) {
                        var shape = layer.pm.getShape();
                        var stored = layer.feature && layer.feature.properties ? layer.feature.properties : {};
                        return feature(layer.toGeoJSON().geometry, shape, stored.category,
                            shape === 'Circle' ? layer.getRadius() : null,
                            shape === 'Text' ? (layer.pm.getText() || '').trim() : null, common);
                    });
                }

                // Saved before the map finished loading: keep what the layer had rather than emptying it.
                return settings.geoJson && settings.geoJson.features ? settings.geoJson.features : [];
            }

            function feature(geometry, shape, category, radius, text, common) {
                var properties = { shape: shape, color: common.color, name: common.name, category: category || 'default' };
                if (shape === 'Circle' && radius) {
                    properties.radius = radius;
                }
                if (shape === 'Text' && text) {
                    properties.text = text;
                }
                return { type: 'Feature', geometry: geometry, properties: properties };
            }
        })(layereditor = mapping.layereditor || (mapping.layereditor = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
