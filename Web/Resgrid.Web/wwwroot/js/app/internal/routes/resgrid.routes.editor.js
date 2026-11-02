var resgrid;
(function (resgrid) {
    var routes;
    (function (routes) {
        var editor;
        (function (editor) {
            // The route plan editor shared by Routes New and Edit (maplibre-mapping-routing-navigation-plan.md §10.6): the
            // stop list with drag and button reordering, per-leg distance and time with the arrival offset (dwell included),
            // totals, a live route preview through the server's routing (not saved until Save), the routing profile with
            // its vehicle limits, roads to avoid, start and end stations, and the optimized stop order (saved plans only).
            //
            // The page owns its stops and saving: it passes getStops(), and onReorder/onDelete/onMove callbacks.
            var settings;
            var options;
            var map = null;
            var routeLine = null;
            var markers = [];
            var legs = null;
            var previewTimer = null;
            var previewRequest = 0;
            var lastPointCount = -1;
            var dragKey = null;

            editor.init = function (pageOptions) {
                settings = window.routeEditorSettings || { stations: [], unitStations: {}, unitVehicles: {}, labels: {} };
                options = pageOptions;

                if (options.mapReady) {
                    options.mapReady.then(function (created) {
                        map = created;
                        editor.refresh();
                    });
                }

                // Every input that changes the route re-runs the preview.
                $('#Plan_RoutingProfile, #Plan_UnitId, #Plan_StartDepartmentGroupId, #Plan_EndDepartmentGroupId, #chkUseStationAsStart, #chkUseStationAsEnd, #Plan_RouteColor')
                    .on('change', function () {
                        updateStationPickers();
                        updateTruckInfo();
                        queuePreview();
                    });
                $('#startLat, #endLat').on('change', queuePreview);

                $('.route-avoid').on('change', function () {
                    var flags = 0;
                    $('.route-avoid:checked').each(function () { flags |= Number($(this).data('flag')); });
                    $('#Plan_AvoidOptions').val(flags);
                    queuePreview();
                });

                $('#stopsTableBody')
                    .on('click', '.route-stop-up', function () { move($(this).data('key'), -1); })
                    .on('click', '.route-stop-down', function () { move($(this).data('key'), 1); })
                    .on('click', '.route-stop-delete', function () { options.onDelete(String($(this).data('key'))); })
                    .on('dragstart', 'tr', function (e) {
                        dragKey = String($(this).data('key'));
                        e.originalEvent.dataTransfer.effectAllowed = 'move';
                        e.originalEvent.dataTransfer.setData('text/plain', dragKey);
                    })
                    .on('dragover', 'tr', function (e) {
                        if (dragKey !== null) {
                            e.preventDefault();
                        }
                    })
                    .on('drop', 'tr', function (e) {
                        e.preventDefault();
                        var target = String($(this).data('key'));
                        if (dragKey !== null && target !== dragKey) {
                            var keys = stopKeys().filter(function (k) { return k !== dragKey; });
                            var at = keys.indexOf(target);
                            // Dropped on a row further down: after it; further up: before it.
                            var from = stopKeys().indexOf(dragKey);
                            keys.splice(from < stopKeys().indexOf(target) ? at + 1 : at, 0, dragKey);
                            options.onReorder(keys);
                        }
                        dragKey = null;
                    })
                    .on('dragend', 'tr', function () { dragKey = null; });

                if (options.planId) {
                    $('#optimizeStopsBtn').show().on('click', previewOptimization);
                    $('#optimizeAcceptBtn').on('click', function () {
                        var proposed = $(this).data('order');
                        $('#optimizeModal').modal('hide');
                        if (proposed && proposed.length) {
                            options.onReorder(proposed);
                        }
                    });
                }

                updateStationPickers();
                updateTruckInfo();
                editor.refresh();
            };

            /** Redraws the stop list and markers and re-runs the route preview (the page calls it after any stop change). */
            editor.refresh = function () {
                if (!options) {
                    return;
                }
                legs = null;
                renderTable();
                drawMarkers();
                queuePreview();
            };

            function label(key, fallback) {
                return (settings.labels && settings.labels[key]) || fallback || key;
            }

            function escapeHtml(value) {
                return String(value == null ? '' : value)
                    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            }

            function stops() {
                return options.getStops() || [];
            }

            function stopKeys() {
                return stops().map(function (s) { return String(s.key); });
            }

            function hasPoint(lat, lng) {
                return lat !== null && lat !== undefined && lat !== '' && lng !== null && lng !== undefined && lng !== '' &&
                    isFinite(Number(lat)) && isFinite(Number(lng));
            }

            function move(key, delta) {
                var keys = stopKeys();
                var index = keys.indexOf(String(key));
                var to = index + delta;
                if (index < 0 || to < 0 || to >= keys.length) {
                    return;
                }
                keys.splice(index, 1);
                keys.splice(to, 0, String(key));
                options.onReorder(keys);
            }

            // ---- Formatting ---------------------------------------------------------------------------------------------

            function distanceText(meters) {
                if (settings.metric) {
                    return meters >= 1000 ? (meters / 1000).toFixed(meters >= 10000 ? 0 : 1) + ' ' + label('unitKm', 'km') : Math.round(meters) + ' ' + label('unitM', 'm');
                }
                var miles = meters / 1609.344;
                return miles >= 0.1 ? miles.toFixed(miles >= 10 ? 0 : 1) + ' ' + label('unitMi', 'mi') : Math.round(meters * 3.28084) + ' ' + label('unitFt', 'ft');
            }

            function durationText(seconds) {
                var minutes = Math.round(seconds / 60);
                if (minutes < 60) {
                    return minutes + ' ' + label('unitMin', 'min');
                }
                return Math.floor(minutes / 60) + ' ' + label('unitH', 'h') + ' ' + ('0' + (minutes % 60)).slice(-2) + ' ' + label('unitMin', 'min');
            }

            // ---- Stop list ----------------------------------------------------------------------------------------------

            function renderTable() {
                var list = stops();
                var $body = $('#stopsTableBody').empty();

                list.forEach(function (stop, index) {
                    var leg = legs ? legs.byKey[String(stop.key)] : null;
                    var $row = $('<tr draggable="true"></tr>').attr('data-key', stop.key);
                    $row.append($('<td style="white-space:nowrap;"></td>').html('<i class="fa fa-bars text-muted" style="cursor:move;" aria-hidden="true"></i> ' + (index + 1)));
                    $row.append($('<td></td>').text(stop.name || ''));
                    $row.append($('<td></td>').text(label('stopType' + stop.stopType, String(stop.stopType))));
                    $row.append($('<td></td>').text(stop.address || ''));
                    $row.append($('<td></td>').text(label('priority' + stop.priority, String(stop.priority))));
                    $row.append($('<td style="white-space:nowrap;"></td>').text(leg ? distanceText(leg.distance) + ' · ' + durationText(leg.duration) : ''));
                    $row.append($('<td style="white-space:nowrap;"></td>').text(leg ? '+' + durationText(leg.offset) : ''));
                    $row.append($('<td style="white-space:nowrap;"></td>').html(
                        '<button type="button" class="btn btn-xs btn-white route-stop-up" data-key="' + escapeHtml(stop.key) + '" title="' + escapeHtml(label('moveUp', 'Move up')) + '"' + (index === 0 ? ' disabled' : '') + '><i class="fa fa-arrow-up"></i></button> ' +
                        '<button type="button" class="btn btn-xs btn-white route-stop-down" data-key="' + escapeHtml(stop.key) + '" title="' + escapeHtml(label('moveDown', 'Move down')) + '"' + (index === list.length - 1 ? ' disabled' : '') + '><i class="fa fa-arrow-down"></i></button> ' +
                        '<button type="button" class="btn btn-xs btn-danger route-stop-delete" data-key="' + escapeHtml(stop.key) + '" title="' + escapeHtml(label('deleteStop', 'Delete')) + '"><i class="fa fa-trash"></i></button>'));
                    $body.append($row);
                });

                $('#stopCount').text(list.length);
                $('#optimizeStopsBtn').prop('disabled', list.length < 3);
            }

            // ---- Map ----------------------------------------------------------------------------------------------------

            function endpointMarker(text, cssClass) {
                return '<span class="label ' + cssClass + '" style="font-size:11px;box-shadow:0 0 2px rgba(0,0,0,.6);">' + escapeHtml(text) + '</span>';
            }

            function drawMarkers() {
                if (!map) {
                    return;
                }

                markers.forEach(function (marker) { marker.remove(); });
                markers = [];

                stops().forEach(function (stop, index) {
                    if (!hasPoint(stop.lat, stop.lng)) {
                        return;
                    }
                    var marker = map.addMarker([Number(stop.lat), Number(stop.lng)], {
                        label: (index + 1) + '. ' + (stop.name || ''),
                        popupHtml: '<strong>' + (index + 1) + '. ' + escapeHtml(stop.name) + '</strong>',
                        draggable: !!options.onMove
                    });
                    if (options.onMove) {
                        marker.onDragEnd(function (position) { options.onMove(String(stop.key), position[0], position[1]); });
                    }
                    markers.push(marker);
                });

                var start = resolveEndpoint('Start');
                var end = resolveEndpoint('End');
                if (start) {
                    markers.push(map.addMarker(start, { html: endpointMarker(label('start', 'Start'), 'label-primary'), title: label('start', 'Start') }));
                }
                if (end && !(start && start[0] === end[0] && start[1] === end[1])) {
                    markers.push(map.addMarker(end, { html: endpointMarker(label('end', 'End'), 'label-default'), title: label('end', 'End') }));
                }
            }

            /** The route's start or end: a chosen station, else the unit's station, else the typed or picked point. */
            function resolveEndpoint(which) {
                if ($('#chkUseStationAs' + which).prop('checked')) {
                    var groupId = $('#Plan_' + which + 'DepartmentGroupId').val();
                    if (!groupId) {
                        groupId = settings.unitStations[$('#Plan_UnitId').val()];
                    }
                    var station = (settings.stations || []).filter(function (s) { return String(s.Id) === String(groupId); })[0];
                    return station && hasPoint(station.Latitude, station.Longitude) ? [Number(station.Latitude), Number(station.Longitude)] : null;
                }

                var prefix = which === 'Start' ? '#start' : '#end';
                var lat = $(prefix + 'Lat').val();
                var lng = $(prefix + 'Lng').val();
                return hasPoint(lat, lng) ? [Number(lat), Number(lng)] : null;
            }

            function updateStationPickers() {
                $('#startStationGroup').toggle($('#chkUseStationAsStart').prop('checked'));
                $('#endStationGroup').toggle($('#chkUseStationAsEnd').prop('checked'));
            }

            // ---- Profile and vehicle ------------------------------------------------------------------------------------

            function vehicleText(vehicle) {
                var parts = [];
                var length = function (m) {
                    return settings.metric ? (Math.round(m * 100) / 100) + ' m' : Math.floor(m / 0.3048) + "' " + Math.round(((m / 0.3048) % 1) * 12) + '"';
                };
                var weight = function (kg) {
                    return settings.metric ? Math.round(kg).toLocaleString() + ' kg' : Math.round(kg / 0.45359237).toLocaleString() + ' lb';
                };
                if (vehicle.HeightMeters) parts.push(label('height', 'Height') + ' ' + length(vehicle.HeightMeters));
                if (vehicle.WidthMeters) parts.push(label('width', 'Width') + ' ' + length(vehicle.WidthMeters));
                if (vehicle.LengthMeters) parts.push(label('length', 'Length') + ' ' + length(vehicle.LengthMeters));
                if (vehicle.GrossWeightKg) parts.push(label('weight', 'Weight') + ' ' + weight(vehicle.GrossWeightKg));
                if (vehicle.AxleLoadKg) parts.push(label('axleLoad', 'Axle load') + ' ' + weight(vehicle.AxleLoadKg));
                if (vehicle.AxleCount) parts.push(label('axles', 'Axles') + ' ' + vehicle.AxleCount);
                if (vehicle.Hazmat) parts.push(label('hazmat', 'Hazardous materials'));
                return parts.join(' · ');
            }

            function updateTruckInfo() {
                var $info = $('#truckInfo');
                if (!$info.length) {
                    return;
                }
                if (Number($('#Plan_RoutingProfile').val()) !== 2) {
                    $info.hide();
                    return;
                }

                var vehicle = settings.unitVehicles[$('#Plan_UnitId').val()];
                var limits = { all: label('limitsAll'), size: label('limitsSize') }[settings.vehicleLimits] || label('limitsNone');
                $info.empty()
                    .append($('<div></div>').text(vehicle ? vehicleText(vehicle) : label('truckNoVehicle', 'No vehicle size is set for this unit; Truck routes as Driving.')))
                    .append($('<div class="text-muted small"></div>').text(limits + ' ' + label('caveat', '')))
                    .show();
            }

            // ---- Route preview ------------------------------------------------------------------------------------------

            function queuePreview() {
                if (previewTimer) {
                    clearTimeout(previewTimer);
                }
                previewTimer = setTimeout(preview, 600);
            }

            function preview() {
                if (!map || !resgrid.maps || !resgrid.maps.route) {
                    return;
                }

                drawMarkers();

                var start = resolveEndpoint('Start');
                var end = resolveEndpoint('End');
                var routed = stops().filter(function (s) { return hasPoint(s.lat, s.lng); });
                var points = [];
                if (start) points.push(start);
                routed.forEach(function (s) { points.push([Number(s.lat), Number(s.lng)]); });
                if (end) points.push(end);

                var request = ++previewRequest;
                if (points.length < 2) {
                    showRoute(null, routed, !!start);
                    return;
                }

                var unitId = $('#Plan_UnitId').val();
                resgrid.maps.route(points, {
                    profile: Number($('#Plan_RoutingProfile').val() || 0),
                    avoid: Number($('#Plan_AvoidOptions').val() || 0),
                    unitId: unitId ? Number(unitId) : undefined,
                    steps: false,
                    metric: !!settings.metric
                }).then(function (data) {
                    if (request === previewRequest) {
                        showRoute(data, routed, !!start, points);
                    }
                }).catch(function () {
                    if (request === previewRequest) {
                        showRoute(null, routed, !!start);
                    }
                });
            }

            function showRoute(data, routed, hasStart, points) {
                var color = $('#Plan_RouteColor').val() || '#3388ff';

                if (!data) {
                    if (routeLine) {
                        routeLine.remove();
                        routeLine = null;
                    }
                    legs = null;
                    $('#routeTotals').text('');
                    $('#routeAttribution').text('');
                    renderTable();
                    return;
                }

                var geometry = JSON.parse(data.Geometry);
                if (routeLine) {
                    routeLine.remove();
                }
                routeLine = map.addLine('route-plan-preview', geometry, { color: color, weight: 5, opacity: 0.8 });

                // Leg i runs from point i to point i + 1; with no start, the first stop has no leg.
                var byKey = {};
                var offset = 0;
                var dwellBefore = 0;
                routed.forEach(function (stop, index) {
                    var legIndex = hasStart ? index : index - 1;
                    var leg = legIndex >= 0 ? (data.Legs || [])[legIndex] : null;
                    if (leg) {
                        offset += leg.DurationSeconds + dwellBefore;
                        byKey[String(stop.key)] = { distance: leg.DistanceMeters, duration: leg.DurationSeconds, offset: offset };
                    }
                    dwellBefore = (Number(stop.dwellMinutes) || 0) * 60;
                });
                legs = { byKey: byKey };

                var dwell = routed.reduce(function (sum, s) { return sum + (Number(s.dwellMinutes) || 0) * 60; }, 0);
                var totals = label('total', 'Total') + ': ' + distanceText(data.DistanceMeters) + ' · ' + durationText(data.DurationSeconds);
                if (dwell > 0) {
                    totals += ' (' + label('withStops', 'with stops') + ' ' + durationText(data.DurationSeconds + dwell) + ')';
                }
                if (data.IsEstimate) {
                    totals += ' · ' + label('estimate', 'straight-line estimate');
                }
                $('#routeTotals').text(totals);
                $('#routeAttribution').text(data.Attribution || settings.attribution || '');

                renderTable();

                if (points && points.length !== lastPointCount) {
                    lastPointCount = points.length;
                    map.fitBounds(points, { padding: 40 });
                }
            }

            // ---- Optimized order (saved plans) -------------------------------------------------------------------------

            function previewOptimization() {
                var $button = $('#optimizeStopsBtn').prop('disabled', true);
                $.getJSON(settings.previewUrl, { id: options.planId })
                    .done(function (result) {
                        if (!result || !result.successful) {
                            alert(label('optimizeFailed', 'The stop order could not be optimized.'));
                            return;
                        }
                        if (!result.changed) {
                            alert(label('optimizeNoChange', 'The stops are already in the best order.'));
                            return;
                        }

                        var list = function (items) {
                            var $ol = $('<ol class="m-b-sm" style="padding-left:20px;"></ol>');
                            items.forEach(function (item) { $ol.append($('<li></li>').text(item.name)); });
                            return $ol;
                        };
                        var saved = function (current, proposed, format) {
                            return current > proposed ? ' (−' + format(current - proposed) + ')' : '';
                        };

                        $('#optimizeCurrent').empty().append(list(result.current))
                            .append($('<div class="text-muted"></div>').text(distanceText(result.currentDistanceMeters) + ' · ' + durationText(result.currentDurationSeconds)));
                        $('#optimizeProposed').empty().append(list(result.proposed))
                            .append($('<div class="text-success"></div>').text(
                                distanceText(result.proposedDistanceMeters) + saved(result.currentDistanceMeters, result.proposedDistanceMeters, distanceText) + ' · ' +
                                durationText(result.proposedDurationSeconds) + saved(result.currentDurationSeconds, result.proposedDurationSeconds, durationText)));
                        $('#optimizeAcceptBtn').data('order', result.proposed.map(function (item) { return item.id; }));
                        $('#optimizeModal').modal('show');
                    })
                    .fail(function () {
                        alert(label('optimizeFailed', 'The stop order could not be optimized.'));
                    })
                    .always(function () {
                        $button.prop('disabled', stops().length < 3);
                    });
            }
        })(editor = routes.editor || (routes.editor = {}));
    })(routes = resgrid.routes || (resgrid.routes = {}));
})(resgrid || (resgrid = {}));
