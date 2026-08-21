
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
                userSuppliedAddress = false;
                resgrid.dispatch.newcall.protocolCount = 0;
                resgrid.dispatch.newcall.protocolData = {};

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

                    userSuppliedAddress = true;
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
                });

                let noteQuillDescription = new Quill('#note-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newCallForm', function () {
                    $('#Call_Notes').val(noteQuillDescription.root.innerHTML);
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

                const tiles1 = L.tileLayer(
                    osmTileUrl,
                    {
                        maxZoom: 19,
                        attribution: osmTileAttribution
                    }
                );

                map = L.map('callMap', {
                    scrollWheelZoom: false
                }).setView([centerLat, centerLng], 11).addLayer(tiles1);

                map.on('click', function (e) {
                    resgrid.dispatch.newcall.setMarkerLocation(e.latlng.lat, e.latlng.lng);

                    $("#Latitude").val(e.latlng.lat.toString());
                    $("#Longitude").val(e.latlng.lng.toString());
                    //$("#What3Word").val('');

                    map.panTo(e.latlng);

                    resgrid.dispatch.newcall.geocodeCoordinates(e.latlng.lat, e.latlng.lng);
                });

                navigator.geolocation.getCurrentPosition(foundLocation, noLocation, { timeout: 10000 });
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
                                map.setView(new L.LatLng(lat, lng), 16);
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
                            map.setView(new L.LatLng(data.Latitude, data.Longitude), 16);

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
                    map.setView(new L.LatLng(lat, lng), 16);
                    resgrid.dispatch.newcall.setMarkerLocation(lat, lng);
                    evt.preventDefault();
                });
                var personnelTable = $("#personnelGrid").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val(), dataSrc: '' },
                    paging: false,
                    columns: [
                        { data: 'UserId', title: '', orderable: false, searchable: false, render: function(data) { return '<input type="checkbox" id="dispatchUser_'+data+'" name="dispatchUser_'+data+'" />'; } },
                        { data: 'Name', title: getText('name', 'Name') },
                        { data: 'Eta', title: getText('eta', 'ETA') },
                        { data: null, title: getText('status', 'Status'), orderable: false, render: function(d,t,row) { return '<span style="color:'+row.StatusColor+'">'+row.Status+'</span>'; } },
                        { data: null, title: getText('staffing', 'Staffing'), orderable: false, render: function(d,t,row) { return '<span style="color:'+row.StaffingColor+'">'+row.Staffing+'</span>'; } },
                        { data: 'Group', title: getText('group', 'Group') },
                        { data: 'Roles', title: getText('roles', 'Roles') }
                    ]
                });
                personnelTable.on('draw', function() {
                    $('#personnelGrid thead th:first').html('<label><input type="checkbox" id="checkAllPersonnel"/></label>');

                    // The rows are new DOM on every draw, so any run card recommendation has
                    // to be re-applied here rather than only when the response arrives.
                    applyRecommendationSelections();
                });

                var groupsTable = $("#groupsGrid").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Groups/GetGroupsForCallGrid', dataSrc: '' },
                    paging: false,
                    columns: [
                        { data: 'GroupId', title: '', orderable: false, searchable: false, render: function(data) { return '<input type="checkbox" id="dispatchGroup_'+data+'" name="dispatchGroup_'+data+'" />'; } },
                        { data: 'Name', title: getText('name', 'Name') },
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
                        { data: 'Name', title: getText('name', 'Name') },
                        { data: 'Count', title: getText('personnelCount', 'Personnel Count') }
                    ]
                });
                rolesTable.on('draw', function() {
                    $('#rolesGrid thead th:first').html('<label><input type="checkbox" id="checkAllRoles"/></label>');
                });

                $('#protocolQuestionWindow').on('show.bs.modal', function (event) {
                    var protocolId = $(event.relatedTarget).data('protocolid');
                    //var protocolId = button.data('protocolId');

                    var protocol = null;

                    for (var i = 0; i < resgrid.dispatch.newcall.protocolData.length; i++) {
                        if (resgrid.dispatch.newcall.protocolData[i].Id === protocolId) {
                            protocol = resgrid.dispatch.newcall.protocolData[i];
                            break;
                        }
                    }

                    var modal = $(this);
                    modal.find('.modal-title').text(formatText(getText('questionsFor', 'Questions for {0}'), protocol.Name));

                    var questionHtml = "";
                    for (var t = 0; t < protocol.Questions.length; t++) {
                        var question = protocol.Questions[t];
                        questionHtml = questionHtml + `<div class="form-group"><label class=" control-label">${question.Question}</label><div class="controls"><select id="questionAnswer_${question.Id}" name="questionAnswer_${question.Id}">`;

                        for (var r = 0; r < protocol.Questions[t].Answers.length; r++) {
                            var answer = protocol.Questions[t].Answers[r];

                            if (r === 0) {
                                questionHtml = questionHtml + `<option selected="selected" value="${answer.Weight}">${answer.Answer}</option>`;
                            } else {
                                questionHtml = questionHtml + `<option value="${answer.Weight}">${answer.Answer}</option>`;
                            }
                        }

                        questionHtml = questionHtml + '</select></div></div>';
                    }
                    modal.find('.modal-body').empty();
                    modal.find('.modal-body').append(questionHtml);

                    $('#processQuestionAnswers').removeAttr("data-protocolid");
                    $('#processQuestionAnswers').attr('data-protocolid', protocol.Id);
                });

                $('#processQuestionAnswers').click(function () {
                    var buttonProtocolId = $('#processQuestionAnswers').attr('data-protocolid');
                    $('#protocolQuestionWindow').modal('hide');

                    var protocol = null;
                    for (var i = 0; i < resgrid.dispatch.newcall.protocolData.length; i++) {
                        if (resgrid.dispatch.newcall.protocolData[i].Id === Number(buttonProtocolId)) {
                            protocol = resgrid.dispatch.newcall.protocolData[i];
                            break;
                        }
                    }

                    var totalAnswerWeight = 0;
                    for (var t = 0; t < protocol.Questions.length; t++) {
                        var question = protocol.Questions[t];

                        var answerWeight = $(`#questionAnswer_${question.Id}`).val();

                        if (answerWeight) {
                            totalAnswerWeight = totalAnswerWeight + Number(answerWeight);
                        }
                    }

                    $(`#answerProcotolQuestions_${protocol.Id}`).removeClass("btn-warning btn-success btn-inverse");

                    if (totalAnswerWeight >= protocol.MinimumWeight) {
                        $(`#pendingProtocol_${protocol.Id}`).val('1');
                        $(`#answerProcotolQuestions_${protocol.Id}`).addClass("btn-success");
                    } else {
                        $(`#answerProcotolQuestions_${protocol.Id}`).addClass("btn-inverse");
                    }
                });

                $('#addNewLinkedCall').click(function () {
                    var data = $('#selectLinkedCall').select2('data');

                    $('#linkedCalls tbody').first().append(`<tr><td style='max-width: 215px;'>${data[0].text}<input type='hidden' id='linkedCall_${data[0].id}' name='linkedCall_${data[0].id}' value='${data[0].id}' /></td><td>${$('#selectCallNote').val()}<input type='hidden' id='linkedCallNote_${data[0].id}' name='linkedCallNote_${data[0].id}' value='${$('#selectCallNote').val()}' /></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='${getText('removeThisCallLink', 'Remove this call link')}'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>`);
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

                                if (data.CallPriority && data.CallPriority >= 0) {
                                    $('#CallPriority').val(data.CallPriority);
                                }

                                $('input[name="Call.CheckInTimersEnabled"]').prop('checked', !!data.CheckInTimersEnabled);
                            }
                        });
                    }
                }
                newcall.fillCallTemplate = fillCallTemplate;

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
                if (centerLat && centerLng) {
                    map.panTo(new L.LatLng(centerLat, centerLng));
                }
            }
            newcall.centerMap = centerMap;
            function foundLocation(position) {
                map.panTo(new L.LatLng(position.coords.latitude, position.coords.longitude));
            }
            newcall.foundLocation = foundLocation;
            function noLocation() {
                // Browser geolocation was denied or unavailable. Fall back to the department's
                // configured map centre -- this used to pan to a hardcoded coordinate in Wollongong,
                // Australia, which every department outside NSW saw as "the map is in the wrong place".
                if (centerLat && centerLng) {
                    map.panTo(new L.LatLng(centerLat, centerLng));
                }
            }
            newcall.noLocation = noLocation;
            function setMarkerLocation(lat, lng) {
                if (callMarker) {
                    callMarker.setLatLng(new L.LatLng(lat, lng));
                } else {
                    callMarker = new L.marker(new L.LatLng(lat, lng), { draggable: 'true' }).addTo(map);
                    callMarker.on('dragend', function (event) {
                        var marker = event.target;
                        var position = marker.getLatLng();
                        marker.setLatLng(new L.LatLng(position.lat, position.lng), { draggable: 'true' });
                        map.panTo(new L.LatLng(position.lat, position.lng));

                        $("#Latitude").val(position.lat);
                        $("#Longitude").val(position.lng);
                        //$("#What3Word").val('');

                        resgrid.dispatch.newcall.geocodeCoordinates(position.lat, position.lng);
                    });
                }
            }
            newcall.setMarkerLocation = setMarkerLocation;
            function getAuthToken() {
                return '';
            }
            function geocodeCoordinates(lat, lng) {
				fetch('/api/web-bff/api/v4/Geocoding/ReverseGeocode?lat=' + lat + '&lon=' + lng)
                    .then(function(r) { return r.json(); })
                    .then(function(result) {
                        if (result && result.Data && result.Data.Address && !userSuppliedAddress) {
                            $("#Call_Address").val(result.Data.Address);
                        }
                    })
                    .catch(function(err) { console.error("Reverse geocode error:", err); });
            }
            newcall.geocodeCoordinates = geocodeCoordinates;
            function findLocation(pos) {
				fetch('/api/web-bff/api/v4/Geocoding/ReverseGeocode?lat=' + pos.lat + '&lon=' + pos.lng)
                    .then(function(r) { return r.json(); })
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
            function refreshPersonnelGrid() {
                personnelTable.ajax.url(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val()).load();
            }
            newcall.refreshPersonnelGrid = refreshPersonnelGrid;
            function checkAllUnits(gridName, item) {
                $('#' + gridName).find(':checkbox').prop('checked', item.checked);
            }
            newcall.checkAllUnits = checkAllUnits;
            function checkForProtocols() {
                var callPriorityVal = $('#CallPriority').val();
                var callTypeVal = $('#Call_Type').val();

                $("#protocols tr").remove();

                $.ajax({
                    url: resgrid.absoluteBaseUrl + `/User/Protocols/GetProtocolsForPrioType?priority=${callPriorityVal}&type=${callTypeVal}`,
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        resgrid.dispatch.newcall.protocolCount = 0;

                        if (data) {
                            resgrid.dispatch.newcall.protocolData = data;
                            for (var i = 0; i < data.length; i++) {
                                var pendingProtocol = data[i];

                                if (pendingProtocol.State === 1 || pendingProtocol.State === 2) {
                                    resgrid.dispatch.newcall.addProtocol(pendingProtocol.Id, pendingProtocol.Name, pendingProtocol.Code, pendingProtocol.State);
                                }

                            }
                        }
                    }
                });
            }
            newcall.checkForProtocols = checkForProtocols;
            function addProtocol(id, name, code, state) {
                resgrid.dispatch.newcall.protocolCount++;
                $('#protocols tbody').first().append(`<tr>
					<td style='max-width: 50px;'>${code}</td>
					<td>${name}</td>"
					<td>${resgrid.dispatch.newcall.getStatusField(id, state, code)}</td>"
				</tr>`);
            }
            newcall.addProtocol = addProtocol;

            function getStatusField(id, state, code) {
                var inactiveText = resgrid.dispatch.getText('inactive', 'Inactive');
                var activeText = resgrid.dispatch.getText('active', 'Active');
                var answerQuestionsText = resgrid.dispatch.getText('answerQuestions', 'Answer Questions');
                var unknownText = resgrid.dispatch.getText('unknown', 'Unknown');
                if (state === 0) {
                    return inactiveText;
                } else if (state === 1) {
                    return `${activeText} <input type='text' id='activeProtocol_${id}' name='activeProtocol_${id}' style='display:none;' value='1'></input><input type='text' id='protocolCode_${id}' name='protocolCode_${id}' style='display:none;' value='${code}'></input>`;
                } else if (state === 2) {
                    return `<a id="answerProcotolQuestions_${id}" class="btn btn-warning btn-xs" data-toggle="modal" data-target="#protocolQuestionWindow" data-protocolId="${id}">${answerQuestionsText}</a> <input type='text' id='pendingProtocol_${id}' name='pendingProtocol_${id}' style='display:none;' value='0'></input><input type='text' id='protocolCode_${id}' name='protocolCode_${id}' style='display:none;' value='${code}'></input>`;
                } else {
                    return unknownText;
                }
            }
            newcall.getStatusField = getStatusField;

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
            var recommendedUnitIds = [];
            var recommendedUserIds = [];

            function clearRecommendationSelections() {
                recommendedUnitIds.forEach(function (id) {
                    $('input[name="dispatchUnit_' + id + '"]').prop('checked', false);
                });
                recommendedUserIds.forEach(function (id) {
                    $('input[name="dispatchUser_' + id + '"]').prop('checked', false);
                });

                recommendedUnitIds = [];
                recommendedUserIds = [];
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
            }
            newcall.applyRecommendationSelections = applyRecommendationSelections;

            function checkForRecommendations() {
                var callPriorityVal = $('#CallPriority').val();
                var callTypeVal = $('#Call_Type').val();
                var lat = $('#Latitude').val();
                var lon = $('#Longitude').val();
                var requestSequence = ++recommendationSequence;

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

                    var autoDispatch = prop(result, 'autoDispatch') === true;
                    var units = prop(result, 'units') || [];
                    var personnel = prop(result, 'personnel') || [];
                    var shortfalls = prop(result, 'shortfalls') || [];
                    var notes = prop(result, 'notes') || [];

                    var html = '<strong>' + $('<span>').text(prop(result, 'matchedRunCardName') || '').html() + '</strong>';
                    if (autoDispatch) {
                        html += ' <span class="label label-warning">Auto-dispatch is ON — recommended resources will be dispatched automatically on save.</span>';
                    }

                    if (units.length) {
                        html += '<div><b>Units:</b> ' + units.map(function (u) {
                            var text = prop(u, 'unitName') || ('#' + prop(u, 'unitId'));
                            var distance = prop(u, 'distanceMeters');
                            if (distance) text += ' (' + (distance / 1000).toFixed(1) + ' km)';
                            return $('<span>').text(text).html();
                        }).join(', ') + '</div>';
                    }
                    if (personnel.length) {
                        html += '<div><b>Personnel:</b> ' + personnel.length + ' recommended</div>';
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

            checkForProtocols();
            checkForRecommendations();
        })(newcall = dispatch.newcall || (dispatch.newcall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
