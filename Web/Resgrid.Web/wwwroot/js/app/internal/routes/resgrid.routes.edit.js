$(document).ready(function () {
    var map, stopPickerMap, stopMarker, pickerMarker;
    var startPickerMap, startMarker, startPickerInitialized = false;
    var endPickerMap, endMarker, endPickerInitialized = false;
    var antiForgeryToken = $('input[name="__RequestVerificationToken"]').first().val();

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

    // The stop list, stop markers and live route preview (resgrid.routes.editor.js). Stops here are saved: reorder,
    // move and delete go to the server straight away, which regenerates the stored route.
    var stopEditor = resgrid.routes.editor;
    var editorSettings = window.routeEditorSettings || { labels: {} };
    var orderTimer = null;

    function saveOrder() {
        $.ajax({
            url: editorSettings.applyUrl,
            type: 'POST',
            traditional: true,
            data: { __RequestVerificationToken: antiForgeryToken, id: routePlanId, stopIds: editStops.map(function (s) { return s.id; }) }
        }).done(function (result) {
            if (!result || !result.success) {
                alert(editorSettings.labels.stopOrderFailed);
                location.reload();
            }
        }).fail(function () {
            alert(editorSettings.labels.stopOrderFailed);
            location.reload();
        });
    }

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
            initStartPickerMap(
                (typeof existingStartLat !== 'undefined' && existingStartLat != null) ? existingStartLat : null,
                (typeof existingStartLng !== 'undefined' && existingStartLng != null) ? existingStartLng : null
            );
        } else {
            $('#startLocationGroup').hide();
            $('#startLat').val('').prop('disabled', true);
            $('#startLng').val('').prop('disabled', true);
        }
        if (!$('#chkUseStationAsEnd').prop('checked')) {
            $('#endLocationGroup').show();
            $('#endLat').prop('disabled', false);
            $('#endLng').prop('disabled', false);
            initEndPickerMap(
                (typeof existingEndLat !== 'undefined' && existingEndLat != null) ? existingEndLat : null,
                (typeof existingEndLng !== 'undefined' && existingEndLng != null) ? existingEndLng : null
            );
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

    // ── Address geocoding (Enter key or button) ───────────────────────────────
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
                geofenceRadius:   $('#stopGeofenceRadius').val() || null,
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

    stopEditor.init({
        mapReady: mapReady,
        planId: routePlanId,
        getStops: function () {
            return editStops.map(function (s) {
                return { key: s.id, name: s.name, stopType: s.stopType, priority: s.priority, address: s.address, lat: s.lat, lng: s.lng, dwellMinutes: s.dwellMinutes };
            });
        },
        // Reordered locally at once; saved after a short pause so several moves make one save.
        onReorder: function (keys) {
            editStops = keys.map(function (key) {
                return editStops.filter(function (s) { return s.id === key; })[0];
            }).filter(Boolean);
            stopEditor.refresh();
            if (orderTimer) clearTimeout(orderTimer);
            orderTimer = setTimeout(saveOrder, 800);
        },
        onDelete: function (stopId) {
            if (!confirm('Delete this stop?')) return;
            $.ajax({
                url: resgrid.absoluteBaseUrl + '/User/Routes/DeleteStop',
                type: 'POST',
                data: {
                    __RequestVerificationToken: antiForgeryToken,
                    stopId: stopId
                },
                success: function (result) {
                    if (result.success) {
                        editStops = editStops.filter(function (s) { return s.id !== stopId; });
                        stopEditor.refresh();
                    } else {
                        alert('Failed to delete stop.');
                    }
                },
                error: function () { alert('Error deleting stop.'); }
            });
        },
        onMove: function (stopId, lat, lng) {
            editStops.forEach(function (s) {
                if (s.id === stopId) {
                    s.lat = lat;
                    s.lng = lng;
                }
            });
            stopEditor.refresh();
            $.ajax({
                url: editorSettings.moveUrl,
                type: 'POST',
                data: { __RequestVerificationToken: antiForgeryToken, stopId: stopId, latitude: lat.toFixed(6), longitude: lng.toFixed(6) }
            }).done(function (result) {
                if (!result || !result.success) {
                    alert(editorSettings.labels.stopMoveFailed);
                    location.reload();
                }
            }).fail(function () {
                alert(editorSettings.labels.stopMoveFailed);
                location.reload();
            });
        }
    });
});
