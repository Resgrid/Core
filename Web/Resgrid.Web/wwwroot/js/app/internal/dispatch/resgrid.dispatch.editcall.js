var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var editcall;
        (function (editcall) {
            var personnelTable, groupsTable, unitsTable, rolesTable;
            var initialDrawCount = 0;
            var totalTables = 4;
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
            editcall.protocolCount = 0;
            editcall.protocolData = {};
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

                $(document).on('submit', '#updateCallForm', function () {
                    if (quillNotes) {
                        $('#Call_Notes').val(quillNotes.root.innerHTML);
                    }
                    $('#Call_NatureOfCall').val(quillNature.root.innerHTML);

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
                    scheduleRecommendation();
                });

                $("#Call_Type").change(function () {
                    checkForProtocols();
                    scheduleRecommendation();
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

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetMapDataForCall?callId=' + callId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (result) {
                    // No map when GPS coordinates are hidden by the field policy.
                    if (result && document.getElementById('callMap')) {
                        var data = result;
                        const tiles1 = L.tileLayer(
                            osmTileUrl,
                            {
                                maxZoom: 19,
                                attribution: osmTileAttribution
                            }
                        );

                        map = L.map('callMap', {
                            scrollWheelZoom: false
                        }).setView([data.centerLat, data.centerLon], 11).addLayer(tiles1);

                        map.on('click', function (e) {
                            resgrid.dispatch.editcall.setMarkerLocation(e.latlng.lat.toString(), e.latlng.lng.toString());

                            $("#Latitude").val(e.latlng.lat.toString());
                            $("#Longitude").val(e.latlng.lng.toString());
                            map.panTo(e.latlng);

                            resgrid.dispatch.editcall.geocodeCoordinates(e.latlng.lat, e.latlng.lng);
                        });

                        resgrid.dispatch.editcall.setMarkerLocation(data.centerLat, data.centerLon);
                    }
                });

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
                            if (result && result.Data && result.Data.Latitude != null && result.Data.Longitude != null) {
                                var lat = result.Data.Latitude;
                                var lng = result.Data.Longitude;
                                if (map) {
                                    map.setView(new L.LatLng(lat, lng), 16);
                                }
                                $("#Latitude").val(lat.toString());
                                $("#Longitude").val(lng.toString());
                                resgrid.dispatch.editcall.setMarkerLocation(lat.toString(), lng.toString());
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
                        if (data && data.Latitude && data.Longitude) {
                            if (map) {
                                map.setView(new L.LatLng(data.Latitude, data.Longitude), 16);
                            }

                            $("#Latitude").val(data.Latitude);
                            $("#Longitude").val(data.Longitude);

                            resgrid.dispatch.editcall.geocodeCoordinates(data.Latitude, data.Longitude);

                            resgrid.dispatch.editcall.setMarkerLocation(data.Latitude, data.Longitude);
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
                    resgrid.dispatch.editcall.setMarkerLocation(lat.toString(), lng.toString());
                    evt.preventDefault();
                });

                $('#protocolQuestionWindow').on('show.bs.modal', function (event) {
                    var protocolId = $(event.relatedTarget).data('protocolid');

                    var protocol = null;
                    for (var i = 0; i < resgrid.dispatch.editcall.protocolData.length; i++) {
                        if (resgrid.dispatch.editcall.protocolData[i].Id === protocolId) {
                            protocol = resgrid.dispatch.editcall.protocolData[i];
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
                    for (var i = 0; i < resgrid.dispatch.editcall.protocolData.length; i++) {
                        if (resgrid.dispatch.editcall.protocolData[i].Id === Number(buttonProtocolId)) {
                            protocol = resgrid.dispatch.editcall.protocolData[i];
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
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + encodeURI($("#Latitude").val()) + '&callLong=' + encodeURI($("#Longitude").val()), dataSrc: '' },
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
                    applyRecommendationSelections();
                    initialDrawCount++;
                    if (initialDrawCount >= totalTables) { resgrid.dispatch.editcall.updateDispatchedEntities(); }
                });

                groupsTable = $("#groupsGrid").DataTable({
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
                    initialDrawCount++;
                    if (initialDrawCount >= totalTables) { resgrid.dispatch.editcall.updateDispatchedEntities(); }
                });

                unitsTable = $("#unitsGrid").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid?callLat=' + encodeURI($("#Latitude").val()) + '&callLong=' + encodeURI($("#Longitude").val()), dataSrc: '' },
                    paging: false,
                    columns: [
                        { data: 'UnitId', title: '', orderable: false, searchable: false, render: function(data) { return '<input type="checkbox" id="dispatchUnit_'+data+'" name="dispatchUnit_'+data+'" />'; } },
                        { data: 'Name', title: getText('name', 'Name') },
                        { data: 'Eta', title: getText('eta', 'ETA') },
                        { data: 'Type', title: getText('type', 'Type') },
                        { data: null, title: getText('status', 'Status'), orderable: false, render: function(d,t,row) { return unitStatusLabel(row); } }
                    ]
                });
                unitsTable.on('draw', function() {
                    $('#unitsGrid thead th:first').html('<label><input type="checkbox" id="checkAllUnits"/></label>');
                    applyRecommendationSelections();
                    initialDrawCount++;
                    if (initialDrawCount >= totalTables) { resgrid.dispatch.editcall.updateDispatchedEntities(); }
                });

                rolesTable = $("#rolesGrid").DataTable({
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
                    initialDrawCount++;
                    if (initialDrawCount >= totalTables) { resgrid.dispatch.editcall.updateDispatchedEntities(); }
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

                // A recommended resource the dispatcher unticks stays unticked across grid redraws.
                $('#unitsGrid, #personnelGrid').on('change', 'tbody :checkbox', function () {
                    if (this.checked) {
                        return;
                    }

                    var name = this.name || '';
                    if (name.indexOf('dispatchUnit_') === 0) {
                        appliedUnitIds = appliedUnitIds.filter(function (id) { return id !== name.substring('dispatchUnit_'.length); });
                    } else if (name.indexOf('dispatchUser_') === 0) {
                        appliedUserIds = appliedUserIds.filter(function (id) { return id !== name.substring('dispatchUser_'.length); });
                    }
                });

                $('#runCardPanel').on('click', '#runCardSelectRecommended', function (evt) {
                    evt.preventDefault();
                    selectRecommended();
                });
                $('#runCardPanel').on('click', '#runCardRefresh', function (evt) {
                    evt.preventDefault();
                    checkForRecommendation();
                });

                checkForProtocols();
                scheduleRecommendation();
            });

            // ── Run card recommendation for adding resources ──
            // What the call's run card still needs at its current alarm level, after the units and people already on it.
            // Advisory only: nothing is ticked until the dispatcher presses Select, so saving an unrelated edit never
            // dispatches extra resources. The ids it ticked are kept so a grid redraw re-applies them, and a sequence number
            // lets a slow earlier response be discarded instead of overwriting a newer one.
            var recommendationSequence = 0;
            var recommendationTimer = null;
            var currentRecommendation = null;
            var appliedUnitIds = [];
            var appliedUserIds = [];

            function prop(obj, name) {
                if (!obj) return undefined;
                if (obj[name] !== undefined) return obj[name];
                var pascal = name.charAt(0).toUpperCase() + name.slice(1);
                return obj[pascal];
            }

            function escapeText(text) {
                return $('<span>').text(text === null || text === undefined ? '' : String(text)).html();
            }

            function recommendedIds(result) {
                return {
                    units: (prop(result, 'units') || []).map(function (u) { return String(prop(u, 'unitId')); }),
                    users: (prop(result, 'personnel') || []).map(function (p) { return String(prop(p, 'userId')); }).filter(function (id) { return id && id !== 'undefined'; })
                };
            }

            function applyRecommendationSelections() {
                appliedUnitIds.forEach(function (id) {
                    $('input[name="dispatchUnit_' + id + '"]').prop('checked', true);
                });
                appliedUserIds.forEach(function (id) {
                    $('input[name="dispatchUser_' + id + '"]').prop('checked', true);
                });
            }
            editcall.applyRecommendationSelections = applyRecommendationSelections;

            function isRecommendationApplied(result) {
                var ids = recommendedIds(result);
                return (ids.units.length + ids.users.length) > 0 &&
                    ids.units.every(function (id) { return appliedUnitIds.indexOf(id) >= 0; }) &&
                    ids.users.every(function (id) { return appliedUserIds.indexOf(id) >= 0; });
            }

            function selectRecommended() {
                if (!currentRecommendation) {
                    return;
                }

                var ids = recommendedIds(currentRecommendation);
                ids.units.forEach(function (id) { if (appliedUnitIds.indexOf(id) < 0) appliedUnitIds.push(id); });
                ids.users.forEach(function (id) { if (appliedUserIds.indexOf(id) < 0) appliedUserIds.push(id); });

                applyRecommendationSelections();
                renderRecommendation(currentRecommendation);
            }
            editcall.selectRecommended = selectRecommended;

            function renderRecommendation(result) {
                var units = prop(result, 'units') || [];
                var personnel = prop(result, 'personnel') || [];
                var shortfalls = prop(result, 'shortfalls') || [];
                var notes = prop(result, 'notes') || [];
                var level = prop(result, 'alarmLevel') || 1;

                var html = '<strong>' + escapeText(prop(result, 'matchedRunCardName')) + '</strong>';
                if (level > 1) {
                    html += ' <span class="label label-warning">' + escapeText(formatText(getText('runCardAlarmLevel', 'Alarm {0}'), level)) + '</span>';
                }

                if (!units.length && !personnel.length && !shortfalls.length) {
                    html += '<div>' + escapeText(formatText(getText('runCardCovered', 'The resources on this call already cover the run card (alarm {0}).'), level)) + '</div>';
                }

                if (units.length) {
                    html += '<div><b>' + escapeText(getText('units', 'Units')) + ':</b> ' + units.map(function (u) {
                        var text = prop(u, 'unitName') || ('#' + prop(u, 'unitId'));
                        var distance = prop(u, 'distanceMeters');
                        if (distance) {
                            text += ' (' + (distance / 1000).toFixed(1) + ' km)';
                        }
                        return escapeText(text);
                    }).join(', ') + '</div>';
                }
                if (personnel.length) {
                    html += '<div><b>' + escapeText(getText('personnel', 'Personnel')) + ':</b> ' + personnel.map(function (p) {
                        return escapeText(prop(p, 'name') || prop(p, 'userId'));
                    }).join(', ') + '</div>';
                }
                if (shortfalls.length) {
                    html += '<div class="text-danger"><b>' + escapeText(getText('runCardShortfalls', 'Could not fill')) + ':</b> ' + shortfalls.map(function (sf) {
                        return escapeText((prop(sf, 'typeOrRoleName') || ('#' + prop(sf, 'typeOrRoleId'))) + ': ' + prop(sf, 'filledCount') + '/' + prop(sf, 'requiredCount'));
                    }).join(', ') + '</div>';
                }
                if (notes.length) {
                    html += '<div class="text-muted" style="font-size: 11px;">' + notes.map(function (n) { return escapeText(n); }).join('<br/>') + '</div>';
                }

                html += '<div style="margin-top: 6px;">';
                if (units.length || personnel.length) {
                    if (isRecommendationApplied(result)) {
                        html += '<span class="text-success"><i class="fa fa-check"></i> ' + escapeText(getText('runCardSelected', 'Selected: save the call to dispatch them')) + '</span> ';
                    } else {
                        html += '<button type="button" class="btn btn-xs btn-primary" id="runCardSelectRecommended">' + escapeText(getText('runCardSelectRecommended', 'Select recommended')) + '</button> ';
                    }
                }
                html += '<a href="#" id="runCardRefresh"><i class="fa fa-refresh"></i> ' + escapeText(getText('runCardRefresh', 'Refresh')) + '</a>';
                html += '</div>';

                $('#runCardPanel').html(html);
            }

            // Debounced: a map click sets the marker before it writes the coordinate fields.
            function scheduleRecommendation() {
                if (!$('#runCardPanel').length) {
                    return;
                }

                if (recommendationTimer) {
                    clearTimeout(recommendationTimer);
                }
                recommendationTimer = setTimeout(checkForRecommendation, 400);
            }
            editcall.scheduleRecommendation = scheduleRecommendation;

            function checkForRecommendation() {
                var panel = $('#runCardPanel');
                if (!panel.length) {
                    return;
                }

                var requestSequence = ++recommendationSequence;
                if (!currentRecommendation) {
                    panel.html(escapeText(getText('runCardChecking', 'Checking run cards...')));
                }

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallDispatchRecommendation',
                    data: {
                        callId: callId,
                        priority: $('#CallPriority').val(),
                        type: $('#Call_Type').val(),
                        latitude: $('#Latitude').val() || null,
                        longitude: $('#Longitude').val() || null
                    },
                    type: 'GET'
                }).done(function (response) {
                    if (requestSequence !== recommendationSequence) {
                        return;
                    }

                    var result = prop(response, 'result');
                    if (!response || !prop(response, 'success') || !result || !prop(result, 'matchedRunCardId')) {
                        // No run card applies to this call: stay out of the way.
                        currentRecommendation = null;
                        $('#runCardPanelRow').hide();
                        return;
                    }

                    currentRecommendation = result;
                    $('#runCardPanelRow').show();
                    renderRecommendation(result);
                }).fail(function () {
                    if (requestSequence !== recommendationSequence) {
                        return;
                    }

                    // A failed lookup must not read as "no run card applies"; the manual selection still works.
                    currentRecommendation = null;
                    $('#runCardPanelRow').show();
                    panel.html('<span class="text-warning">' + escapeText(getText('runCardLookupFailed', "Couldn't check run cards. You can still select resources by hand.")) +
                        '</span> <a href="#" id="runCardRefresh"><i class="fa fa-refresh"></i> ' + escapeText(getText('runCardRefresh', 'Refresh')) + '</a>');
                });
            }
            editcall.checkForRecommendation = checkForRecommendation;

            function getAuthToken() {
                return '';
            }
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
            editcall.findLocation = findLocation;

            function setMarkerLocation(lat, lng) {
                if (!map) {
                    scheduleRecommendation();
                    return;
                }

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

                        resgrid.dispatch.editcall.geocodeCoordinates(position.lat, position.lng);
                        scheduleRecommendation();
                    });
                }

                // Callers write #Latitude/#Longitude with .val(), which raises no change event.
                scheduleRecommendation();
            }
            editcall.setMarkerLocation = setMarkerLocation;
            function geocodeCoordinates(lat, lng) {
				fetch('/api/web-bff/api/v4/Geocoding/ReverseGeocode?lat=' + lat + '&lon=' + lng)
                    .then(function(r) {
                        if (!r.ok) throw new Error('ReverseGeocode failed with HTTP status ' + r.status);
                        return r.json();
                    })
                    .then(function(result) {
                        if (result && result.Data && result.Data.Address && !userSuppliedAddress) {
                            $("#Call_Address").val(result.Data.Address);
                        }
                    })
                    .catch(function(err) { console.error("Reverse geocode error:", err); });
            }
            editcall.geocodeCoordinates = geocodeCoordinates;

            function refreshPersonnelGrid() {
                personnelTable.ajax.url(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + encodeURI($("#Latitude").val()) + '&callLong=' + encodeURI($("#Longitude").val())).load();
                unitsTable.ajax.url(resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid?callLat=' + encodeURI($("#Latitude").val()) + '&callLong=' + encodeURI($("#Longitude").val())).load();
            }
            editcall.refreshPersonnelGrid = refreshPersonnelGrid;

            function updateDispatchedEntities() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetAllDispatchesForCall?callId=' + callId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (result) {
                    for (var i = 0; i < result.length; i++) {
                        $(result[i].DisptachCode).prop('checked', true);
                    }
                });
            }
            editcall.updateDispatchedEntities = updateDispatchedEntities;

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
                            // handlers for the template's values.
                            checkForProtocols();
                            scheduleRecommendation();
                        }
                    });
                }
            }
            editcall.fillCallTemplate = fillCallTemplate;
            // Bound here rather than inline: the button renders disabled until this script has
            // run, so an early click can't call into an undefined namespace (RESGRID-WEB-1MA).
            $('#setCallTemplateButton').on('click', fillCallTemplate).prop('disabled', false);

            // Type, priority and template changes can overlap; only the latest request's protocols are shown.
            var protocolsRequest = 0;
            function checkForProtocols() {
                var callPriorityVal = $('#CallPriority').val();
                var callTypeVal = $('#Call_Type').val();
                var request = ++protocolsRequest;

                $("#protocols tr").remove();

                $.ajax({
                    url: resgrid.absoluteBaseUrl + `/User/Protocols/GetProtocolsForPrioType?priority=${callPriorityVal}&type=${callTypeVal}`,
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (request !== protocolsRequest)
                        return;

                    if (data) {
                        resgrid.dispatch.editcall.protocolCount = 0;

                        resgrid.dispatch.editcall.protocolData = data;
                        for (var i = 0; i < data.length; i++) {
                            var pendingProtocol = data[i];

                            if (pendingProtocol.State === 1 || pendingProtocol.State === 2) {
                                resgrid.dispatch.editcall.addProtocol(pendingProtocol.Id, pendingProtocol.Name, pendingProtocol.Code, pendingProtocol.State);
                            }
                        }
                    }
                });
            }
            editcall.checkForProtocols = checkForProtocols;

            function addProtocol(id, name, code, state) {
                resgrid.dispatch.editcall.protocolCount++;
                $('#protocols tbody').first().append(`<tr>
                    <td style='max-width: 50px;'>${code}</td>
                    <td>${name}</td>
                    <td>${resgrid.dispatch.editcall.getStatusField(id, state, code)}</td>
                </tr>`);
            }
            editcall.addProtocol = addProtocol;

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
            editcall.getStatusField = getStatusField;

        })(editcall = dispatch.editcall || (dispatch.editcall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
