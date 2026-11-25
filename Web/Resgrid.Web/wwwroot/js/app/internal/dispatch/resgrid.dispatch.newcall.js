
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var newcall;
        (function (newcall) {
            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }
            function formatText(template) {
                if (resgrid.dispatch && typeof resgrid.dispatch.formatText === 'function') {
                    return resgrid.dispatch.formatText.apply(null, arguments);
                }
                var args = Array.prototype.slice.call(arguments, 1);
                return (template || '').replace(/\{(\d+)\}/g, function (match, index) {
                    return typeof args[index] !== 'undefined' ? args[index] : match;
                });
            }
            $(document).ready(function () {
                callMarker = null;
                map = null;
                // The board fills the #personnelGrid list and builds the unit tabs; a run card recommendation that came
                // back before the units did is ticked once they are in.
                resgrid.dispatch.nearestboard.init({
                    getMap: function () { return map; },
                    getPersonnelGrid: function () { return personnelGrid; },
                    onUnitsLoaded: applyRecommendationSelections
                });
                // A typed address always wins over a reverse-geocoded one (a re-rendered form after a
                // validation failure already carries the dispatcher's address).
                userSuppliedAddress = jQuery.trim($("#Call_Address").val() || '') !== '';

                let quillNote2 = new Quill('#nature-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $('#PrimaryContact').select2();
                $('#AdditionalContacts').select2();

                $("#Call_Address").bind("keypress", function (event) {
                    if (event.keyCode == 13) {
                        $("#searchButton").click();
                        return false;
                    }
                });
                // "input" also covers paste, autofill and mobile keyboards, which never fire keypress.
                // Clearing the field hands it back to the map pin's reverse geocode.
                $("#Call_Address").on("input", function () {
                    userSuppliedAddress = jQuery.trim($(this).val() || '') !== '';
                });
                $("#What3Word").bind("keypress", function (event) {
                    if (event.keyCode == 13) {
                        $("#findw3wButton").click();
                        return false;
                    }
                });

                $("#CallPriority").change(function () {
                    checkForProtocols();
                    newcall.checkForRecommendations();
                });

                $("#Call_Type").change(function () {
                    checkForProtocols();
                    newcall.checkForRecommendations();
                });

                $("#Latitude, #Longitude").change(function () {
                    newcall.checkForRecommendations();
                    newcall.scheduleNearestUnits();
                });

                // The department's new-call field policy can leave the note, the map and other fields off the
                // form, so anything bound to one of them has to cope with it not being there.
                let noteQuillDescription = $('#note-container').length ? new Quill('#note-container', {
                    placeholder: '',
                    theme: 'snow'
                }) : null;

                $(document).on('submit', '#newCallForm', function () {
                    if (noteQuillDescription) {
                        $('#Call_Notes').val(noteQuillDescription.root.innerHTML);
                    }
                    $('#Call_NatureOfCall').val(quillNote2.root.innerHTML);

                    return true;
                });

                if (newCallFormData) {
                    let newCallForm = $('#fb-template').formRender({
                        dataType: 'json',
                        formData: newCallFormData
                    });
                }

                $("#saveNewCallFrom").click(function (evt) {
                    var data = JSON.stringify(newCallForm.userData);
                    $("#Call_CallFormData").val(data);
                });

                $("#selectLinkedCall").select2({
                    dropdownParent: $("#selectCallToLinkModal"),
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallsForSelectList',
                        dataType: 'json',
                        delay: 250,
                        data: function (params) {
                            return {
                                term: params.term
                            };
                        },
                    }
                });

                // No map when GPS coordinates are hidden by the field policy.
                if (document.getElementById('callMap')) {
                    // resgrid.maps: MapLibre or Leaflet by the department's Maps.MapLibreWeb flag (maplibre plan section 7).
                    resgrid.maps.create('callMap', { center: [Number(centerLat), Number(centerLng)], zoom: 11, scrollZoom: false, traffic: 'on' }).then(function (created) {
                        map = created;

                    map.onClick(function (clicked) {
                            var e = { latlng: { lat: clicked[0], lng: clicked[1] } };
                        callLocationChanged();
                        resgrid.dispatch.nearestboard.keepView(e.latlng.lat, e.latlng.lng);
                        resgrid.dispatch.newcall.setMarkerLocation(e.latlng.lat, e.latlng.lng);

                        $("#Latitude").val(e.latlng.lat.toString());
                        $("#Longitude").val(e.latlng.lng.toString());
                        //$("#What3Word").val('');

                        map.setView(clicked);

                        // A click inside a room of a shown facility map sets the call's indoor location; the room
                        // names the place better than a street address would.
                        var room = resgrid.dispatch.facilitymaps ? resgrid.dispatch.facilitymaps.zoneAt(e.latlng.lat, e.latlng.lng) : null;
                        if (room) {
                            resgrid.dispatch.facilitymaps.selectZone(room, e.latlng.lat, e.latlng.lng);
                            return;
                        }

                        resgrid.dispatch.newcall.geocodeCoordinates(e.latlng.lat, e.latlng.lng);
                    });

                    resgrid.dispatch.nearestboard.drawBoundaries();

                        // Location POI, destination and route (resgrid.dispatch.calllocations.js) draw on the same map.
                        if (resgrid.dispatch.calllocations && typeof resgrid.dispatch.calllocations.mapReady === 'function') {
                            resgrid.dispatch.calllocations.mapReady(map);
                        }

                        // Facility maps (site plans, building floors) with rooms to click (resgrid.dispatch.facilitymaps.js).
                        if (resgrid.dispatch.facilitymaps) {
                            resgrid.dispatch.facilitymaps.mapReady(map, {
                                setLocation: function (lat, lng) {
                                    // A room was picked (a click in it or the Indoor Location search): its zone stays.
                                    callLocationChanged(true);
                                    resgrid.dispatch.newcall.setMarkerLocation(lat, lng);
                                    $("#Latitude").val(lat.toString());
                                    $("#Longitude").val(lng.toString());
                                }
                            });
                        }

                        // A location placed before the map finished loading gets its pin now.
                        var placedLat = parseFloat($("#Latitude").val());
                        var placedLng = parseFloat($("#Longitude").val());
                        if (!isNaN(placedLat) && !isNaN(placedLng)) {
                            resgrid.dispatch.newcall.setMarkerLocation(placedLat, placedLng);
                        }
                    });
                }
                $("#searchButton").click(function (evt) {
                    var where = jQuery.trim($("#Call_Address").val());
                    if (where.length < 1)
                        return;

					fetch('/api/web-bff/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where))
                        .then(function(r) {
                            if (!r.ok) { throw new Error("Geocode request failed: " + r.status + " " + r.statusText); }
                            return r.json();
                        })
                        .then(function(result) {
                            if (result && result.Data && result.Data.Latitude != null && result.Data.Longitude != null) {
                                var lat = result.Data.Latitude;
                                var lng = result.Data.Longitude;
                                callLocationChanged();
                                if (map) {
                                    map.setView([Number(lat), Number(lng)], 16);
                                }
                                $("#Latitude").val(lat.toString());
                                $("#Longitude").val(lng.toString());
                                resgrid.dispatch.newcall.setMarkerLocation(lat, lng);
                            } else {
                                console.log("Geocode returned no results for: " + where);
                            }
                        })
                        .catch(function(err) { console.error("Geocode error:", err); });
                    evt.preventDefault();
                });
                $("#findw3wButton").click(function (evt) {
                    var word = jQuery.trim($("#What3Word").val());
                    if (word.length < 1)
                        return;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCoordinatesFromW3W?words=' + word,
                        contentType: 'application/json',
                        type: 'GET'
                    }).done(function (data) {
                        if (data && data.Latitude != null && data.Longitude != null) {
                            callLocationChanged();
                            if (map) {
                                map.setView([Number(data.Latitude), Number(data.Longitude)], 16);
                            }

                            $("#Latitude").val(data.Latitude);
                            $("#Longitude").val(data.Longitude);

                            resgrid.dispatch.newcall.geocodeCoordinates(data.Latitude, data.Longitude);

                            resgrid.dispatch.newcall.setMarkerLocation(data.Latitude, data.Longitude);
                        }
                        else {
                            alert(getText('whatThreeWordsNotFound', 'What3Words was unable to find a location for those words. Ensure they are 3 words separated by periods.'));
                        }
                    });
                    evt.preventDefault();
                });
                $("#setPinButton").click(function (evt) {
                    var lat = parseFloat($("#Latitude").val());
                    var lng = parseFloat($("#Longitude").val());
                    if (isNaN(lat) || isNaN(lng)) {
                        alert(getText('invalidCoordinates', 'Please enter valid numeric latitude and longitude values.'));
                        return false;
                    }
                    callLocationChanged();
                    if (map) {
                        map.setView([Number(lat), Number(lng)], 16);
                    }
                    resgrid.dispatch.newcall.setMarkerLocation(lat, lng);
                    evt.preventDefault();
                });
                // Distance, ETA and position come from the nearest-unit board (one ranked request) once the call has a
                // location, closest first; not a routed lookup per person.
                var personnelTable = $("#personnelGrid").DataTable(resgrid.dispatch.nearestboard.personnelTableOptions(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid'));
                personnelGrid = personnelTable;
                personnelTable.on('draw', function() {
                    // The rows are new DOM on every draw, so any run card recommendation has
                    // to be re-applied here rather than only when the response arrives.
                    applyRecommendationSelections();
                });

                var groupsTable = $("#groupsGrid").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Groups/GetGroupsForCallGrid', dataSrc: '' },
                    paging: false,
                    columns: [
                        { data: 'GroupId', title: '', orderable: false, searchable: false, render: function(data) { return '<input type="checkbox" id="dispatchGroup_'+data+'" name="dispatchGroup_'+data+'" />'; } },
                        { data: 'Name', title: getText('name', 'Name'), render: resgrid.textCell },
                        { data: 'Count', title: getText('personnelCount', 'Personnel Count') }
                    ]
                });
                groupsTable.on('draw', function() {
                    $('#groupsGrid thead th:first').html('<label><input type="checkbox" id="checkAllGroups"/></label>');
                });

                var rolesTable = $("#rolesGrid").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Personnel/GetRolesForCallGrid', dataSrc: '' },
                    paging: false,
                    columns: [
                        { data: 'RoleId', title: '', orderable: false, searchable: false, render: function(data) { return '<input type="checkbox" id="dispatchRole_'+data+'" name="dispatchRole_'+data+'" />'; } },
                        { data: 'Name', title: getText('name', 'Name'), render: resgrid.textCell },
                        { data: 'Count', title: getText('personnelCount', 'Personnel Count') }
                    ]
                });
                rolesTable.on('draw', function() {
                    $('#rolesGrid thead th:first').html('<label><input type="checkbox" id="checkAllRoles"/></label>');
                });


                $('#addNewLinkedCall').click(function (e) {
                    var data = $('#selectLinkedCall').select2('data');

                    // Nothing picked yet: keep the modal open (stopping propagation keeps Bootstrap's delegated
                    // data-dismiss handler from closing it) and open the picker instead.
                    if (!data || !data.length || !data[0].id) {
                        e.stopPropagation();
                        $('#selectLinkedCall').select2('open');
                        return;
                    }

                    var callId = Number(data[0].id);
                    var note = $('#selectCallNote').val() || '';

                    // Built with DOM calls, not an HTML string: the call name and note are user-entered text.
                    if ($('#linkedCall_' + callId).length === 0) {
                        var row = $('<tr></tr>');
                        $('<td style="max-width: 215px;"></td>').text(data[0].text || '')
                            .append($('<input type="hidden" />').attr({ id: 'linkedCall_' + callId, name: 'linkedCall_' + callId }).val(callId))
                            .appendTo(row);
                        $('<td></td>').text(note)
                            .append($('<input type="hidden" />').attr({ id: 'linkedCallNote_' + callId, name: 'linkedCallNote_' + callId }).val(note))
                            .appendTo(row);
                        $('<td style="text-align:center;"></td>')
                            .append($('<a class="tip-top"><i class="fa fa-minus" style="color: red;"></i></a>')
                                .attr('data-original-title', getText('removeThisCallLink', 'Remove this call link'))
                                .on('click', function () { $(this).closest('tr').remove(); }))
                            .appendTo(row);
                        $('#linkedCalls tbody').first().append(row);
                    }

                    $('#selectCallNote').val('');
                    $('#selectLinkedCall').empty();
                });

                function fillCallTemplate() {
                    var templateId = $('#CallTemplateId').val();

                    if (templateId && templateId > 0) {
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Templates/GetTemplate?id=' + templateId,
                            contentType: 'application/json',
                            type: 'GET'
                        }).done(function (data) {
                            if (data) {
                                if (data.CallName && data.CallName.length > 0) {
                                    $('#Call_Name').val(data.CallName);
                                }

                                if (data.CallNature && data.CallNature.length > 0) {
                                    $('#Call_NatureOfCall').val(data.CallNature);
                                    quillNote2.setText(data.CallNature);

                                }

                                if (data.CallType && data.CallType.length > 0) {
                                    $('#Call_Type').val(data.CallType);
                                }

                                // Priority 0 is a real value (the default Low priority), so test the
                                // type rather than truthiness.
                                if (typeof data.CallPriority === 'number' && data.CallPriority >= 0) {
                                    $('#CallPriority').val(data.CallPriority);
                                }

                                $('input[name="Call.CheckInTimersEnabled"]').prop('checked', !!data.CheckInTimersEnabled);

                                // .val() raises no change event, so the type/priority handlers never
                                // ran for a template. Re-run them here so the template's run card and
                                // protocols show up (and pre-check resources) as if picked by hand.
                                checkForProtocols();
                                newcall.checkForRecommendations();
                            }
                        });
                    }
                }
                newcall.fillCallTemplate = fillCallTemplate;

                // Bound here rather than with inline handlers: the page is clickable while this
                // script is still loading, and an inline call into resgrid.dispatch.newcall would
                // throw until it has run (RESGRID-WEB-1MA). The button renders disabled until now.
                $('#setCallTemplateButton').on('click', fillCallTemplate).prop('disabled', false);

                $('#personnelGrid').on('click', '#checkAllPersonnel', function () {
                    $('#personnelGrid').find('tbody :checkbox').prop('checked', this.checked);
                });
                $('#groupsGrid').on('click', '#checkAllGroups', function () {
                    $('#groupsGrid').find('tbody :checkbox').prop('checked', this.checked);
                });
                $('#rolesGrid').on('click', '#checkAllRoles', function () {
                    $('#rolesGrid').find('tbody :checkbox').prop('checked', this.checked);
                });
                $('a[data-toggle="tab"]').on('shown.bs.tab', function (e) {
                    // DataTables adjusts itself; trigger resize for any hidden columns
                    var targetTab = e.target && e.target.getAttribute('href');
                    if (targetTab === '#personnelTab') { personnelTable.columns.adjust(); }
                    else if (targetTab === '#groupsTab') { groupsTable.columns.adjust(); }
                    else if (targetTab === '#rolesTab') { rolesTable.columns.adjust(); }
                });
                centerMap();
            });
            function centerMap() {
                if (map && centerLat && centerLng) {
                    map.setView([Number(centerLat), Number(centerLng)]);
                }
            }
            newcall.centerMap = centerMap;
            function foundLocation(position) {
                if (map) {
                    map.setView([Number(position.coords.latitude), Number(position.coords.longitude)]);
                }
            }
            newcall.foundLocation = foundLocation;
            function noLocation() {
                // Browser geolocation was denied or unavailable. Fall back to the department's
                // configured map centre -- this used to pan to a hardcoded coordinate in Wollongong,
                // Australia, which every department outside NSW saw as "the map is in the wrong place".
                if (map && centerLat && centerLng) {
                    map.setView([Number(centerLat), Number(centerLng)]);
                }
            }
            newcall.noLocation = noLocation;
            function setMarkerLocation(lat, lng) {
                // Every way of placing the call (map click, address or What3Words search,
                // typed coordinates) ends here, so this is where the unit board and the run
                // card recommendation refresh. These paths set #Latitude/#Longitude with .val(),
                // which raises no change event, so the recommendation used to keep the
                // "no usable location" answer it got when the priority or type was picked.
                scheduleNearestUnits(lat, lng);
                scheduleRecommendations();

                if (!map) {
                    return;
                }

                if (callMarker) {
                    callMarker.setLatLng([Number(lat), Number(lng)]);
                } else {
                    callMarker = map.addMarker([Number(lat), Number(lng)], { draggable: true });
                    callMarker.onDragEnd(function (moved) {
                        var position = { lat: moved[0], lng: moved[1] };
                        callLocationChanged();
                        map.setView(moved);

                        $("#Latitude").val(position.lat);
                        $("#Longitude").val(position.lng);
                        //$("#What3Word").val('');

                        resgrid.dispatch.newcall.geocodeCoordinates(position.lat, position.lng);
                        resgrid.dispatch.nearestboard.keepView(position.lat, position.lng);
                        scheduleNearestUnits(position.lat, position.lng);
                        scheduleRecommendations();
                    });
                }
            }
            newcall.setMarkerLocation = setMarkerLocation;
            function getAuthToken() {
                return '';
            }
            // Reverse geocodes (the address of a clicked or found point): each takes the next number and fills the address only
            // while it is still the latest, so the answer for an earlier click never lands over a newer location (a picked room's
            // name, another click). Returns the lookup, which always settles (a failure is logged), for callers that wait on it.
            var reverseGeocodeRequest = 0;
            function geocodeCoordinates(lat, lng) {
                var request = ++reverseGeocodeRequest;
				return fetch('/api/web-bff/api/v4/Geocoding/ReverseGeocode?lat=' + lat + '&lon=' + lng)
                    .then(function(r) {
                        if (!r.ok) throw new Error('ReverseGeocode failed with HTTP status ' + r.status);
                        return r.json();
                    })
                    .then(function(result) {
                        if (request === reverseGeocodeRequest && result && result.Data && result.Data.Address && !userSuppliedAddress) {
                            $("#Call_Address").val(result.Data.Address);
                        }
                    })
                    .catch(function(err) { console.error("Reverse geocode error:", err); });
            }
            newcall.geocodeCoordinates = geocodeCoordinates;
            /**
             * The dispatcher placed the call another way (map click, search, pin, dragged marker, room): an address lookup for an
             * earlier point and a call plus code still typed or being looked up (resgrid.dispatch.calllocations) no longer apply, and
             * neither does the indoor location unless keepRoom (the facility map's room pick placed the call in that room).
             */
            function callLocationChanged(keepRoom) {
                reverseGeocodeRequest++;
                if (resgrid.dispatch.calllocations && typeof resgrid.dispatch.calllocations.callLocationChanged === 'function') {
                    resgrid.dispatch.calllocations.callLocationChanged(keepRoom === true);
                }
            }
            newcall.callLocationChanged = callLocationChanged;
            function findLocation(pos) {
				fetch('/api/web-bff/api/v4/Geocoding/ReverseGeocode?lat=' + pos.lat + '&lon=' + pos.lng)
                    .then(function(r) {
                        if (!r.ok) throw new Error('ReverseGeocode failed with HTTP status ' + r.status);
                        return r.json();
                    })
                    .then(function(result) {
                        if (result && result.Data && result.Data.Address) {
                            $("#Call_Address").val(result.Data.Address);
                        }
                    })
                    .catch(function(err) { console.error("Reverse geocode error:", err); });
                $("#Latitude").val(pos.lat.toString());
                $("#Longitude").val(pos.lng.toString());
            }
            newcall.findLocation = findLocation;
            // The protocol panel (resgrid.dispatch.protocolpanel.js) asks the server's matcher (Protocols/GetProtocolsForCall)
            // and runs the questions inline; this only tells it the call's type or priority changed.
            var protocolPanelStarted = false;
            function checkForProtocols() {
                if (!resgrid.dispatch.protocolpanel)
                    return;

                if (!protocolPanelStarted) {
                    protocolPanelStarted = true;
                    resgrid.dispatch.protocolpanel.init({
                        getPriority: function () { return $('#CallPriority').val(); },
                        getType: function () { return $('#Call_Type').val(); },
                        getLoggedOn: function () { return null; },
                        getCallId: function () { return null; }
                    });
                    return;
                }

                resgrid.dispatch.protocolpanel.refresh();
            }
            newcall.checkForProtocols = checkForProtocols;

            // ── Run card recommendations (pre-populate mode) ──
            function prop(obj, name) {
                if (!obj) return undefined;
                if (obj[name] !== undefined) return obj[name];
                var pascal = name.charAt(0).toUpperCase() + name.slice(1);
                return obj[pascal];
            }

            // Ids the current recommendation ticked, so a later one can untick exactly those
            // and leave the dispatcher's own selections alone. The sequence number lets a
            // slow earlier response be discarded instead of overwriting a newer one.
            var recommendationSequence = 0;
            var recommendationTimer = null;
            var recommendedUnitIds = [];
            var recommendedUserIds = [];

            // Debounced like the unit board: a map click sets the marker before it writes the
            // coordinate fields, so the request reads them once the burst has settled.
            function scheduleRecommendations() {
                if (recommendationTimer) {
                    clearTimeout(recommendationTimer);
                }
                recommendationTimer = setTimeout(checkForRecommendations, 400);
            }
            newcall.scheduleRecommendations = scheduleRecommendations;

            function clearRecommendationSelections() {
                recommendedUnitIds.forEach(function (id) {
                    $('input[name="dispatchUnit_' + id + '"]').prop('checked', false);
                });
                recommendedUserIds.forEach(function (id) {
                    $('input[name="dispatchUser_' + id + '"]').prop('checked', false);
                });

                recommendedUnitIds = [];
                recommendedUserIds = [];
                resgrid.dispatch.nearestboard.syncPicks();
            }

            // Personnel checkboxes are rendered by the DataTable, so they may not exist when
            // the recommendation lands and are rebuilt unchecked on every redraw. This is
            // called both on response and from the grid's draw handler.
            function applyRecommendationSelections() {
                recommendedUnitIds.forEach(function (id) {
                    $('input[name="dispatchUnit_' + id + '"]').prop('checked', true);
                });
                recommendedUserIds.forEach(function (id) {
                    $('input[name="dispatchUser_' + id + '"]').prop('checked', true);
                });
                resgrid.dispatch.nearestboard.syncPicks();
            }
            newcall.applyRecommendationSelections = applyRecommendationSelections;

            function checkForRecommendations() {
                // Every type, priority, template and location change ends here (map, search and drag write the fields with
                // .val(), which raises no change event), so the suggested destinations refresh with the run card.
                if (resgrid.dispatch.destinations) {
                    resgrid.dispatch.destinations.schedule();
                }

                var callPriorityVal = $('#CallPriority').val();
                var callTypeVal = $('#Call_Type').val();
                var lat = $('#Latitude').val();
                var lon = $('#Longitude').val();
                var requestSequence = ++recommendationSequence;

                // Drop the previous proposal's outcome and codes as the lookup starts, so a save while it runs, or after it
                // fails, never posts an outcome for a type or priority the dispatcher has since changed.
                $('#RunCardOutcomeNodeKey').val('');
                $('#runCardCodePickers').empty();

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetDispatchRecommendation',
                    data: { priority: callPriorityVal, type: callTypeVal, latitude: lat || null, longitude: lon || null },
                    type: 'GET'
                }).done(function (response) {
                    if (requestSequence !== recommendationSequence) {
                        return;
                    }

                    // Drop the previous recommendation's ticks before deciding what this one
                    // shows, so no-match and auto-dispatch responses clear them too.
                    clearRecommendationSelections();

                    var panel = $('#runCardPanel');
                    var row = $('#runCardPanelRow');
                    var result = prop(response, 'result');

                    if (!response || !prop(response, 'success') || !result || !prop(result, 'matchedRunCardId')) {
                        row.hide();
                        return;
                    }

                    var decision = prop(result, 'decision');
                    var decisionRenderer = resgrid.dispatch.runcarddecision;

                    var autoDispatch = prop(result, 'autoDispatch') === true;
                    var units = prop(result, 'units') || [];
                    var personnel = prop(result, 'personnel') || [];
                    var shortfalls = prop(result, 'shortfalls') || [];
                    var notes = prop(result, 'notes') || [];

                    var html = '<strong>' + $('<span>').text(prop(result, 'matchedRunCardName') || '').html() + '</strong>';
                    if (autoDispatch) {
                        html += ' <span class="label label-warning">Auto-dispatch is ON — recommended resources will be dispatched automatically on save.</span>';
                    }

                    if (decision && decisionRenderer) {
                        html += decisionRenderer.renderDecision(decision);
                    }

                    if (units.length) {
                        html += '<div><b>' + $('<span>').text(resgrid.terms.apply('Units:')).html() + '</b> ' + units.map(function (u) {
                            var text = prop(u, 'unitName') || ('#' + prop(u, 'unitId'));
                            var distance = prop(u, 'distanceMeters');
                            var response = prop(u, 'responseSeconds');
                            if (distance && response !== null && response !== undefined) {
                                // m:ss, as the board shows and the run card ranks (to the second).
                                var total = Math.max(0, Math.round(response));
                                var rest = total % 60;
                                text += ' (' + (distance / 1000).toFixed(1) + ' km, ' + Math.floor(total / 60) + ':' + (rest < 10 ? '0' : '') + rest + ' ' + nearestText('minutes') + ')';
                            } else if (distance) {
                                text += ' (' + (distance / 1000).toFixed(1) + ' km)';
                            }
                            if (prop(u, 'rowLabel') && decision) {
                                text += ' - ' + prop(u, 'rowLabel');
                            }
                            return $('<span>').text(text).html()
                                + (decisionRenderer ? decisionRenderer.codeChip(prop(u, 'responseCode'), prop(u, 'responseCodeName'), prop(u, 'responseCodeColor')) : '');
                        }).join(', ') + '</div>';
                    }
                    if (personnel.length) {
                        html += '<div><b>' + $('<span>').text(resgrid.terms.apply('Personnel:')).html() + '</b> ' + personnel.length + ' recommended</div>';
                    }
                    if (shortfalls.length) {
                        html += '<div class="text-danger"><b>Shortfalls:</b> ' + shortfalls.map(function (s) {
                            return $('<span>').text((prop(s, 'typeOrRoleName') || ('#' + prop(s, 'typeOrRoleId'))) + ': ' + prop(s, 'filledCount') + '/' + prop(s, 'requiredCount')).html();
                        }).join(', ') + '</div>';
                    }
                    if (notes.length) {
                        html += '<div class="text-muted" style="font-size: 11px;">' + notes.map(function (n) { return $('<span>').text(n).html(); }).join('<br/>') + '</div>';
                    }

                    panel.html(html);
                    row.show();

                    // Conditional run cards: post the outcome shown here so the saved call keeps what the dispatcher reviewed,
                    // and offer a code per proposed unit when the department uses response codes.
                    if (decision) {
                        $('#RunCardOutcomeNodeKey').val(prop(decision, 'outcomeNodeKey') || '');
                    }
                    if (decisionRenderer) {
                        $('#runCardCodePickers').html(decisionRenderer.renderCodePickers(units, prop(response, 'codes')));
                    }

                    // Pre-check recommended resources when NOT auto-dispatching (dispatcher
                    // reviews and can uncheck; the normal form post picks these up).
                    if (!autoDispatch) {
                        recommendedUnitIds = units.map(function (u) { return prop(u, 'unitId'); });
                        recommendedUserIds = personnel.map(function (p) { return prop(p, 'userId'); });

                        applyRecommendationSelections();
                    }
                }).fail(function () {
                    if (requestSequence !== recommendationSequence) {
                        return;
                    }

                    // A failed lookup leaves no recommendation to show, so drop the previous
                    // one's ticks and panel instead of letting stale resources ride along on
                    // the save.
                    clearRecommendationSelections();
                    $('#runCardPanel').empty();
                    $('#runCardPanelRow').hide();
                });
            }
            newcall.checkForRecommendations = checkForRecommendations;

            // Nearest available unit board: resgrid.dispatch.nearestboard (shared with Update Call).
            var personnelGrid = null;
            function nearestText(key) {
                return (typeof nearestUnitStrings !== 'undefined' && nearestUnitStrings && nearestUnitStrings[key]) || key;
            }
            function scheduleNearestUnits(lat, lng) {
                resgrid.dispatch.nearestboard.schedule(lat, lng);
            }
            newcall.scheduleNearestUnits = scheduleNearestUnits;
            function checkForNearestUnits(lat, lng) {
                resgrid.dispatch.nearestboard.check(lat, lng);
            }
            newcall.checkForNearestUnits = checkForNearestUnits;

            checkForProtocols();
            checkForRecommendations();
            checkForNearestUnits();
        })(newcall = dispatch.newcall || (dispatch.newcall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
