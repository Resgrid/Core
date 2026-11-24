// Call locations on the call forms (M0322): New Call, Update Call and Add Archived Call.
//  - Location from a POI: picking one of the department's points of interest fills the address and the call pin.
//  - Location from a plus code: a found code places the call pin (and a blank address); the code itself is never stored.
//  - A free destination (address, coordinates, what3words, plus code) for a destination that is not a POI. One destination
//    only: a destination POI clears and disables the free fields, and a free destination disables the POI.
//  - A pre-defined route: picking one fills a blank call location (route start) and a blank destination (route end) and draws
//    the route line and its numbered stops on the call map.
// Settings come from window.callLocationsConfig (_CallLocationsScript). The page script hands its map over with
// resgrid.dispatch.calllocations.mapReady(map) and places the call pin with its own setMarkerLocation.
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var calllocations;
        (function (calllocations) {
            var map = null;
            var destinationMarker = null;
            var routeLayer = null;
            var routeMarkers = [];
            var routeData = null;
            var routeRequest = 0;
            var routeFitPending = false;
            var destinationColor = '#8e44ad';
            // Destination lookups (address, plus code, what3words): each one takes the next number, and a result is applied only
            // while its number is still the latest and its input still reads what was looked up. Editing, clearing or replacing the
            // destination moves the number on, so a late answer never overwrites what the dispatcher did since.
            var destinationLookup = 0;

            function settings() {
                return window.callLocationsConfig || {};
            }

            function text(key, fallback) {
                var values = settings().text || {};
                return values[key] || fallback;
            }

            function escapeHtml(value) {
                return String(value === null || value === undefined ? '' : value)
                    .replace(/&/g, '&amp;')
                    .replace(/</g, '&lt;')
                    .replace(/>/g, '&gt;')
                    .replace(/"/g, '&quot;')
                    .replace(/'/g, '&#39;');
            }

            /** The page script (newcall, editcall, addArchivedCall), which owns the call pin. */
            function pageScript() {
                var name = settings().page || 'newcall';
                return resgrid.dispatch ? resgrid.dispatch[name] : null;
            }

            function isPoint(lat, lng) {
                return isFinite(lat) && isFinite(lng) && lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180 && !(lat === 0 && lng === 0);
            }

            function formatCoordinate(value) {
                return String(Math.round(Number(value) * 10000000) / 10000000);
            }

            /** A typed coordinate pair, read the way the server reads it (a decimal point, never a comma). */
            function readPoint(latitudeSelector, longitudeSelector) {
                var latText = $.trim($(latitudeSelector).val() || '');
                var lngText = $.trim($(longitudeSelector).val() || '');
                if (latText === '' || lngText === '') {
                    return null;
                }

                var lat = Number(latText);
                var lng = Number(lngText);
                return isPoint(lat, lng) ? [lat, lng] : null;
            }

            function optionPoint(option, prefix) {
                var lat = Number(option.attr('data-' + prefix + '-lat'));
                var lng = Number(option.attr('data-' + prefix + '-lng'));
                if (!option.attr('data-' + prefix + '-lat') || !isPoint(lat, lng)) {
                    return null;
                }

                return { lat: lat, lng: lng, name: option.attr('data-' + prefix + '-name') || '' };
            }

            // ── Call location ───────────────────────────────────────────────────────────────────────────────────────────

            function hasCallLocation() {
                return $.trim($('#Latitude').val() || '') !== '' || $.trim($('#Call_Address').val() || '') !== '';
            }

            /** Places the call where it starts: the pin (and the address, when one is given and the form has it). */
            function setCallLocation(lat, lng, address) {
                if (address && $('#Call_Address').length) {
                    $('#Call_Address').val(address);
                    // The page's map click reverse geocodes into the address unless the dispatcher supplied one; a picked
                    // address counts as supplied.
                    window.userSuppliedAddress = true;
                }

                $('#Latitude').val(formatCoordinate(lat));
                $('#Longitude').val(formatCoordinate(lng));

                var page = pageScript();
                if (page && typeof page.setMarkerLocation === 'function') {
                    page.setMarkerLocation(lat, lng);
                }

                if (map) {
                    map.setView([lat, lng], 16);
                }
            }

            // The call location's plus code (_CallLocationPlusCode): like the destination's, it only finds the point and is never
            // stored. A result applies only while its number is still the latest and the input still reads the code looked up.
            var callPlusCodeLookup = 0;

            function callPlusCodeInput() {
                return $('#CallPlusCode');
            }

            /**
             * The call location was set another way (a POI, a map click, an address or What3Words search, a typed pin, a dragged
             * marker, a room): a call plus code still being looked up no longer applies, and one still typed in is dropped so the
             * save does not look it up again and move the call back.
             */
            function callLocationChanged() {
                callPlusCodeLookup++;
                callPlusCodeInput().val('');
            }
            calllocations.callLocationChanged = callLocationChanged;

            /**
             * Looks the typed call plus code up and, when found, places the call there (the pin, and the address through the
             * page's reverse geocode unless the dispatcher typed one) and empties the code. onFound runs after that, once the
             * page's address lookup has answered (or failed), so a save waiting on the code is not sent without that address.
             */
            function lookupCallPlusCode(code, onFound) {
                var request = ++callPlusCodeLookup;
                var isCurrent = function () {
                    return request === callPlusCodeLookup && $.trim(callPlusCodeInput().val() || '') === code;
                };

                fetch('/api/web-bff/api/v4/Geocoding/PlusCodeLookup?code=' + encodeURIComponent(code))
                    .then(function (r) {
                        if (!r.ok) { throw new Error('PlusCodeLookup failed with HTTP status ' + r.status); }
                        return r.json();
                    })
                    .then(function (result) {
                        if (!isCurrent()) {
                            return;
                        }

                        if (result && result.Data && result.Data.Latitude != null && result.Data.Longitude != null &&
                            isPoint(Number(result.Data.Latitude), Number(result.Data.Longitude))) {
                            var lat = Number(result.Data.Latitude);
                            var lng = Number(result.Data.Longitude);

                            setCallLocation(lat, lng);
                            callPlusCodeInput().val('');

                            var page = pageScript();
                            var addressLookup = page && typeof page.geocodeCoordinates === 'function' ? page.geocodeCoordinates(lat, lng) : null;

                            if (typeof onFound === 'function') {
                                // A location set another way while the address was being looked up wins: the save waits
                                // for the dispatcher again instead of going on with a place they did not save.
                                var resume = function () {
                                    if (request === callPlusCodeLookup) {
                                        onFound();
                                    }
                                };
                                Promise.resolve(addressLookup).then(resume, resume);
                            }
                        } else {
                            alert(text('plusCodeNotFound', 'No location was found for that plus code.'));
                        }
                    })
                    .catch(function (err) {
                        console.error('Call plus code lookup error:', err);
                        if (isCurrent()) {
                            alert(text('plusCodeNotFound', 'No location was found for that plus code.'));
                        }
                    });
            }

            function findCallPlusCode(evt) {
                evt.preventDefault();

                var code = $.trim(callPlusCodeInput().val() || '');
                if (code.length < 1) {
                    return;
                }

                lookupCallPlusCode(code);
            }

            function onLocationPoiChanged() {
                var select = $(this);
                if (!select.val()) {
                    return;
                }

                // A picked POI is the call's location now: a plus code lookup still on its way no longer applies, and a code still
                // typed in goes too (the save would otherwise look it up and move the call off the POI).
                callLocationChanged();

                var option = select.find('option:selected');
                var lat = Number(option.attr('data-lat'));
                var lng = Number(option.attr('data-lng'));
                var address = option.attr('data-address') || '';

                if (option.attr('data-lat') && isPoint(lat, lng)) {
                    setCallLocation(lat, lng, address);
                } else if (address && $('#Call_Address').length) {
                    // A POI with an address but no point: the pin that was there belongs to the old location. Its coordinates go,
                    // and the page looks the new address up the way its own Find button does (the save locates an address-only
                    // call too), so the pin and the address never name two different places.
                    $('#Call_Address').val(address);
                    window.userSuppliedAddress = true;
                    $('#Latitude').val('');
                    $('#Longitude').val('');
                    if ($('#searchButton').length) {
                        $('#searchButton').trigger('click');
                    }
                }
            }

            // ── Destination ─────────────────────────────────────────────────────────────────────────────────────────────

            function destinationPoiSelect() {
                return $('#Call_DestinationPoiId');
            }

            function freeInputs() {
                return $('.call-destination-free');
            }

            function hasDestinationPoi() {
                return !!destinationPoiSelect().val();
            }

            function hasFreeDestination() {
                var any = false;
                freeInputs().each(function () {
                    if ($.trim($(this).val() || '') !== '') {
                        any = true;
                    }
                });
                return any;
            }

            function hasDestination() {
                return hasDestinationPoi() || hasFreeDestination();
            }

            function poiDestinationPoint() {
                var id = destinationPoiSelect().val();
                var points = settings().destinationPoiPoints || {};
                var point = id ? points[String(id)] : null;
                if (!point || point.length !== 2 || !isPoint(Number(point[0]), Number(point[1]))) {
                    return null;
                }

                return [Number(point[0]), Number(point[1])];
            }

            function freeDestinationPoint() {
                return readPoint('#DestinationLatitude', '#DestinationLongitude');
            }

            /** Starts a destination lookup; its result applies only while isCurrentLookup says so. */
            function beginDestinationLookup() {
                destinationLookup++;
                return destinationLookup;
            }

            /** Pending lookups no longer apply: the dispatcher changed the destination another way. */
            function invalidateDestinationLookups() {
                destinationLookup++;
            }

            function isCurrentLookup(request, selector, value) {
                return request === destinationLookup && $.trim($(selector).val() || '') === value;
            }

            /** One destination: a POI disables the free fields, a free destination disables the POI select. */
            function updateDestinationInputs() {
                var poi = hasDestinationPoi();
                var free = !poi && hasFreeDestination();

                freeInputs().prop('disabled', poi);
                $('#destinationSearchButton, #destinationPlusCodeButton, #destinationW3wButton').toggleClass('disabled', poi);
                destinationPoiSelect().prop('disabled', free);
            }

            function destinationMarkerHtml() {
                return '<svg xmlns="http://www.w3.org/2000/svg" width="28" height="38" viewBox="0 0 28 38" style="display:block;">' +
                    '<path d="M14 1C6.8 1 1 6.8 1 14c0 9.8 13 23 13 23s13-13.2 13-23C27 6.8 21.2 1 14 1z" fill="' + destinationColor + '" stroke="#fff" stroke-width="2"/>' +
                    '<path d="M10 8v13M10 8h8l-2 3 2 3h-8" fill="none" stroke="#fff" stroke-width="2" stroke-linejoin="round"/>' +
                    '</svg>';
            }

            /** The destination marker: the POI's point or the free point, in its own colour so it never reads as the call pin. */
            function drawDestinationMarker() {
                if (destinationMarker) {
                    destinationMarker.remove();
                    destinationMarker = null;
                }

                if (!map) {
                    return;
                }

                var fromPoi = hasDestinationPoi();
                var point = fromPoi ? poiDestinationPoint() : freeDestinationPoint();
                if (!point) {
                    return;
                }

                var title = text('destination', 'Destination');
                if (fromPoi) {
                    title = title + ': ' + (destinationPoiSelect().find('option:selected').text() || '');
                } else if ($.trim($('#Call_DestinationAddress').val() || '') !== '') {
                    title = title + ': ' + $.trim($('#Call_DestinationAddress').val());
                }

                destinationMarker = map.addMarker(point, {
                    html: destinationMarkerHtml(),
                    iconSize: [28, 38],
                    iconAnchor: [14, 38],
                    title: title,
                    zIndex: 500,
                    draggable: !fromPoi
                });

                if (!fromPoi) {
                    destinationMarker.onDragEnd(function (moved) {
                        invalidateDestinationLookups();
                        $('#DestinationLatitude').val(formatCoordinate(moved[0]));
                        $('#DestinationLongitude').val(formatCoordinate(moved[1]));
                        updateDestinationInputs();
                    });
                }
            }

            function syncDestination() {
                updateDestinationInputs();
                drawDestinationMarker();
            }

            /** A destination point found by address, plus code or what3words: the free destination takes over from any POI. */
            function setDestinationPoint(lat, lng, address) {
                if (address && $.trim($('#Call_DestinationAddress').val() || '') === '') {
                    $('#Call_DestinationAddress').val(address);
                }

                $('#DestinationLatitude').val(formatCoordinate(lat));
                $('#DestinationLongitude').val(formatCoordinate(lng));

                if (hasDestinationPoi()) {
                    destinationPoiSelect().val('').trigger('change');
                }

                syncDestination();

                if (map) {
                    map.setView([lat, lng], Math.max(map.getZoom(), 14));
                }
            }

            function geocodeDestination(evt) {
                evt.preventDefault();
                if ($(this).hasClass('disabled')) {
                    return;
                }

                var where = $.trim($('#Call_DestinationAddress').val() || '');
                if (where.length < 1) {
                    return;
                }

                var request = beginDestinationLookup();
                fetch('/api/web-bff/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where))
                    .then(function (r) {
                        if (!r.ok) { throw new Error('ForwardGeocode failed with HTTP status ' + r.status); }
                        return r.json();
                    })
                    .then(function (result) {
                        if (!isCurrentLookup(request, '#Call_DestinationAddress', where)) {
                            return;
                        }

                        if (result && result.Data && result.Data.Latitude != null && result.Data.Longitude != null &&
                            isPoint(Number(result.Data.Latitude), Number(result.Data.Longitude))) {
                            setDestinationPoint(Number(result.Data.Latitude), Number(result.Data.Longitude));
                        } else {
                            alert(text('notFound', 'No location was found for that destination.'));
                        }
                    })
                    .catch(function (err) { console.error('Destination geocode error:', err); });
            }

            /**
             * Looks the typed plus code up and, when found, makes its point the destination and empties the code (a plus code only
             * finds the point; it is never stored). onFound runs after that.
             */
            function lookupPlusCode(code, onFound) {
                var request = beginDestinationLookup();
                fetch('/api/web-bff/api/v4/Geocoding/PlusCodeLookup?code=' + encodeURIComponent(code))
                    .then(function (r) {
                        if (!r.ok) { throw new Error('PlusCodeLookup failed with HTTP status ' + r.status); }
                        return r.json();
                    })
                    .then(function (result) {
                        if (!isCurrentLookup(request, '#DestinationPlusCode', code)) {
                            return;
                        }

                        if (result && result.Data && result.Data.Latitude != null && result.Data.Longitude != null &&
                            isPoint(Number(result.Data.Latitude), Number(result.Data.Longitude))) {
                            setDestinationPoint(Number(result.Data.Latitude), Number(result.Data.Longitude));
                            $('#DestinationPlusCode').val('');
                            updateDestinationInputs();
                            if (typeof onFound === 'function') {
                                onFound();
                            }
                        } else {
                            alert(text('plusCodeNotFound', 'No location was found for that plus code.'));
                        }
                    })
                    .catch(function (err) {
                        console.error('Plus code lookup error:', err);
                        if (isCurrentLookup(request, '#DestinationPlusCode', code)) {
                            alert(text('plusCodeNotFound', 'No location was found for that plus code.'));
                        }
                    });
            }

            function findPlusCode(evt) {
                evt.preventDefault();
                if ($(this).hasClass('disabled')) {
                    return;
                }

                var code = $.trim($('#DestinationPlusCode').val() || '');
                if (code.length < 1) {
                    return;
                }

                lookupPlusCode(code);
            }

            /**
             * A plus code is only a way to find the call or destination point, never saved: a form sent with one still typed in
             * would silently save without that point. The save waits for the lookup instead (the call's code first, then the
             * destination's on the resubmit); found, the point takes its place and the form goes on (with the button that was
             * pressed); not found, the dispatcher is told and the form stays.
             */
            function onFormSubmit(evt) {
                var callInput = callPlusCodeInput();
                var callCode = $.trim(callInput.val() || '');
                var destinationInput = $('#DestinationPlusCode');
                var destinationCode = $.trim(destinationInput.val() || '');

                var lookup = null;
                if (callCode !== '' && !callInput.prop('disabled')) {
                    lookup = function (resume) { lookupCallPlusCode(callCode, resume); };
                } else if (destinationCode !== '' && !destinationInput.prop('disabled')) {
                    lookup = function (resume) { lookupPlusCode(destinationCode, resume); };
                }

                if (!lookup) {
                    return true;
                }

                evt.preventDefault();
                var form = this;
                var submitter = evt.originalEvent && evt.originalEvent.submitter ? evt.originalEvent.submitter : null;

                lookup(function () {
                    if (typeof form.requestSubmit === 'function') {
                        if (submitter) {
                            form.requestSubmit(submitter);
                        } else {
                            form.requestSubmit();
                        }
                    } else {
                        $(form).trigger('submit');
                    }
                });
                return false;
            }

            function findW3w(evt) {
                evt.preventDefault();
                if ($(this).hasClass('disabled')) {
                    return;
                }

                var words = $.trim($('#Call_DestinationW3W').val() || '');
                if (words.length < 1) {
                    return;
                }

                var request = beginDestinationLookup();
                $.ajax({
                    url: settings().w3wUrl || (resgrid.absoluteBaseUrl + '/User/Dispatch/GetCoordinatesFromW3W'),
                    data: { words: words },
                    type: 'GET'
                }).done(function (data) {
                    if (!isCurrentLookup(request, '#Call_DestinationW3W', words)) {
                        return;
                    }

                    if (data && data.Latitude != null && data.Longitude != null && isPoint(Number(data.Latitude), Number(data.Longitude))) {
                        setDestinationPoint(Number(data.Latitude), Number(data.Longitude));
                    } else {
                        alert(text('w3wNotFound', 'What3Words was unable to find a location for those words.'));
                    }
                });
            }

            function clearFreeDestination(evt) {
                evt.preventDefault();
                invalidateDestinationLookups();
                freeInputs().val('');
                syncDestination();
            }

            // ── Route ───────────────────────────────────────────────────────────────────────────────────────────────────

            function routeSelect() {
                return $('#Call_RoutePlanId');
            }

            function clearRoute() {
                if (routeLayer) {
                    routeLayer.remove();
                    routeLayer = null;
                }

                routeMarkers.forEach(function (marker) { marker.remove(); });
                routeMarkers = [];
            }

            function routeColor(value) {
                return /^#[0-9a-fA-F]{3,8}$/.test(value || '') ? value : '#1c84c6';
            }

            function stopMarkerHtml(number, color) {
                return '<span style="display:block;width:22px;height:22px;line-height:18px;border-radius:50%;background:' + color +
                    ';color:#fff;font-size:11px;font-weight:bold;text-align:center;border:2px solid #fff;box-shadow:0 0 2px rgba(0,0,0,.6);">' +
                    escapeHtml(number) + '</span>';
            }

            function endpointMarkerHtml(label, cssClass) {
                return '<span class="label ' + cssClass + '" style="font-size:11px;white-space:nowrap;box-shadow:0 0 2px rgba(0,0,0,.6);">' + escapeHtml(label) + '</span>';
            }

            /** The route line and its numbered stops; fits the map to the route with the call and the destination when asked. */
            function drawRoute(fit) {
                clearRoute();

                if (!map || !routeData) {
                    return;
                }

                var color = routeColor(routeData.color);
                var points = [];
                var path = [];
                var stops = routeData.stops || [];

                if (routeData.start) {
                    path.push([routeData.start.lat, routeData.start.lng]);
                }
                stops.forEach(function (stop) {
                    if (isPoint(Number(stop.lat), Number(stop.lng))) {
                        path.push([Number(stop.lat), Number(stop.lng)]);
                    }
                });
                if (routeData.end) {
                    path.push([routeData.end.lat, routeData.end.lng]);
                }

                var geometry = null;
                if (routeData.geometry) {
                    try {
                        geometry = typeof routeData.geometry === 'string' ? JSON.parse(routeData.geometry) : routeData.geometry;
                    } catch (e) {
                        geometry = null;
                    }
                }

                try {
                    if (geometry) {
                        routeLayer = map.addLine('call-route-plan', geometry, { color: color, weight: 5, opacity: 0.75, dashed: !!routeData.stale });
                    } else if (path.length > 1) {
                        // No generated route yet: the order of travel as a dashed line.
                        routeLayer = map.addLine('call-route-plan', path, { color: color, weight: 3, opacity: 0.6, dashed: true });
                    }
                } catch (e) {
                    routeLayer = null;
                }

                if (routeLayer) {
                    var bounds = routeLayer.bounds();
                    if (bounds) {
                        points.push(bounds[0], bounds[1]);
                    }
                }

                stops.forEach(function (stop) {
                    var lat = Number(stop.lat);
                    var lng = Number(stop.lng);
                    if (!isPoint(lat, lng)) {
                        return;
                    }

                    var label = stop.order + '. ' + (stop.name || '');
                    routeMarkers.push(map.addMarker([lat, lng], {
                        html: stopMarkerHtml(stop.order, color),
                        iconSize: [22, 22],
                        iconAnchor: [11, 11],
                        title: label,
                        popupHtml: '<strong>' + escapeHtml(label) + '</strong>'
                    }));
                    points.push([lat, lng]);
                });

                var start = routeData.start;
                var end = routeData.end;
                if (start && !routeData.startIsStop) {
                    routeMarkers.push(map.addMarker([start.lat, start.lng], {
                        html: endpointMarkerHtml(text('routeStart', 'Route start'), 'label-primary'),
                        title: start.name || text('routeStart', 'Route start')
                    }));
                    points.push([start.lat, start.lng]);
                }
                if (end && !routeData.endIsStop && !(start && start.lat === end.lat && start.lng === end.lng)) {
                    routeMarkers.push(map.addMarker([end.lat, end.lng], {
                        html: endpointMarkerHtml(text('routeEnd', 'Route end'), 'label-default'),
                        title: end.name || text('routeEnd', 'Route end')
                    }));
                    points.push([end.lat, end.lng]);
                }

                if (fit && points.length > 0) {
                    var callPoint = readPoint('#Latitude', '#Longitude');
                    if (callPoint) {
                        points.push(callPoint);
                    }
                    var destination = hasDestinationPoi() ? poiDestinationPoint() : freeDestinationPoint();
                    if (destination) {
                        points.push(destination);
                    }

                    map.fitBounds(points, { padding: 40, maxZoom: 16 });
                }
            }

            function loadRoute(routePlanId, fit) {
                var request = ++routeRequest;

                $.ajax({
                    url: settings().previewUrl || (resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallRoutePreview'),
                    data: { routePlanId: routePlanId },
                    type: 'GET',
                    dataType: 'json'
                }).done(function (data) {
                    if (request !== routeRequest) {
                        return;
                    }

                    routeData = data && data.success ? data : null;
                    // Drawn now, or once the page hands over its map.
                    routeFitPending = !map && fit;
                    drawRoute(fit);
                }).fail(function () {
                    if (request === routeRequest) {
                        routeData = null;
                        clearRoute();
                    }
                });
            }

            function updatePreviewLink(routePlanId, option) {
                var link = $('#callRoutePreviewLink');
                var baseUrl = settings().routeViewUrl;
                if (!link.length) {
                    return;
                }

                if (routePlanId && baseUrl && option.attr('data-available') !== 'false') {
                    link.attr('href', baseUrl.replace(/\/$/, '') + '/' + encodeURIComponent(routePlanId)).show();
                } else {
                    link.attr('href', '#').hide();
                }
            }

            /**
             * A picked route: with fill, a blank call location takes the route start and a blank destination the route end (as
             * the server does on save); the route is drawn on the call map.
             */
            function onRouteChanged(fill) {
                var select = routeSelect();
                var routePlanId = select.val();
                var option = select.find('option:selected');

                updatePreviewLink(routePlanId, option);

                if (!routePlanId) {
                    routeRequest++;
                    routeData = null;
                    clearRoute();
                    return;
                }

                if (fill) {
                    var start = optionPoint(option, 'start');
                    var end = optionPoint(option, 'end');

                    if (start && !hasCallLocation()) {
                        setCallLocation(start.lat, start.lng, start.name);
                    }

                    if (end && !hasDestination() && $('#DestinationLatitude').length) {
                        if (end.name) {
                            $('#Call_DestinationAddress').val(end.name);
                        }
                        $('#DestinationLatitude').val(formatCoordinate(end.lat));
                        $('#DestinationLongitude').val(formatCoordinate(end.lng));
                        syncDestination();
                    }
                }

                if (option.attr('data-available') === 'false') {
                    // A stored route that can no longer be followed: kept on the call, nothing to draw. A preview still on its way
                    // for the route picked before is dropped too.
                    routeRequest++;
                    routeData = null;
                    clearRoute();
                    return;
                }

                loadRoute(routePlanId, true);
            }

            /** The page created its call map: draw the destination and the route on it. */
            function mapReady(createdMap) {
                map = createdMap;
                drawDestinationMarker();

                if (routeData) {
                    drawRoute(routeFitPending);
                    routeFitPending = false;
                }
            }
            calllocations.mapReady = mapReady;

            $(document).ready(function () {
                if ($.fn.select2) {
                    $('.location-poi-search').select2({
                        placeholder: text('searchLocationPois', 'Location from a point of interest'),
                        allowClear: true,
                        minimumResultsForSearch: 0,
                        width: '100%'
                    });
                    $('.route-plan-search').select2({
                        placeholder: text('searchRoutes', 'No route'),
                        allowClear: true,
                        minimumResultsForSearch: 0,
                        width: '100%'
                    });
                }

                $('#Call_LocationPoiId').on('change', onLocationPoiChanged);

                destinationPoiSelect().on('change', function () {
                    if ($(this).val()) {
                        // A destination POI replaces any free destination, and any lookup still on its way.
                        invalidateDestinationLookups();
                        freeInputs().val('');
                    }
                    syncDestination();
                });
                freeInputs().on('input change', updateDestinationInputs);
                // An edit to any free-destination field (address, plus code, coordinates, what3words) wins over a lookup still on its way;
                // a lookup's own .val() fires no input event, so it never cancels itself.
                freeInputs().on('input', invalidateDestinationLookups);
                $('#DestinationLatitude, #DestinationLongitude').on('change', syncDestination);
                // One handler per form (closest() returns each form once) for whichever plus code inputs the policy shows.
                $('#CallPlusCode, #DestinationPlusCode').closest('form').on('submit', onFormSubmit);
                callPlusCodeInput().on('keypress', function (event) {
                    if (event.keyCode === 13) {
                        $('#callPlusCodeButton').click();
                        return false;
                    }
                });
                $('#callPlusCodeButton').on('click', findCallPlusCode);
                $('#Call_DestinationAddress').on('keypress', function (event) {
                    if (event.keyCode === 13) {
                        $('#destinationSearchButton').click();
                        return false;
                    }
                });
                $('#DestinationPlusCode').on('keypress', function (event) {
                    if (event.keyCode === 13) {
                        $('#destinationPlusCodeButton').click();
                        return false;
                    }
                });
                $('#Call_DestinationW3W').on('keypress', function (event) {
                    if (event.keyCode === 13) {
                        $('#destinationW3wButton').click();
                        return false;
                    }
                });
                $('#destinationSearchButton').on('click', geocodeDestination);
                $('#destinationPlusCodeButton').on('click', findPlusCode);
                $('#destinationW3wButton').on('click', findW3w);
                $('#destinationClearButton').on('click', clearFreeDestination);

                routeSelect().on('change', function () { onRouteChanged(true); });

                // A re-rendered or edited form: the stored destination and route, without filling anything.
                syncDestination();
                if (routeSelect().val()) {
                    onRouteChanged(false);
                }
            });
        })(calllocations = dispatch.calllocations || (dispatch.calllocations = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
