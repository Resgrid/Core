$(document).ready(function () {

    function escapeHtml(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function formatDist(meters) {
        if (meters >= 1000) return (meters / 1000).toFixed(1) + ' km';
        return Math.round(meters) + ' m';
    }

    function formatDuration(seconds) {
        var h = Math.floor(seconds / 3600);
        var m = Math.round((seconds % 3600) / 60);
        if (h > 0) return h + ' hr ' + m + ' min';
        return m + ' min';
    }

    // Direction icon mapping by OSRM maneuver
    function stepIcon(type, modifier) {
        if (type === 'depart') return 'fa-flag-o';
        if (type === 'arrive') return 'fa-map-marker';
        if (type === 'roundabout' || type === 'rotary') return 'fa-refresh';
        if (!modifier) return 'fa-arrow-up';
        switch (modifier) {
            case 'left':        return 'fa-arrow-left';
            case 'slight left': return 'fa-arrow-left';
            case 'sharp left':  return 'fa-arrow-left';
            case 'right':       return 'fa-arrow-right';
            case 'slight right':return 'fa-arrow-right';
            case 'sharp right': return 'fa-arrow-right';
            case 'uturn':       return 'fa-undo';
            default:            return 'fa-arrow-up';
        }
    }

    // Build human-readable instruction from OSRM step
    function buildInstruction(step) {
        var type     = step.maneuver.type;
        var modifier = step.maneuver.modifier || '';
        var name     = step.name ? ' onto <strong>' + escapeHtml(step.name) + '</strong>' : '';

        if (type === 'depart')  return 'Head ' + escapeHtml(modifier) + name;
        if (type === 'arrive')  return 'Arrive at destination' + (step.name ? ': <strong>' + escapeHtml(step.name) + '</strong>' : '');
        if (type === 'continue' || type === 'new name') return 'Continue' + name;
        if (type === 'merge')   return 'Merge ' + escapeHtml(modifier) + name;
        if (type === 'fork')    return 'At fork, keep ' + escapeHtml(modifier) + name;
        if (type === 'off ramp')return 'Take the ramp ' + escapeHtml(modifier) + name;
        if (type === 'on ramp') return 'Take the on-ramp ' + escapeHtml(modifier) + name;
        if (type === 'end of road') return 'At end of road turn ' + escapeHtml(modifier) + name;
        if (type === 'roundabout' || type === 'rotary') return 'Enter roundabout' + name;
        if (type === 'exit roundabout' || type === 'exit rotary') return 'Exit roundabout' + name;
        if (type === 'turn') {
            var dir = modifier ? modifier.replace('slight ', 'slightly ').replace('sharp ', 'sharply ') : '';
            return 'Turn ' + escapeHtml(dir) + name;
        }
        return 'Continue' + name;
    }

    // ── Map setup (resgrid.maps: MapLibre or Leaflet by the department's Maps.MapLibreWeb flag) ─────────────
    resgrid.maps.create('routeMap', { center: [39.8283, -98.5795], zoom: 4, scrollZoom: false, printable: true }).then(function (map) {
        drawDirections(map);
    });

    function drawDirections(map) {
    var positions = [];

    // Add start point marker if a configured origin exists
    var shownStart = (typeof serverRoute !== 'undefined' && serverRoute && serverRoute.start) ? serverRoute.start : (typeof startPoint !== 'undefined' ? startPoint : null);
    if (shownStart && Number.isFinite(Number(shownStart.lat)) && Number.isFinite(Number(shownStart.lng))) {
        var startPosition = [Number(shownStart.lat), Number(shownStart.lng)];
        map.addMarker(startPosition, {
            html: '<div style="background:#1ab394;color:#fff;border-radius:50%;width:28px;height:28px;line-height:28px;text-align:center;font-weight:bold;font-size:14px;">S</div>',
            iconSize: [28, 28],
            iconAnchor: [14, 14],
            label: shownStart.name || 'Start'
        });
        positions.push(startPosition);
    }

    if (typeof routeStops !== 'undefined' && routeStops.length > 0) {
        routeStops.forEach(function (stop, index) {
            var lat = Number(stop.lat);
            var lng = Number(stop.lng);
            if (!Number.isFinite(lat) || !Number.isFinite(lng)) return;

            map.addMarker([lat, lng], {
                label: (index + 1) + '. ' + stop.name,
                popupHtml: '<strong>' + (index + 1) + '. ' + escapeHtml(stop.name) + '</strong>' +
                           (stop.address ? '<br/><small>' + escapeHtml(stop.address) + '</small>' : '')
            });
            positions.push([lat, lng]);
        });
    }

    if (positions.length > 0) {
        map.fitBounds(positions, { padding: 60 });
    }

    // ── Server route (routing provider on the server; plan §10.4) ──────────────
    var validStops = routeStops.filter(function (s) {
        return Number.isFinite(Number(s.lat)) && Number.isFinite(Number(s.lng));
    });

    // The server resolves the start (explicit point, chosen station or the unit's station) and the end; the legs it
    // returns run start -> stops -> end, so the waypoint list here must match.
    var serverStart = (typeof serverRoute !== 'undefined' && serverRoute) ? serverRoute.start : null;
    if (!serverStart && typeof startPoint !== 'undefined' && startPoint &&
        Number.isFinite(Number(startPoint.lat)) && Number.isFinite(Number(startPoint.lng))) {
        serverStart = startPoint;
    }
    if (serverStart) {
        validStops = [{ name: serverStart.name || 'Start', address: '', lat: serverStart.lat, lng: serverStart.lng }].concat(validStops);
    }
    if (typeof serverRoute !== 'undefined' && serverRoute && serverRoute.end) {
        validStops = validStops.concat([{ name: serverRoute.end.name || 'End', address: '', lat: serverRoute.end.lat, lng: serverRoute.end.lng }]);
    }

    if (validStops.length < 2 || typeof serverRoute === 'undefined' || !serverRoute || serverRoute.isEstimate) {
        $('#directionsError').show();
        return;
    }

    (function (data) {
            var route = {
                distance: data.distance,
                duration: data.duration,
                geometry: data.geometry,
                legs: (data.route && data.route.legs) ? data.route.legs : (data.legs || []).map(function (l) { return { distance: l.distance, duration: l.duration, steps: [] }; })
            };
            var totalDist = route.distance;
            var totalDur  = route.duration;

            // Summary (travel time; plus dwell when stops have estimated dwell)
            var summary = formatDist(totalDist) + '  \u2022  ' + formatDuration(totalDur);
            if (data.durationWithDwell && data.durationWithDwell > totalDur + 30) {
                summary += '  (' + formatDuration(data.durationWithDwell) + ')';
            }
            $('#routeSummary').text(summary);
            if (data.attribution) {
                $('#routeSummary').attr('title', data.attribution);
            }

            // Draw route polyline
            if (route.geometry) {
                var line = map.addLine('route', route.geometry, { color: routeColor || '#1c84c6', weight: 5, opacity: 0.75 });
                var lineBounds = line.bounds();
                if (lineBounds) {
                    map.fitBounds(lineBounds, { padding: 50 });
                }
            }

            // Build turn-by-turn HTML
            var legs = route.legs;   // one leg per segment between consecutive waypoints
            var html = '';
            var stepNum = 0;

            legs.forEach(function (leg, legIndex) {
                // Stop header (destination of this leg = next stop)
                var destStop = validStops[legIndex + 1];
                if (destStop) {
                    html += '<div class="stop-header">' +
                            '<span class="stop-num">' + (legIndex + 2) + '</span>' +
                            escapeHtml(destStop.name) +
                            (destStop.address ? ' <small style="font-weight:normal;">&mdash; ' + escapeHtml(destStop.address) + '</small>' : '') +
                            ' <span style="float:right;font-weight:normal;">' + formatDist(leg.distance) + ' &bull; ' + formatDuration(leg.duration) + '</span>' +
                            '</div>';
                } else if (legIndex === 0) {
                    var startStop = validStops[0];
                    html = '<div class="stop-header">' +
                           '<span class="stop-num">1</span>' +
                           escapeHtml(startStop.name) +
                           (startStop.address ? ' <small style="font-weight:normal;">&mdash; ' + escapeHtml(startStop.address) + '</small>' : '') +
                           '</div>' + html;
                }

                if (leg.steps && leg.steps.length > 0) {
                    leg.steps.forEach(function (step) {
                        if (step.distance < 5 && step.maneuver.type !== 'depart' && step.maneuver.type !== 'arrive') return;
                        stepNum++;
                        var icon = stepIcon(step.maneuver.type, step.maneuver.modifier);
                        html += '<div class="direction-step">' +
                                '<span class="step-dist">' + formatDist(step.distance) + '</span>' +
                                '<span class="step-num">' + stepNum + '</span>' +
                                '<i class="fa ' + icon + '" style="width:18px;text-align:center;margin-right:6px;"></i>' +
                                buildInstruction(step) +
                                '</div>';
                    });
                }
            });

            // Prepend start stop header if it wasn't added yet
            if (validStops.length > 0 && html.indexOf('stop-num">1<') === -1) {
                var startStop = validStops[0];
                html = '<div class="stop-header">' +
                       '<span class="stop-num">1</span>' +
                       escapeHtml(startStop.name) +
                       (startStop.address ? ' <small style="font-weight:normal;">&mdash; ' + escapeHtml(startStop.address) + '</small>' : '') +
                       '</div>' + html;
            }

            if (!html) {
                html = '<p class="text-muted">No detailed steps available for this route.</p>';
            }

            $('#directionsContainer').html(html);
    })(serverRoute);
    }
});
