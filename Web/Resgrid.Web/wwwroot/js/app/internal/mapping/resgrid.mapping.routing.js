// Live Routing and Station Routing: a route between two points from the install's routing provider (server
// Routing/GetRoute through the web BFF; maplibre plan section 6.3), drawn with resgrid.maps. No page calls a routing
// server directly any more.
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var routing;
        (function (routing) {
            function stepText(step) {
                var maneuver = step.maneuver || {};
                if (maneuver.instruction) {
                    return resgrid.escapeHtml(maneuver.instruction);
                }

                return resgrid.escapeHtml(maneuver.type || '') + (step.name ? ' onto ' + resgrid.escapeHtml(step.name) : '');
            }

            /** Draws the route from start to end ([lat, lng] each) and fills the distance and steps panels. */
            function show(start, end, labels) {
                resgrid.maps.create('dvMap', { center: start, zoom: 9, traffic: 'off' }).then(function (map) {
                    map.addMarker(start);
                    map.addMarker(end);
                    map.fitBounds([start, end], { padding: 40 });

                    return resgrid.maps.route([start, end], { steps: true }).then(function (route) {
                        if (!route) {
                            return;
                        }

                        var line = map.addLine('route', JSON.parse(route.Geometry), { color: '#0066cc', weight: 4, opacity: 0.8 });
                        var bounds = line.bounds();
                        if (bounds) {
                            map.fitBounds(bounds, { padding: 20 });
                        }

                        var dvDistance = document.getElementById('dvDistance');
                        dvDistance.textContent = labels.distance + ' ' + (route.DistanceMeters / 1000).toFixed(1) + ' km';
                        dvDistance.appendChild(document.createElement('br'));
                        dvDistance.appendChild(document.createTextNode(labels.duration + ' ' + Math.round(route.DurationSeconds / 60) + ' min'));

                        var legs = route.Route && route.Route.legs ? route.Route.legs : [];
                        if (legs.length > 0 && legs[0].steps) {
                            var html = '<ol style="padding-left:20px;">';
                            legs[0].steps.forEach(function (step) {
                                if (step.maneuver && step.maneuver.type !== 'arrive') {
                                    html += '<li>' + stepText(step) + ' (' + (step.distance / 1000).toFixed(1) + ' km)</li>';
                                }
                            });
                            html += '</ol>';
                            document.getElementById('dvPanel').innerHTML = html;
                        }

                        if (route.Attribution) {
                            var credit = document.createElement('p');
                            credit.className = 'text-muted small';
                            credit.textContent = route.Attribution;
                            document.getElementById('dvPanel').appendChild(credit);
                        }
                    });
                }).catch(function (err) {
                    console.error('Routing error:', err);
                });
            }
            routing.show = show;
        })(routing = mapping.routing || (mapping.routing = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
