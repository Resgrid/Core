var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var roadclosures;
        (function (roadclosures) {
            // Mapping → Road Closures (M0314). The list page shows every closure on a resgrid.maps map coloured by state; the
            // edit page draws the closed roads (lines) and areas (polygons) with Terra Draw on MapLibre or Leaflet-Geoman on
            // Leaflet into the GeometryJson textarea the form posts, and edits the closure times (department-local).
            var page;

            $(document).ready(function () {
                page = window.roadClosuresPage;
                if (!page || !resgrid.maps) {
                    return;
                }

                if (page.page === 'index') {
                    initIndex();
                } else if (page.page === 'edit') {
                    initEdit();
                }
            });

            // ---- List page ------------------------------------------------------------------------------------------------

            function colorFor(closure) {
                if (closure.state === 'active') {
                    return closure.severity === 'partial' ? '#f8ac59' : '#ed5565';
                }
                return closure.state === 'scheduled' ? '#1c84c6' : '#999999';
            }

            function initIndex() {
                var layers = {};
                var kitMap = null;
                var filter = page.filter === 'all' ? 'all' : 'current';

                function isCurrent(closure) {
                    return closure.state === 'active' || closure.state === 'scheduled';
                }

                function draw(fit) {
                    if (!kitMap) {
                        return;
                    }

                    Object.keys(layers).forEach(function (id) { layers[id].remove(); });
                    layers = {};
                    var points = [];

                    (page.closures || []).forEach(function (closure) {
                        if (!closure.geometry || (filter !== 'all' && !isCurrent(closure))) {
                            return;
                        }

                        var inactive = closure.state !== 'active';
                        var handle = kitMap.addGeoJson('road-closure-' + closure.id, closure.geometry, {
                            color: colorFor(closure),
                            weight: 6,
                            opacity: 0.9,
                            fillOpacity: inactive ? 0.08 : 0.2,
                            dashed: inactive
                        });
                        layers[closure.id] = handle;

                        var bounds = handle.bounds();
                        if (bounds) {
                            points.push(bounds[0], bounds[1]);
                        }
                    });

                    if (fit && points.length > 0) {
                        kitMap.fitBounds(points, { padding: 30, maxZoom: 16 });
                    }
                }

                function applyFilter() {
                    var shown = 0;
                    $('#roadClosuresTable tbody tr').each(function () {
                        var visible = filter === 'all' || $(this).attr('data-current') === '1';
                        $(this).toggle(visible);
                        if (visible) {
                            shown++;
                        }
                    });
                    $('#roadClosuresNoneInFilter').toggle(shown === 0 && $('#roadClosuresTable tbody tr').length > 0);
                    $('#roadClosuresFilter [data-filter]').each(function () {
                        var active = $(this).attr('data-filter') === filter;
                        $(this).toggleClass('btn-primary', active).toggleClass('btn-white', !active);
                    });
                }

                $('#roadClosuresFilter').on('click', '[data-filter]', function () {
                    filter = $(this).attr('data-filter') === 'all' ? 'all' : 'current';
                    applyFilter();
                    draw(true);
                });

                $('#roadClosuresTable').on('click', '[data-show-closure]', function (e) {
                    e.preventDefault();
                    var handle = layers[$(this).attr('data-show-closure')];
                    var bounds = handle ? handle.bounds() : null;
                    if (bounds && kitMap) {
                        kitMap.fitBounds([bounds[0], bounds[1]], { padding: 40, maxZoom: 17 });
                        document.getElementById('roadClosuresMap').scrollIntoView({ behavior: 'smooth', block: 'center' });
                    }
                });

                $('form[data-confirm]').on('submit', function () {
                    return window.confirm($(this).attr('data-confirm'));
                });

                applyFilter();

                resgrid.maps.create('roadClosuresMap', { center: page.center, zoom: 12, scrollZoom: false }).then(function (created) {
                    kitMap = created;
                    draw(true);
                });
            }

            // ---- Edit page ------------------------------------------------------------------------------------------------

            function initEdit() {
                var textarea = document.getElementById('GeometryJson');
                var kitMap = null;
                var drawing = null;
                var drawingReady = null;
                var leafletLayers = [];

                function round(value) {
                    return Math.round(value * 1e7) / 1e7;
                }

                function roundCoordinates(coordinates) {
                    if (typeof coordinates[0] === 'number') {
                        return [round(coordinates[0]), round(coordinates[1])];
                    }
                    return coordinates.map(roundCoordinates);
                }

                /** The drawing as [{ type: 'Polygon'|'LineString', coordinates }]; null when the text is not usable GeoJSON. */
                function parseShapes(text) {
                    if (!$.trim(text)) {
                        return [];
                    }

                    var parsed;
                    try {
                        parsed = JSON.parse(text);
                    } catch (e) {
                        return null;
                    }

                    var shapes = [];
                    var collect = function (node) {
                        if (!node) {
                            return;
                        }
                        switch (node.type) {
                            case 'Polygon':
                            case 'LineString':
                                shapes.push({ type: node.type, coordinates: node.coordinates });
                                break;
                            case 'MultiPolygon':
                                (node.coordinates || []).forEach(function (c) { shapes.push({ type: 'Polygon', coordinates: c }); });
                                break;
                            case 'MultiLineString':
                                (node.coordinates || []).forEach(function (c) { shapes.push({ type: 'LineString', coordinates: c }); });
                                break;
                            case 'Feature':
                                collect(node.geometry);
                                break;
                            case 'FeatureCollection':
                                (node.features || []).forEach(collect);
                                break;
                            case 'GeometryCollection':
                                (node.geometries || []).forEach(collect);
                                break;
                        }
                    };
                    collect(parsed);
                    return shapes;
                }

                function writeShapes(shapes) {
                    if (!shapes || shapes.length === 0) {
                        textarea.value = '';
                        return;
                    }

                    textarea.value = JSON.stringify({
                        type: 'FeatureCollection',
                        features: shapes.map(function (s) {
                            return { type: 'Feature', properties: {}, geometry: { type: s.type, coordinates: roundCoordinates(s.coordinates) } };
                        })
                    });
                }

                function asCollection(shapes) {
                    return {
                        type: 'FeatureCollection',
                        features: shapes.map(function (s) { return { type: 'Feature', properties: {}, geometry: { type: s.type, coordinates: s.coordinates } }; })
                    };
                }

                function fitShapes(shapes) {
                    var points = [];
                    shapes.forEach(function (s) {
                        var positions = s.type === 'Polygon' ? (s.coordinates[0] || []) : (s.coordinates || []);
                        positions.forEach(function (c) { points.push([c[1], c[0]]); });
                    });
                    if (points.length > 0) {
                        kitMap.fitBounds(points, { padding: 30, maxZoom: 17 });
                    }
                }

                function showShapes(shapes, fit) {
                    if (!kitMap) {
                        return;
                    }

                    if (kitMap.engine === 'maplibre') {
                        // Terra Draw takes shapes at start, so a new drawing restarts the editor.
                        drawingReady = (drawingReady || Promise.resolve()).then(function () {
                            if (drawing) {
                                drawing.destroy();
                                drawing = null;
                            }

                            return kitMap.startDrawing({
                                modes: ['linestring', 'polygon', 'rectangle'],
                                color: '#ed5565',
                                initial: shapes && shapes.length ? asCollection(shapes) : null,
                                onChange: function (features) {
                                    writeShapes(features
                                        .filter(function (f) { return f.geometry && (f.geometry.type === 'Polygon' || f.geometry.type === 'LineString'); })
                                        .map(function (f) { return { type: f.geometry.type, coordinates: f.geometry.coordinates }; }));
                                }
                            });
                        }).then(function (handle) {
                            drawing = handle;
                        });
                    } else {
                        var map = kitMap.native;
                        leafletLayers.forEach(function (layer) { map.removeLayer(layer); });
                        leafletLayers = [];

                        (shapes || []).forEach(function (s) {
                            var layer = s.type === 'Polygon'
                                ? L.polygon(s.coordinates.map(function (ring) { return ring.map(function (c) { return [c[1], c[0]]; }); }), { color: '#ed5565', fillOpacity: 0.2 })
                                : L.polyline(s.coordinates.map(function (c) { return [c[1], c[0]]; }), { color: '#ed5565', weight: 6 });
                            layer.addTo(map);
                            trackLeafletLayer(layer);
                        });
                    }

                    if (fit && shapes && shapes.length) {
                        fitShapes(shapes);
                    }
                }

                function startGeoman(map) {
                    map.pm.addControls({
                        position: 'topleft',
                        drawMarker: false,
                        drawCircle: false,
                        drawCircleMarker: false,
                        drawText: false,
                        cutPolygon: false,
                        rotateMode: false,
                        drawPolyline: true,
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
                    writeShapes(leafletLayers
                        .map(function (layer) { return layer.toGeoJSON().geometry; })
                        .filter(function (g) { return g && (g.type === 'Polygon' || g.type === 'LineString'); })
                        .map(function (g) { return { type: g.type, coordinates: g.coordinates }; }));
                }

                resgrid.maps.create('roadClosureMap', { center: page.center, zoom: 14, scrollZoom: true }).then(function (created) {
                    kitMap = created;
                    if (created.engine !== 'maplibre') {
                        startGeoman(created.native);
                    }
                    showShapes(parseShapes(textarea.value) || [], true);
                });

                // A pasted or typed shape redraws the map.
                $(textarea).on('change', function () {
                    var shapes = parseShapes(textarea.value);
                    $('#roadClosureGeoJsonError').toggle(shapes === null);
                    if (shapes !== null) {
                        showShapes(shapes, true);
                    }
                });

                $('#roadClosureClearShape').on('click', function () {
                    textarea.value = '';
                    showShapes([], false);
                });

                // The detour time only matters for full closures.
                function toggleDetour() {
                    $('#roadClosureDetourGroup').toggle($('#roadClosureSeverity').val() === '0');
                }
                $('#roadClosureSeverity').on('change', toggleDetour);
                toggleDetour();

                initWindows();
            }

            // ---- Closure times ---------------------------------------------------------------------------------------------

            function initWindows() {
                var body = $('#roadClosureWindows tbody');
                var template = document.getElementById('roadClosureWindowTemplate');

                function refresh() {
                    $('#roadClosureNoWindows').toggle(body.find('[data-window-row]').length === 0);
                }

                function addRow(start, end) {
                    var row = $(template.content.cloneNode(true)).find('tr');
                    row.find('[data-window-start]').val(start || '');
                    row.find('[data-window-end]').val(end || '');
                    body.append(row);
                    refresh();
                    return row;
                }

                $('#roadClosureAddWindow').on('click', function () {
                    var row = addRow('', '');
                    row.find('[data-window-start]').trigger('focus');
                });

                $('#roadClosureClearWindows').on('click', function () {
                    body.empty();
                    refresh();
                });

                body.on('click', '[data-window-remove]', function () {
                    $(this).closest('tr').remove();
                    refresh();
                });

                // Rows are added and removed freely; the posted names follow the order on the page.
                $('#roadClosureForm').on('submit', function () {
                    body.find('[data-window-row]').each(function (i) {
                        $(this).find('[data-window-start]').attr('name', 'Windows[' + i + '].StartsOn');
                        $(this).find('[data-window-end]').attr('name', 'Windows[' + i + '].EndsOn');
                    });
                });

                $('#roadClosureRepeat').on('click', function () {
                    var error = $('#roadClosureRepeatError').hide();
                    var first = parseDate($('#repeatFirstDate').val());
                    var last = parseDate($('#repeatLastDate').val());
                    var dailyStart = $('#repeatDailyStart').val();
                    var dailyEnd = $('#repeatDailyEnd').val();

                    if (!first || !last || !/^\d{2}:\d{2}/.test(dailyStart || '') || !/^\d{2}:\d{2}/.test(dailyEnd || '') || last < first) {
                        error.text(page.strings.repeatInvalid).show();
                        return;
                    }

                    var days = [];
                    $('[data-repeat-day]:checked').each(function () { days.push(parseInt($(this).attr('data-repeat-day'), 10)); });

                    dailyStart = dailyStart.substring(0, 5);
                    dailyEnd = dailyEnd.substring(0, 5);
                    var overnight = dailyEnd <= dailyStart;
                    var windows = [];
                    var dayMs = 24 * 60 * 60 * 1000;

                    for (var t = first; t <= last; t += dayMs) {
                        var date = new Date(t);
                        if (days.length > 0 && days.indexOf(date.getUTCDay()) < 0) {
                            continue;
                        }
                        windows.push([formatDate(t) + 'T' + dailyStart, formatDate(overnight ? t + dayMs : t) + 'T' + dailyEnd]);
                    }

                    if (body.find('[data-window-row]').length + windows.length > page.maxWindows) {
                        error.text(page.strings.repeatTooMany).show();
                        return;
                    }

                    windows.forEach(function (w) { addRow(w[0], w[1]); });
                });

                refresh();
            }

            /** "yyyy-mm-dd" as a UTC midnight timestamp (days are counted without time zones); null when invalid. */
            function parseDate(value) {
                var match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || '');
                return match ? Date.UTC(parseInt(match[1], 10), parseInt(match[2], 10) - 1, parseInt(match[3], 10)) : null;
            }

            function formatDate(timestamp) {
                var date = new Date(timestamp);
                var pad = function (n) { return n < 10 ? '0' + n : String(n); };
                return date.getUTCFullYear() + '-' + pad(date.getUTCMonth() + 1) + '-' + pad(date.getUTCDate());
            }
        })(roadclosures = mapping.roadclosures || (mapping.roadclosures = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
