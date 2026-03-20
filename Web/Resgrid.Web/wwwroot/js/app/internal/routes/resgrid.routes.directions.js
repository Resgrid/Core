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

    // ── Map setup ──────────────────────────────────────────────────────────────
    var tiles = L.tileLayer(osmTileUrl, { maxZoom: 19, attribution: osmTileAttribution });
    var map = L.map('routeMap', { scrollWheelZoom: false })
               .setView([39.8283, -98.5795], 4)
               .addLayer(tiles);

    var markers = [];
    var group   = [];

    // Add start point marker if a configured origin exists
    if (typeof startPoint !== 'undefined' && startPoint &&
        Number.isFinite(Number(startPoint.lat)) && Number.isFinite(Number(startPoint.lng))) {
        var startIcon = L.divIcon({ className: '', html: '<div style="background:#1ab394;color:#fff;border-radius:50%;width:28px;height:28px;line-height:28px;text-align:center;font-weight:bold;font-size:14px;">S</div>', iconSize: [28, 28], iconAnchor: [14, 14] });
        var sm = L.marker([Number(startPoint.lat), Number(startPoint.lng)], { icon: startIcon }).addTo(map);
        sm.bindTooltip('Start', { permanent: true, direction: 'right' });
        group.push(sm);
    }

    if (typeof routeStops !== 'undefined' && routeStops.length > 0) {
        routeStops.forEach(function (stop, index) {
            var lat = Number(stop.lat);
            var lng = Number(stop.lng);
            if (!Number.isFinite(lat) || !Number.isFinite(lng)) return;

            var m = L.marker([lat, lng]).addTo(map);
            m.bindTooltip((index + 1) + '. ' + escapeHtml(stop.name), { permanent: true, direction: 'right' });
            m.bindPopup('<strong>' + (index + 1) + '. ' + escapeHtml(stop.name) + '</strong>' +
                        (stop.address ? '<br/><small>' + escapeHtml(stop.address) + '</small>' : ''));
            markers.push(m);
            group.push(m);
        });

        if (group.length > 0) {
            map.fitBounds(L.featureGroup(group).getBounds(), { padding: [60, 60] });
        }
    }

    // ── OSRM routing ──────────────────────────────────────────────────────────
    var validStops = routeStops.filter(function (s) {
        return Number.isFinite(Number(s.lat)) && Number.isFinite(Number(s.lng));
    });

    // Prepend the plan's configured start point (or last checked-in location)
    // when one is available so routing begins from there rather than stop #1.
    if (typeof startPoint !== 'undefined' && startPoint &&
        Number.isFinite(Number(startPoint.lat)) && Number.isFinite(Number(startPoint.lng))) {
        validStops = [{ name: startPoint.name || 'Start', address: '', lat: startPoint.lat, lng: startPoint.lng }].concat(validStops);
    }

    if (validStops.length < 2) {
        $('#directionsError').show();
        return;
    }

    // Map route profile names to OSRM profile names.
    // driving-traffic falls back to driving (OSRM has no live traffic layer).
    var osrmProfile = 'driving';
    if (routeProfile === 'walking' || routeProfile === 'foot') osrmProfile = 'foot';
    else if (routeProfile === 'cycling' || routeProfile === 'bike') osrmProfile = 'bike';
    // driving and driving-traffic both use the OSRM 'driving' profile

    // OSRM uses lon,lat order
    var coords = validStops.map(function (s) { return Number(s.lng) + ',' + Number(s.lat); }).join(';');
    var osrmUrl = 'https://router.project-osrm.org/route/v1/' + osrmProfile + '/' + coords +
                  '?steps=true&geometries=geojson&overview=full&annotations=false';

    $('#directionsLoading').show();

    fetch(osrmUrl)
        .then(function (r) {
            if (!r.ok) throw new Error('OSRM error: ' + r.status);
            return r.json();
        })
        .then(function (data) {
            $('#directionsLoading').hide();

            if (!data || data.code !== 'Ok' || !data.routes || !data.routes[0]) {
                $('#directionsError').show();
                return;
            }

            var route = data.routes[0];
            var totalDist = route.distance;
            var totalDur  = route.duration;

            // Summary
            $('#routeSummary').text(formatDist(totalDist) + '  \u2022  ' + formatDuration(totalDur));

            // Draw route polyline
            if (route.geometry) {
                L.geoJSON(route.geometry, {
                    style: { color: routeColor || '#1c84c6', weight: 5, opacity: 0.75 }
                }).addTo(map);
                map.fitBounds(L.geoJSON(route.geometry).getBounds(), { padding: [50, 50] });
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
        })
        .catch(function (err) {
            $('#directionsLoading').hide();
            $('#directionsError').show();
            console.error('Routing error:', err);
        });
});
