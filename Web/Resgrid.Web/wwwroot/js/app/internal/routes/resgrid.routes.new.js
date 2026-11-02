$(document).ready(function () {
    var map, stopPickerMap, pickerMarker;
    var startPickerMap, startMarker, startPickerInitialized = false;
    var endPickerMap, endMarker, endPickerInitialized = false;
    var pendingStops = [];
    var stopIdCounter = 0;

    function getAuthToken() {
        return '';
    }

    // ── Main route map ────────────────────────────────────────────────────────
    var mapReady = document.getElementById('routeMap')
        ? resgrid.maps.create('routeMap', { center: [39.8283, -98.5795], zoom: 4, scrollZoom: false, traffic: 'off' }).then(function (created) {
            map = created;
            return created;
        })
        : null;

    // The stop list, stop markers and live route preview (resgrid.routes.editor.js); stops stay in this page until Save.
    var stopEditor = resgrid.routes.editor;

    // ── Stop picker map helpers (resgrid.maps: MapLibre or Leaflet by the department's Maps.MapLibreWeb flag) ──
    var stopPickerReady = null, startPickerReady = null, endPickerReady = null;

    function createPicker(id, onPick) {
        return resgrid.maps.create(id, { center: [39.8283, -98.5795], zoom: 4, scrollZoom: false, hydrants: false }).then(function (created) {
            created.onClick(function (position) { onPick(position[0], position[1]); });
            return created;
        });
    }

    function initStopPickerMap() {
        if (stopPickerReady) {
            stopPickerReady.then(function (pickerMap) { pickerMap.resize(); });
            return stopPickerReady;
        }
        stopPickerReady = createPicker('stopPickerMap', function (lat, lng) { setPickerLocation(lat, lng, true); }).then(function (created) {
            stopPickerMap = created;
            return created;
        });
        return stopPickerReady;
    }

    function setPickerLocation(lat, lng, reverseGeocode) {
        lat = Number(lat);
        lng = Number(lng);
        $('#stopLat').val(lat.toFixed(6));
        $('#stopLng').val(lng.toFixed(6));
        initStopPickerMap().then(function (pickerMap) {
            if (pickerMarker) {
                pickerMarker.setLatLng([lat, lng]);
            } else {
                pickerMarker = pickerMap.addMarker([lat, lng], { draggable: true });
                pickerMarker.onDragEnd(function (pos) {
                    pickerMap.setView(pos);
                    $('#stopLat').val(pos[0].toFixed(6));
                    $('#stopLng').val(pos[1].toFixed(6));
                    reverseGeocodeStop(pos[0], pos[1]);
                });
            }
            pickerMap.setView([lat, lng], 15);
        });
        if (reverseGeocode) {
            reverseGeocodeStop(lat, lng);
        }
    }

    function reverseGeocodeStop(lat, lng) {
		fetch('/api/web-bff/api/v4/Geocoding/ReverseGeocode?lat=' + lat + '&lon=' + lng)
			.then(function (r) {
				if (!r.ok) throw new Error('ReverseGeocode failed with HTTP status ' + r.status);
				return r.json();
			})
            .then(function (result) {
                if (result && result.Data && result.Data.Address) {
                    $('#stopAddress').val(result.Data.Address);
                }
            })
			.catch(function (err) {
				console.error('ReverseGeocode failed', { operation: 'ReverseGeocode', latitude: lat, longitude: lng, error: err });
			});
    }

    // ── Start/End location pickers ────────────────────────────────────────────
    function hasPoint(lat, lng) {
        return lat != null && lng != null && lat !== '' && lng !== '' && Number.isFinite(Number(lat)) && Number.isFinite(Number(lng));
    }

    function initStartPickerMap(initialLat, initialLng) {
        if (startPickerReady) {
            setTimeout(function () { startPickerReady.then(function (pickerMap) { pickerMap.resize(); }); }, 50);
            return;
        }
        startPickerReady = createPicker('startPickerMap', setStartLocation).then(function (created) {
            startPickerMap = created;
            return created;
        });
        startPickerInitialized = true;
        if (hasPoint(initialLat, initialLng)) { setStartLocation(initialLat, initialLng); }
    }

    function setStartLocation(lat, lng) {
        var latStr = parseFloat(lat).toFixed(6);
        var lngStr = parseFloat(lng).toFixed(6);
        $('#startLat').val(latStr);
        $('#startLng').val(lngStr);
        $('#startLatDisplay').val(latStr);
        $('#startLngDisplay').val(lngStr);
        stopEditor.refresh();
        if (!startPickerReady) {
            return;
        }
        startPickerReady.then(function (pickerMap) {
            var position = [Number(lat), Number(lng)];
            if (startMarker) {
                startMarker.setLatLng(position);
            } else {
                startMarker = pickerMap.addMarker(position, { draggable: true });
                startMarker.onDragEnd(function (pos) { setStartLocation(pos[0], pos[1]); });
            }
            pickerMap.setView(position, 15);
        });
    }

    function initEndPickerMap(initialLat, initialLng) {
        if (endPickerReady) {
            setTimeout(function () { endPickerReady.then(function (pickerMap) { pickerMap.resize(); }); }, 50);
            return;
        }
        endPickerReady = createPicker('endPickerMap', setEndLocation).then(function (created) {
            endPickerMap = created;
            return created;
        });
        endPickerInitialized = true;
        if (hasPoint(initialLat, initialLng)) { setEndLocation(initialLat, initialLng); }
    }

    function setEndLocation(lat, lng) {
        var latStr = parseFloat(lat).toFixed(6);
        var lngStr = parseFloat(lng).toFixed(6);
        $('#endLat').val(latStr);
        $('#endLng').val(lngStr);
        $('#endLatDisplay').val(latStr);
        $('#endLngDisplay').val(lngStr);
        stopEditor.refresh();
        if (!endPickerReady) {
            return;
        }
        endPickerReady.then(function (pickerMap) {
            var position = [Number(lat), Number(lng)];
            if (endMarker) {
                endMarker.setLatLng(position);
            } else {
                endMarker = pickerMap.addMarker(position, { draggable: true });
                endMarker.onDragEnd(function (pos) { setEndLocation(pos[0], pos[1]); });
            }
            pickerMap.setView(position, 15);
        });
    }

    function updateLocationPickerVisibility() {
        if (!$('#chkUseStationAsStart').prop('checked')) {
            $('#startLocationGroup').show();
            $('#startLat').prop('disabled', false);
            $('#startLng').prop('disabled', false);
            initStartPickerMap();
        } else {
            $('#startLocationGroup').hide();
            $('#startLat').val('').prop('disabled', true);
            $('#startLng').val('').prop('disabled', true);
        }
        if (!$('#chkUseStationAsEnd').prop('checked')) {
            $('#endLocationGroup').show();
            $('#endLat').prop('disabled', false);
            $('#endLng').prop('disabled', false);
            initEndPickerMap();
        } else {
            $('#endLocationGroup').hide();
            $('#endLat').val('').prop('disabled', true);
            $('#endLng').val('').prop('disabled', true);
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
		fetch('/api/web-bff/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where))
			.then(function (r) {
				if (!r.ok) throw new Error('ForwardGeocode failed with HTTP status ' + r.status);
				return r.json();
			})
            .then(function (result) {
                if (result && result.Data && result.Data.Latitude != null && result.Data.Longitude != null) {
                    setStartLocation(result.Data.Latitude, result.Data.Longitude);
                } else { alert('Address not found.'); }
            })
			.catch(function (err) {
				console.error('ForwardGeocode failed', { operation: 'ForwardGeocode', address: where, error: err });
			});
    });

    // Start – What3Words
    $('#startW3W').on('keypress', function (e) { if (e.keyCode === 13) { $('#findStartW3WBtn').click(); return false; } });
    $('#findStartW3WBtn').on('click', function (evt) {
        evt.preventDefault();
        var word = $.trim($('#startW3W').val());
        if (!word) return;
        $.ajax({ url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCoordinatesFromW3W?words=' + encodeURIComponent(word), type: 'GET' })
            .done(function (data) {
                if (data && data.Latitude != null && data.Longitude != null) { setStartLocation(data.Latitude, data.Longitude); }
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
		fetch('/api/web-bff/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where))
			.then(function (r) {
				if (!r.ok) throw new Error('ForwardGeocode failed with HTTP status ' + r.status);
				return r.json();
			})
            .then(function (result) {
                if (result && result.Data && result.Data.Latitude != null && result.Data.Longitude != null) {
                    setEndLocation(result.Data.Latitude, result.Data.Longitude);
                } else { alert('Address not found.'); }
            })
			.catch(function (err) {
				console.error('ForwardGeocode failed', { operation: 'ForwardGeocode', address: where, error: err });
			});
    });

    // End – What3Words
    $('#endW3W').on('keypress', function (e) { if (e.keyCode === 13) { $('#findEndW3WBtn').click(); return false; } });
    $('#findEndW3WBtn').on('click', function (evt) {
        evt.preventDefault();
        var word = $.trim($('#endW3W').val());
        if (!word) return;
        $.ajax({ url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCoordinatesFromW3W?words=' + encodeURIComponent(word), type: 'GET' })
            .done(function (data) {
                if (data && data.Latitude != null && data.Longitude != null) { setEndLocation(data.Latitude, data.Longitude); }
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

    // Initialise visibility on page load
    updateLocationPickerVisibility();

    stopEditor.init({
        mapReady: mapReady,
        getStops: function () {
            return pendingStops.map(function (s) {
                return { key: s._clientId, name: s.name, stopType: s.stopType, priority: s.priority, address: s.address, lat: s.latitude, lng: s.longitude, dwellMinutes: s.dwellMinutes };
            });
        },
        onReorder: function (keys) {
            pendingStops = keys.map(function (key) {
                return pendingStops.filter(function (s) { return String(s._clientId) === key; })[0];
            }).filter(Boolean);
            stopEditor.refresh();
        },
        onDelete: function (key) {
            if (!confirm('Remove this stop?')) return;
            pendingStops = pendingStops.filter(function (s) { return String(s._clientId) !== key; });
            stopEditor.refresh();
        },
        onMove: function (key, lat, lng) {
            pendingStops.forEach(function (s) {
                if (String(s._clientId) === key) {
                    s.latitude = lat;
                    s.longitude = lng;
                }
            });
            stopEditor.refresh();
        }
    });

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
            if (!$('#stopContactName').val()) $('#stopContactName').val(contact.name);
            else $('#stopContactName').val(contact.name);
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
        $('#stopGeofenceRadius').val('');
        $('#stopContactPicker').val('');
        $('#stopContactId').val('');
        $('#stopContactName').val('');
        $('#stopContactNumber').val('');
        $('#stopNotes').val('');
        if (pickerMarker) {
            pickerMarker.remove();
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

    // ── Address geocoding ─────────────────────────────────────────────────────
    $('#stopAddress').on('keypress', function (e) {
        if (e.keyCode === 13) { $('#geocodeStopBtn').click(); return false; }
    });

    $('#geocodeStopBtn').on('click', function (evt) {
        evt.preventDefault();
        var where = $.trim($('#stopAddress').val());
        if (!where) return;
		fetch('/api/web-bff/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where))
			.then(function (r) {
				if (!r.ok) throw new Error('ForwardGeocode failed with HTTP status ' + r.status);
				return r.json();
			})
            .then(function (result) {
                if (result && result.Data && result.Data.Latitude != null && result.Data.Longitude != null) {
                    setPickerLocation(result.Data.Latitude, result.Data.Longitude, false);
                } else {
                    alert('Address not found.');
                }
            })
			.catch(function (err) {
				console.error('ForwardGeocode failed', { operation: 'ForwardGeocode', address: where, error: err });
			});
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

    // ── Add stop (client-side only) ───────────────────────────────────────────
    $('#saveStopBtn').on('click', function () {
        var name = $.trim($('#stopName').val());
        var lat  = $('#stopLat').val();
        var lng  = $('#stopLng').val();

        if (!name) { alert('Stop name is required.'); return; }
        if (!lat || !lng) { alert('Please select a location on the map, or use the address / What3Words finder.'); return; }

        var stopType = parseInt($('#stopType').val());
        var callId   = (stopType === 1) ? ($('#linkedCallId').val() || null) : null;

        pendingStops.push({
            _clientId:        ++stopIdCounter,
            name:             name,
            description:      '',
            stopType:         stopType,
            priority:         parseInt($('#stopPriority').val()),
            latitude:         parseFloat(lat),
            longitude:        parseFloat(lng),
            address:          $('#stopAddress').val(),
            callId:           callId,
            plannedArrival:   $('#stopPlannedArrival').val(),
            plannedDeparture: $('#stopPlannedDeparture').val(),
            dwellMinutes:     $('#stopDwellMinutes').val() || null,
            geofenceRadius:   $('#stopGeofenceRadius').val() || null,
            contactName:      $('#stopContactName').val(),
            contactNumber:    $('#stopContactNumber').val(),
            contactId:        $('#stopContactId').val() || null,
            notes:            $('#stopNotes').val()
        });

        stopEditor.refresh();
        $('#addStopModal').modal('hide');
    });

    // ── Serialize stops into hidden field before form submit ──────────────────
    $('form').on('submit', function () {
        var stopsToSubmit = pendingStops.map(function (s) {
            return {
                name:             s.name,
                description:      s.description,
                stopType:         s.stopType,
                priority:         s.priority,
                latitude:         s.latitude,
                longitude:        s.longitude,
                address:          s.address,
                callId:           s.callId,
                plannedArrival:   s.plannedArrival,
                plannedDeparture: s.plannedDeparture,
                dwellMinutes:     s.dwellMinutes,
                geofenceRadius:   s.geofenceRadius,
                contactName:      s.contactName,
                contactNumber:    s.contactNumber,
                contactId:        s.contactId,
                notes:            s.notes
            };
        });
        $('#PendingStopsJson').val(JSON.stringify(stopsToSubmit));
    });
});
