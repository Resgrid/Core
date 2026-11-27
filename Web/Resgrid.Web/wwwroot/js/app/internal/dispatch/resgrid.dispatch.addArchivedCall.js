var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var addArchivedCall;
        (function (addArchivedCall) {
            var personnelTable, unitsTable;
            // A unit's or call's name with its type icon (uploaded or built-in, from TypeIconUrl) in front, both escaped;
            // sorting and search keep using the plain name.
            function typeIconNameCell(name, type, row) {
                if (type !== 'display') { return name; }
                var text = $('<div/>').text(name || '').html();
                if (!row || !row.TypeIconUrl) { return text; }
                return '<img src="' + $('<div/>').text(row.TypeIconUrl).html() + '" alt="" aria-hidden="true" class="rg-type-icon" ' +
                    'style="width:20px;height:20px;object-fit:contain;vertical-align:middle;margin-right:4px;" />' + text;
            }

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
            // A unit's status in its own button and text colours, as on the New Call unit list. Older built-in
            // statuses carry a label class instead of a colour. Colours go through .css() so one never lands in
            // the markup unchecked; the border keeps a white or very light status visible on white rows.
            function unitStatusLabel(row) {
                var label = $('<span class="label label-default">').text(row.State || '');
                var color = $.trim(row.StateColor || '');
                if (/^label-[a-z]+$/.test(color)) {
                    label.addClass(color);
                } else if (color) {
                    label.css({ 'background-color': color, 'border': '1px solid rgba(0, 0, 0, 0.2)' });
                    if (row.TextColor) {
                        label.css('color', row.TextColor);
                    }
                }
                return label.prop('outerHTML');
            }
            // The suggested destinations (resgrid.dispatch.destinations.js, suggest only here) follow the incident location,
            // type and priority. Pin, lookup and template changes set the fields without a change event, so they ask here.
            function scheduleDestinations() {
                if (resgrid.dispatch.destinations && typeof resgrid.dispatch.destinations.schedule === 'function') {
                    resgrid.dispatch.destinations.schedule();
                }
            }

            $(document).ready(function () {
                callMarker = null;
                map = null;
                // A typed (or previously saved) address always wins over a reverse-geocoded one; only an
                // empty field is filled from the map pin.
                userSuppliedAddress = jQuery.trim($("#Call_Address").val() || '') !== '';

                let quillNature = new Quill('#nature-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                // The department's new-call field policy can leave the note, the map and other fields off the
                // form, so anything bound to one of them has to cope with it not being there.
                let quillNotes = $('#note-container').length ? new Quill('#note-container', {
                    placeholder: '',
                    theme: 'snow'
                }) : null;

                $(document).on('submit', '#addArchivedCallForm', function () {
                    $('#Call_NatureOfCall').val(quillNature.root.innerHTML);
                    if (quillNotes) {
                        $('#Call_Notes').val(quillNotes.root.innerHTML);
                    }

                    return true;
                });

                if (newCallFormData) {
                    let newCallForm = $('#fb-template').formRender({
                        dataType: 'json',
                        formData: newCallFormData
                    });

                    $("#saveNewCallFrom").click(function (evt) {
                        var data = JSON.stringify(newCallForm.userData);
                        $("#Call_CallFormData").val(data);
                    });
                }

                // Match the server-rendered department-local timestamp when the picker first opens.
                $('#Call_LoggedOn').datetimepicker({ format: 'Y/m/d H:i', step: 5 });

                $('#PrimaryContact').select2();
                $('#AdditionalContacts').select2();

                $("#Call_Address").bind("keypress", function (event) {
                    if (event.keyCode == 13) {
                        $("#searchButton").click();
                        return false;
                    }
                });
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
                });

                $("#Call_Type").change(function () {
                    checkForProtocols();
                });

                // No map when GPS coordinates are hidden by the field policy.
                if (document.getElementById('callMap')) {
                    // resgrid.maps: MapLibre or Leaflet by the department's Maps.MapLibreWeb flag (maplibre plan section 7).
                    resgrid.maps.create('callMap', { center: [Number(centerLat), Number(centerLng)], zoom: 11, scrollZoom: false, traffic: 'on' }).then(function (created) {
                        map = created;

                    map.onClick(function (clicked) {
                            var e = { latlng: { lat: clicked[0], lng: clicked[1] } };
                        callLocationChanged();
                        resgrid.dispatch.addArchivedCall.setMarkerLocation(e.latlng.lat, e.latlng.lng);

                        $("#Latitude").val(e.latlng.lat.toString());
                        $("#Longitude").val(e.latlng.lng.toString());

                        map.setView(clicked);

                        resgrid.dispatch.addArchivedCall.geocodeCoordinates(e.latlng.lat, e.latlng.lng);
                    });

                        // Location POI, destination and route (resgrid.dispatch.calllocations.js) draw on the same map.
                        if (resgrid.dispatch.calllocations && typeof resgrid.dispatch.calllocations.mapReady === 'function') {
                            resgrid.dispatch.calllocations.mapReady(map);
                        }
                    });
                }

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

                navigator.geolocation.getCurrentPosition(foundLocation, noLocation, { timeout: 10000 });
                $("#searchButton").click(function (evt) {
                    var where = jQuery.trim($("#Call_Address").val());
                    if (where.length < 1)
                        return;

					fetch('/api/web-bff/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where))
						.then(function(r) {
							if (!r.ok) throw new Error('ForwardGeocode failed with HTTP status ' + r.status);
							return r.json();
						})
                        .then(function(result) {
                            if (result && result.Data && result.Data.Latitude && result.Data.Longitude) {
                                var lat = result.Data.Latitude;
                                var lng = result.Data.Longitude;
                                callLocationChanged();
                                if (map) {
                                    map.setView([Number(lat), Number(lng)], 16);
                                }
                                $("#Latitude").val(lat.toString());
                                $("#Longitude").val(lng.toString());
                                resgrid.dispatch.addArchivedCall.setMarkerLocation(lat, lng);
                            } else {
                                console.log("Geocode returned no results for: " + where);
                            }
                        })
						.catch(function(err) {
							console.error('ForwardGeocode failed', { operation: 'ForwardGeocode', address: where, error: err });
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
                    resgrid.dispatch.addArchivedCall.setMarkerLocation(lat, lng);
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
                        if (data && data.Latitude && data.Longitude) {
                            callLocationChanged();
                            if (map) {
                                map.setView([Number(data.Latitude), Number(data.Longitude)], 16);
                            }

                            $("#Latitude").val(data.Latitude);
                            $("#Longitude").val(data.Longitude);

                            resgrid.dispatch.addArchivedCall.geocodeCoordinates(data.Latitude, data.Longitude);

                            resgrid.dispatch.addArchivedCall.setMarkerLocation(data.Latitude, data.Longitude);
                        }
                        else {
                            alert(getText('whatThreeWordsNotFound', 'What3Words was unable to find a location for those words. Ensure they are 3 words separated by periods.'));
                        }
                    });
                    evt.preventDefault();
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

                personnelTable = $("#personnelGrid").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val(), dataSrc: '' },
                    paging: false,
                    columns: [
                        { data: 'UserId', title: '', orderable: false, searchable: false, render: function(data) { return '<input type="checkbox" id="dispatchUser_'+data+'" name="dispatchUser_'+data+'" />'; } },
                        { data: 'Name', title: getText('name', 'Name'), render: resgrid.textCell },
                        { data: 'Eta', title: getText('eta', 'ETA') },
                        { data: null, title: getText('status', 'Status'), orderable: false, render: function(d,t,row) { return '<span style="color:'+resgrid.escapeHtml(row.StatusColor)+'">'+resgrid.escapeHtml(row.Status)+'</span>'; } },
                        { data: null, title: getText('staffing', 'Staffing'), orderable: false, render: function(d,t,row) { return '<span style="color:'+resgrid.escapeHtml(row.StaffingColor)+'">'+resgrid.escapeHtml(row.Staffing)+'</span>'; } },
                        { data: 'Group', title: getText('group', 'Group'), render: resgrid.textCell },
                        { data: 'Roles', title: getText('roles', 'Roles'), render: resgrid.textCell }
                    ]
                });
                personnelTable.on('draw', function() {
                    $('#personnelGrid thead th:first').html('<label><input type="checkbox" id="checkAllPersonnel"/></label>');
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

                unitsTable = $("#unitsGrid").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val(), dataSrc: '' },
                    paging: false,
                    columns: [
                        { data: 'UnitId', title: '', orderable: false, searchable: false, render: function(data) { return '<input type="checkbox" id="dispatchUnit_'+data+'" name="dispatchUnit_'+data+'" />'; } },
                        { data: 'Name', title: getText('name', 'Name'), render: typeIconNameCell },
                        { data: 'Eta', title: getText('eta', 'ETA') },
                        { data: 'Type', title: getText('type', 'Type'), render: resgrid.textCell },
                        { data: null, title: getText('status', 'Status'), orderable: false, render: function(d,t,row) { return unitStatusLabel(row); } }
                    ]
                });
                unitsTable.on('draw', function() {
                    $('#unitsGrid thead th:first').html('<label><input type="checkbox" id="checkAllUnits"/></label>');
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

                $('#personnelGrid').on('click', '#checkAllPersonnel', function () { $('#personnelGrid').find('tbody :checkbox').prop('checked', this.checked); });
                $('#groupsGrid').on('click', '#checkAllGroups', function () { $('#groupsGrid').find('tbody :checkbox').prop('checked', this.checked); });
                $('#unitsGrid').on('click', '#checkAllUnits', function () { $('#unitsGrid').find('tbody :checkbox').prop('checked', this.checked); });
                $('#rolesGrid').on('click', '#checkAllRoles', function () { $('#rolesGrid').find('tbody :checkbox').prop('checked', this.checked); });
                $('a[data-toggle="tab"]').on('shown.bs.tab', function (e) {
                    var targetTab = e.target && e.target.getAttribute('href');
                    if (targetTab === '#personnelTab') { personnelTable.columns.adjust(); }
                    else if (targetTab === '#groupsTab') { groupsTable.columns.adjust(); }
                    else if (targetTab === '#rolesTab') { rolesTable.columns.adjust(); }
                });

                checkForProtocols();
                centerMap();
            });

            function centerMap() {
                if (map && centerLat && centerLng) {
                    map.setView([Number(centerLat), Number(centerLng)]);
                }
            }
            addArchivedCall.centerMap = centerMap;

            function foundLocation(position) {
                if (map) {
                    map.setView([Number(position.coords.latitude), Number(position.coords.longitude)]);
                }
            }
            addArchivedCall.foundLocation = foundLocation;

            function noLocation() {
                // Browser geolocation was denied or unavailable. Fall back to the department's
                // configured map centre -- this used to pan to a hardcoded coordinate in Wollongong,
                // Australia, which every department outside NSW saw as "the map is in the wrong place".
                if (map && centerLat && centerLng) {
                    map.setView([Number(centerLat), Number(centerLng)]);
                }
            }
            addArchivedCall.noLocation = noLocation;

            function setMarkerLocation(lat, lng) {
                // Before the map check: with the map hidden by the field policy, lookups still move the location.
                scheduleDestinations();

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
                        scheduleDestinations();

                        resgrid.dispatch.addArchivedCall.geocodeCoordinates(position.lat, position.lng);
                    });
                }
            }
            addArchivedCall.setMarkerLocation = setMarkerLocation;
            function getAuthToken() {
                return '';
            }
            // Reverse geocodes (the address of a clicked or found point): each takes the next number and fills the address only
            // while it is still the latest, so the answer for an earlier click never lands over a newer location. Returns the
            // lookup, which always settles (a failure is logged), for callers that wait on it.
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
					.catch(function(err) {
						console.error('ReverseGeocode failed', { operation: 'ReverseGeocode', latitude: lat, longitude: lng, error: err });
					});
            }
            addArchivedCall.geocodeCoordinates = geocodeCoordinates;
            /**
             * The dispatcher placed the call another way (map click, search, pin, dragged marker): an address lookup for an earlier
             * point, a call plus code still typed or being looked up and an indoor location picked earlier
             * (resgrid.dispatch.calllocations) no longer apply.
             */
            function callLocationChanged() {
                reverseGeocodeRequest++;
                if (resgrid.dispatch.calllocations && typeof resgrid.dispatch.calllocations.callLocationChanged === 'function') {
                    resgrid.dispatch.calllocations.callLocationChanged();
                }
            }
            addArchivedCall.callLocationChanged = callLocationChanged;

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
					.catch(function(err) {
						console.error('ReverseGeocode failed', { operation: 'ReverseGeocode', latitude: pos.lat, longitude: pos.lng, error: err });
					});
                $("#Latitude").val(pos.lat.toString());
                $("#Longitude").val(pos.lng.toString());
                scheduleDestinations();
            }
            addArchivedCall.findLocation = findLocation;

            function refreshPersonnelGrid() {
                personnelTable.ajax.url(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val()).load();
                unitsTable.ajax.url(resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val()).load();
            }
            addArchivedCall.refreshPersonnelGrid = refreshPersonnelGrid;

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
                            }

                            if (data.CallType && data.CallType.length > 0) {
                                $('#Call_Type').val(data.CallType);
                            }

                            // Priority 0 is a real value (the default Low priority), so test the
                            // type rather than truthiness.
                            if (typeof data.CallPriority === 'number' && data.CallPriority >= 0) {
                                $('#CallPriority').val(data.CallPriority);
                            }

                            // .val() raises no change event, so re-run the type/priority
                            // handler for the template's values.
                            checkForProtocols();
                        }
                    });
                }
            }
            addArchivedCall.fillCallTemplate = fillCallTemplate;
            // Bound here rather than inline: the button renders disabled until this script has
            // run, so an early click can't call into an undefined namespace (RESGRID-WEB-1MA).
            $('#setCallTemplateButton').on('click', fillCallTemplate).prop('disabled', false);

            // The protocol panel (resgrid.dispatch.protocolpanel.js) asks the server's matcher (Protocols/GetProtocolsForCall)
            // and runs the questions inline; this only tells it the call's type or priority changed.
            var protocolPanelStarted = false;
            function checkForProtocols() {
                // Type and priority changes (a template's too) re-rank the suggested destinations.
                scheduleDestinations();

                if (!resgrid.dispatch.protocolpanel)
                    return;

                if (!protocolPanelStarted) {
                    protocolPanelStarted = true;
                    resgrid.dispatch.protocolpanel.init({
                        getPriority: function () { return $('#CallPriority').val(); },
                        // P4: the case flow applies its outcome's priority; the location feeds zone triggers.
                        setPriority: function (value) { $('#CallPriority').val(value).trigger('change'); },
                        getLatitude: function () { return $('#Latitude').val(); },
                        getLongitude: function () { return $('#Longitude').val(); },
                        getType: function () { return $('#Call_Type').val(); },
                        getLoggedOn: function () { return $('#Call_LoggedOn').val(); },
                        getCallId: function () { return null; }
                    });
                    return;
                }

                resgrid.dispatch.protocolpanel.refresh();
            }
            addArchivedCall.checkForProtocols = checkForProtocols;

        })(addArchivedCall = dispatch.addArchivedCall || (dispatch.addArchivedCall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
