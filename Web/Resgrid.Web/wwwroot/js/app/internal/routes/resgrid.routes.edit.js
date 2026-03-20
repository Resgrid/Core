$(document).ready(function () {
    var map, stopPickerMap, stopMarker, pickerMarker;
    var startPickerMap, startMarker, startPickerInitialized = false;
    var endPickerMap, endMarker, endPickerInitialized = false;
    var antiForgeryToken = $('input[name="__RequestVerificationToken"]').first().val();

    function getAuthToken() {
        try {
            for (var i = 0; i < localStorage.length; i++) {
                var val = localStorage.getItem(localStorage.key(i));
                if (!val || val.charAt(0) !== '{') continue;
                var obj = JSON.parse(val);
                if (obj && typeof obj.access_token === 'string' && obj.access_token.length > 0) {
                    return obj.access_token;
                }
            }
        } catch (e) {}
        return '';
    }

    function escapeHtml(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // ── Main route map ────────────────────────────────────────────────────────
    if (document.getElementById('routeMap')) {
        var tiles = L.tileLayer(osmTileUrl, { maxZoom: 19, attribution: osmTileAttribution });
        map = L.map('routeMap', { scrollWheelZoom: false })
               .setView([39.8283, -98.5795], 4)
               .addLayer(tiles);

        if (typeof editStops !== 'undefined' && editStops.length > 0) {
            var group = [];
            editStops.forEach(function (stop, index) {
                if (stop.lat != null && stop.lng != null && Number.isFinite(Number(stop.lat)) && Number.isFinite(Number(stop.lng))) {
                    var m = L.marker([stop.lat, stop.lng]).addTo(map);
                    m.bindTooltip((index + 1) + '. ' + escapeHtml(stop.Name), { permanent: true, direction: 'right' });
                    m.bindPopup('<strong>' + (index + 1) + '. ' + escapeHtml(stop.Name) + '</strong>');
                    group.push(m);
                }
            });
            if (group.length > 0) {
                map.fitBounds(L.featureGroup(group).getBounds(), { padding: [50, 50] });
            }
        }
    }

    // ── Stop picker map helpers ───────────────────────────────────────────────
    function initStopPickerMap() {
        if (stopPickerMap) {
            stopPickerMap.invalidateSize();
            return;
        }
        var pickerTiles = L.tileLayer(osmTileUrl, { maxZoom: 19, attribution: osmTileAttribution });
        stopPickerMap = L.map('stopPickerMap', { scrollWheelZoom: false })
                         .setView([39.8283, -98.5795], 4)
                         .addLayer(pickerTiles);

        stopPickerMap.on('click', function (e) {
            setPickerLocation(e.latlng.lat, e.latlng.lng, true);
        });
    }

    function setPickerLocation(lat, lng, reverseGeocode) {
        $('#stopLat').val(lat.toFixed(6));
        $('#stopLng').val(lng.toFixed(6));
        if (pickerMarker) {
            pickerMarker.setLatLng(new L.LatLng(lat, lng));
        } else {
            pickerMarker = new L.marker(new L.LatLng(lat, lng), { draggable: true }).addTo(stopPickerMap);
            pickerMarker.on('dragend', function (event) {
                var pos = event.target.getLatLng();
                event.target.setLatLng(new L.LatLng(pos.lat, pos.lng));
                stopPickerMap.panTo(new L.LatLng(pos.lat, pos.lng));
                $('#stopLat').val(pos.lat.toFixed(6));
                $('#stopLng').val(pos.lng.toFixed(6));
                reverseGeocodeStop(pos.lat, pos.lng);
            });
        }
        stopPickerMap.setView(new L.LatLng(lat, lng), 15);
        if (reverseGeocode) {
            reverseGeocodeStop(lat, lng);
        }
    }

    function reverseGeocodeStop(lat, lng) {
        fetch(resgrid.absoluteApiBaseUrl + '/api/v4/Geocoding/ReverseGeocode?lat=' + lat + '&lon=' + lng, { headers: { 'Authorization': 'Bearer ' + getAuthToken() } })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result && result.Data && result.Data.Address) {
                    $('#stopAddress').val(result.Data.Address);
                }
            })
            .catch(function (err) { console.error('Stop reverse geocode error:', err); });
    }

    // ── Start/End location pickers ────────────────────────────────────────────
    function initStartPickerMap(initialLat, initialLng) {
        if (startPickerInitialized) {
            setTimeout(function () { startPickerMap.invalidateSize(); }, 50);
            return;
        }
        var tiles = L.tileLayer(osmTileUrl, { maxZoom: 19, attribution: osmTileAttribution });
        startPickerMap = L.map('startPickerMap', { scrollWheelZoom: false })
                          .setView([39.8283, -98.5795], 4)
                          .addLayer(tiles);
        startPickerMap.on('click', function (e) { setStartLocation(e.latlng.lat, e.latlng.lng); });
        startPickerInitialized = true;
        if (initialLat && initialLng) { setStartLocation(initialLat, initialLng); }
    }

    function setStartLocation(lat, lng) {
        var latStr = parseFloat(lat).toFixed(6);
        var lngStr = parseFloat(lng).toFixed(6);
        $('#startLat').val(latStr);
        $('#startLng').val(lngStr);
        $('#startLatDisplay').val(latStr);
        $('#startLngDisplay').val(lngStr);
        if (startMarker) {
            startMarker.setLatLng(new L.LatLng(lat, lng));
        } else {
            startMarker = L.marker(new L.LatLng(lat, lng), { draggable: true }).addTo(startPickerMap);
            startMarker.on('dragend', function (event) {
                var pos = event.target.getLatLng();
                setStartLocation(pos.lat, pos.lng);
            });
        }
        startPickerMap.setView(new L.LatLng(lat, lng), 15);
    }

    function initEndPickerMap(initialLat, initialLng) {
        if (endPickerInitialized) {
            setTimeout(function () { endPickerMap.invalidateSize(); }, 50);
            return;
        }
        var tiles = L.tileLayer(osmTileUrl, { maxZoom: 19, attribution: osmTileAttribution });
        endPickerMap = L.map('endPickerMap', { scrollWheelZoom: false })
                        .setView([39.8283, -98.5795], 4)
                        .addLayer(tiles);
        endPickerMap.on('click', function (e) { setEndLocation(e.latlng.lat, e.latlng.lng); });
        endPickerInitialized = true;
        if (initialLat && initialLng) { setEndLocation(initialLat, initialLng); }
    }

    function setEndLocation(lat, lng) {
        var latStr = parseFloat(lat).toFixed(6);
        var lngStr = parseFloat(lng).toFixed(6);
        $('#endLat').val(latStr);
        $('#endLng').val(lngStr);
        $('#endLatDisplay').val(latStr);
        $('#endLngDisplay').val(lngStr);
        if (endMarker) {
            endMarker.setLatLng(new L.LatLng(lat, lng));
        } else {
            endMarker = L.marker(new L.LatLng(lat, lng), { draggable: true }).addTo(endPickerMap);
            endMarker.on('dragend', function (event) {
                var pos = event.target.getLatLng();
                setEndLocation(pos.lat, pos.lng);
            });
        }
        endPickerMap.setView(new L.LatLng(lat, lng), 15);
    }

    function updateLocationPickerVisibility() {
        if (!$('#chkUseStationAsStart').prop('checked')) {
            $('#startLocationGroup').show();
            initStartPickerMap(
                (typeof existingStartLat !== 'undefined' && existingStartLat) ? existingStartLat : null,
                (typeof existingStartLng !== 'undefined' && existingStartLng) ? existingStartLng : null
            );
        } else {
            $('#startLocationGroup').hide();
        }
        if (!$('#chkUseStationAsEnd').prop('checked')) {
            $('#endLocationGroup').show();
            initEndPickerMap(
                (typeof existingEndLat !== 'undefined' && existingEndLat) ? existingEndLat : null,
                (typeof existingEndLng !== 'undefined' && existingEndLng) ? existingEndLng : null
            );
        } else {
            $('#endLocationGroup').hide();
        }
    }

    $('#chkUseStationAsStart').on('change', updateLocationPickerVisibility);
    $('#chkUseStationAsEnd').on('change', updateLocationPickerVisibility);

    // Start – address geocode
    $('#startAddress').on('keypress', function (e) { if (e.keyCode === 13) { $('#geocodeStartBtn').click(); return false; } });
    $('#geocodeStartBtn').on('click', function (evt) {
        evt.preventDefault();
        var where = $.trim($('#startAddress').val());
        if (!where) return;
        fetch(resgrid.absoluteApiBaseUrl + '/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where), { headers: { 'Authorization': 'Bearer ' + getAuthToken() } })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result && result.Data && result.Data.Latitude && result.Data.Longitude) {
                    setStartLocation(result.Data.Latitude, result.Data.Longitude);
                } else { alert('Address not found.'); }
            })
            .catch(function (err) { console.error('Geocode error:', err); });
    });

    // Start – What3Words
    $('#startW3W').on('keypress', function (e) { if (e.keyCode === 13) { $('#findStartW3WBtn').click(); return false; } });
    $('#findStartW3WBtn').on('click', function (evt) {
        evt.preventDefault();
        var word = $.trim($('#startW3W').val());
        if (!word) return;
        $.ajax({ url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCoordinatesFromW3W?words=' + encodeURIComponent(word), type: 'GET' })
            .done(function (data) {
                if (data && data.Latitude && data.Longitude) { setStartLocation(data.Latitude, data.Longitude); }
                else { alert('What3Words was unable to find a location for those words.'); }
            });
    });

    // Start – manual lat/lng
    $('#applyStartLatLngBtn').on('click', function () {
        var lat = parseFloat($('#startLatDisplay').val());
        var lng = parseFloat($('#startLngDisplay').val());
        if (isNaN(lat) || isNaN(lng)) { alert('Please enter valid coordinates.'); return; }
        setStartLocation(lat, lng);
    });

    // End – address geocode
    $('#endAddress').on('keypress', function (e) { if (e.keyCode === 13) { $('#geocodeEndBtn').click(); return false; } });
    $('#geocodeEndBtn').on('click', function (evt) {
        evt.preventDefault();
        var where = $.trim($('#endAddress').val());
        if (!where) return;
        fetch(resgrid.absoluteApiBaseUrl + '/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where), { headers: { 'Authorization': 'Bearer ' + getAuthToken() } })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result && result.Data && result.Data.Latitude && result.Data.Longitude) {
                    setEndLocation(result.Data.Latitude, result.Data.Longitude);
                } else { alert('Address not found.'); }
            })
            .catch(function (err) { console.error('Geocode error:', err); });
    });

    // End – What3Words
    $('#endW3W').on('keypress', function (e) { if (e.keyCode === 13) { $('#findEndW3WBtn').click(); return false; } });
    $('#findEndW3WBtn').on('click', function (evt) {
        evt.preventDefault();
        var word = $.trim($('#endW3W').val());
        if (!word) return;
        $.ajax({ url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCoordinatesFromW3W?words=' + encodeURIComponent(word), type: 'GET' })
            .done(function (data) {
                if (data && data.Latitude && data.Longitude) { setEndLocation(data.Latitude, data.Longitude); }
                else { alert('What3Words was unable to find a location for those words.'); }
            });
    });

    // End – manual lat/lng
    $('#applyEndLatLngBtn').on('click', function () {
        var lat = parseFloat($('#endLatDisplay').val());
        var lng = parseFloat($('#endLngDisplay').val());
        if (isNaN(lat) || isNaN(lng)) { alert('Please enter valid coordinates.'); return; }
        setEndLocation(lat, lng);
    });

    // Initialise visibility on page load (pre-populates from existingStart/EndLat/Lng if set)
    updateLocationPickerVisibility();

    // ── Populate contact picker ───────────────────────────────────────────────
    function populateContactPicker() {
        var $sel = $('#stopContactPicker');
        if ($sel.find('option').length > 1) return;
        if (typeof availableContacts !== 'undefined' && availableContacts.length > 0) {
            $.each(availableContacts, function (i, c) {
                $sel.append($('<option>').val(c.id).text(c.name));
            });
        }
    }

    $('#stopContactPicker').on('change', function () {
        var id = $(this).val();
        if (!id) {
            $('#stopContactId').val('');
            return;
        }
        $('#stopContactId').val(id);
        var contact = (typeof availableContacts !== 'undefined') ? availableContacts.find(function (c) { return c.id === id; }) : null;
        if (contact) {
            $('#stopContactName').val(contact.name);
            if (contact.phone) $('#stopContactNumber').val(contact.phone);
        }
    });

    // ── Initialise picker map when modal is shown ─────────────────────────────
    $('#addStopModal').on('shown.bs.modal', function () {
        initStopPickerMap();
        populateContactPicker();
    });

    // ── Reset modal when closed ───────────────────────────────────────────────
    $('#addStopModal').on('hidden.bs.modal', function () {
        $('#stopName').val('');
        $('#stopType').val('0').trigger('change');
        $('#stopPriority').val('0');
        $('#stopAddress').val('');
        $('#stopW3W').val('');
        $('#stopLat').val('');
        $('#stopLng').val('');
        $('#stopPlannedArrival').val('');
        $('#stopPlannedDeparture').val('');
        $('#stopDwellMinutes').val('');
        $('#stopContactPicker').val('');
        $('#stopContactId').val('');
        $('#stopContactName').val('');
        $('#stopContactNumber').val('');
        $('#stopNotes').val('');
        if (pickerMarker && stopPickerMap) {
            stopPickerMap.removeLayer(pickerMarker);
            pickerMarker = null;
        }
    });

    // ── Stop type toggle ──────────────────────────────────────────────────────
    $('#stopType').on('change', function () {
        if ($(this).val() === '1') {
            $('#linkedCallGroup').show();
            loadCallsForLinking();
        } else {
            $('#linkedCallGroup').hide();
        }
    });

    var callsLoaded = false;
    function loadCallsForLinking() {
        if (callsLoaded) return;
        $.getJSON(resgrid.absoluteBaseUrl + '/User/Routes/GetCallsForLinking', function (data) {
            var $sel = $('#linkedCallId').empty();
            $sel.append('<option value="">-- None --</option>');
            $.each(data, function (i, c) {
                $sel.append($('<option>').val(c.id).text(c.name + (c.address ? ' (' + c.address + ')' : '')));
            });
            callsLoaded = true;
        });
    }

    // ── Address geocoding (Enter key or button) ───────────────────────────────
    $('#stopAddress').on('keypress', function (e) {
        if (e.keyCode === 13) { $('#geocodeStopBtn').click(); return false; }
    });

    $('#geocodeStopBtn').on('click', function (evt) {
        evt.preventDefault();
        var where = $.trim($('#stopAddress').val());
        if (!where) return;
        fetch(resgrid.absoluteApiBaseUrl + '/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where), { headers: { 'Authorization': 'Bearer ' + getAuthToken() } })
            .then(function (r) { return r.json(); })
            .then(function (result) {
                if (result && result.Data && result.Data.Latitude && result.Data.Longitude) {
                    setPickerLocation(result.Data.Latitude, result.Data.Longitude, false);
                } else {
                    alert('Address not found.');
                }
            })
            .catch(function (err) { console.error('Geocode error:', err); });
    });

    // ── What3Words ────────────────────────────────────────────────────────────
    $('#stopW3W').on('keypress', function (e) {
        if (e.keyCode === 13) { $('#findStopW3WBtn').click(); return false; }
    });

    $('#findStopW3WBtn').on('click', function (evt) {
        evt.preventDefault();
        var word = $.trim($('#stopW3W').val());
        if (!word) return;
        $.ajax({
            url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCoordinatesFromW3W?words=' + encodeURIComponent(word),
            contentType: 'application/json',
            type: 'GET'
        }).done(function (data) {
            if (data && data.Latitude && data.Longitude) {
                setPickerLocation(data.Latitude, data.Longitude, true);
            } else {
                alert('What3Words was unable to find a location for those words. Ensure it is 3 words separated by periods.');
            }
        });
    });

    // ── Save stop ─────────────────────────────────────────────────────────────
    $('#saveStopBtn').on('click', function () {
        var name = $.trim($('#stopName').val());
        var lat  = $('#stopLat').val();
        var lng  = $('#stopLng').val();

        if (!name) { alert('Stop name is required.'); return; }
        if (!lat || !lng) { alert('Please select a location on the map, or use the address / What3Words finder.'); return; }

        var stopType = parseInt($('#stopType').val());
        var callId   = (stopType === 1) ? ($('#linkedCallId').val() || null) : null;

        $.ajax({
            url: resgrid.absoluteBaseUrl + '/User/Routes/AddStop',
            type: 'POST',
            data: {
                __RequestVerificationToken: antiForgeryToken,
                routePlanId:      routePlanId,
                name:             name,
                description:      '',
                stopType:         stopType,
                priority:         parseInt($('#stopPriority').val()),
                latitude:         parseFloat(lat),
                longitude:        parseFloat(lng),
                address:          $('#stopAddress').val(),
                callId:           callId,
                geofenceRadius:   null,
                plannedArrival:   $('#stopPlannedArrival').val(),
                plannedDeparture: $('#stopPlannedDeparture').val(),
                dwellMinutes:     $('#stopDwellMinutes').val() || null,
                contactName:      $('#stopContactName').val(),
                contactNumber:    $('#stopContactNumber').val(),
                contactId:        $('#stopContactId').val() || null,
                notes:            $('#stopNotes').val()
            },
            success: function (result) {
                if (result.success) {
                    $('#addStopModal').modal('hide');
                    location.reload();
                } else {
                    alert('Failed to add stop: ' + (result.message || 'Unknown error'));
                }
            },
            error: function () { alert('Error saving stop.'); }
        });
    });

    // ── Delete stop ───────────────────────────────────────────────────────────
    $(document).on('click', '.delete-stop-btn', function () {
        if (!confirm('Delete this stop?')) return;
        var stopId = $(this).data('stop-id');
        var $row   = $('#stopRow_' + stopId);
        $.ajax({
            url: resgrid.absoluteBaseUrl + '/User/Routes/DeleteStop',
            type: 'POST',
            data: {
                __RequestVerificationToken: antiForgeryToken,
                stopId: stopId
            },
            success: function (result) {
                if (result.success) {
                    $row.remove();
                    $('#stopCount').text($('#stopsTableBody tr').length);
                    // Update main map: remove all markers and redraw remaining
                    if (map) {
                        map.eachLayer(function (layer) {
                            if (layer instanceof L.Marker) map.removeLayer(layer);
                        });
                        editStops = editStops.filter(function (s) { return s.id !== stopId; });
                        editStops.forEach(function (stop, index) {
                            if (stop.lat != null && stop.lng != null && Number.isFinite(Number(stop.lat)) && Number.isFinite(Number(stop.lng))) {
                                var m = L.marker([stop.lat, stop.lng]).addTo(map);
                                m.bindTooltip((index + 1) + '. ' + escapeHtml(stop.Name), { permanent: true, direction: 'right' });
                                m.bindPopup('<strong>' + (index + 1) + '. ' + escapeHtml(stop.Name) + '</strong>');
                            }
                        });
                    }
                } else {
                    alert('Failed to delete stop.');
                }
            },
            error: function () { alert('Error deleting stop.'); }
        });
    });
});
