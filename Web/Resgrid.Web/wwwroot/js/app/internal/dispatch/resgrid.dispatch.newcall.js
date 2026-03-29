
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var newcall;
        (function (newcall) {
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
                });

                $("#Call_Type").change(function () {
                    checkForProtocols();
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

                    fetch(resgrid.absoluteApiBaseUrl + '/api/v4/Geocoding/ForwardGeocode?address=' + encodeURIComponent(where), { headers: { 'Authorization': 'Bearer ' + getAuthToken() } })
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
                            alert("What3Words was unable to find a location for those words. Ensure its 3 words separated by periods.");
                        }
                    });
                    evt.preventDefault();
                });
                $("#setPinButton").click(function (evt) {
                    var lat = parseFloat($("#Latitude").val());
                    var lng = parseFloat($("#Longitude").val());
                    if (isNaN(lat) || isNaN(lng)) {
                        alert("Please enter valid numeric latitude and longitude values.");
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
                        { data: 'Name', title: 'Name' },
                        { data: 'Eta', title: 'ETA' },
                        { data: null, title: 'Status', orderable: false, render: function(d,t,row) { return '<span style="color:'+row.StatusColor+'">'+row.Status+'</span>'; } },
                        { data: null, title: 'Staffing', orderable: false, render: function(d,t,row) { return '<span style="color:'+row.StaffingColor+'">'+row.Staffing+'</span>'; } },
                        { data: 'Group', title: 'Group' },
                        { data: 'Roles', title: 'Roles' }
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
                        { data: 'Name', title: 'Name' },
                        { data: 'Count', title: 'Personnel Count' }
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
                        { data: 'Name', title: 'Name' },
                        { data: 'Count', title: 'Personnel Count' }
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
                    modal.find('.modal-title').text(`Questions for ${protocol.Name}`);

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

                    $('#linkedCalls tbody').first().append(`<tr><td style='max-width: 215px;'>${data[0].text}<input type='hidden' id='linkedCall_${data[0].id}' name='linkedCall_${data[0].id}' value='${data[0].id}' /></td><td>${$('#selectCallNote').val()}<input type='hidden' id='linkedCallNote_${data[0].id}' name='linkedCallNote_${data[0].id}' value='${$('#selectCallNote').val()}' /></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this call link'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>`);
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

                $('#checkAllPersonnel').on('click', function () {
                    $('#personnelGrid').find(':checkbox').prop('checked', this.checked);
                });
                $('#checkAllGroups').on('click', function () {
                    $('#groupsGrid').find(':checkbox').prop('checked', this.checked);
                });
                $('#checkAllRoles').on('click', function () {
                    $('#rolesGrid').find(':checkbox').prop('checked', this.checked);
                });
                $('a[data-toggle="tab"]').on('shown.bs.tab', function (e) {
                    // DataTables adjusts itself; trigger resize for any hidden columns
                    if (e.target && e.target.textContent === "Personnel") { personnelTable.columns.adjust(); }
                    else if (e.target && e.target.textContent === "Groups") { groupsTable.columns.adjust(); }
                    else if (e.target && e.target.textContent === "Roles") { rolesTable.columns.adjust(); }
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
                map.panTo(new L.LatLng(-34.397, 150.644));
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
            function geocodeCoordinates(lat, lng) {
                fetch(resgrid.absoluteApiBaseUrl + '/api/v4/Geocoding/ReverseGeocode?lat=' + lat + '&lon=' + lng, { headers: { 'Authorization': 'Bearer ' + getAuthToken() } })
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
                fetch(resgrid.absoluteApiBaseUrl + '/api/v4/Geocoding/ReverseGeocode?lat=' + pos.lat + '&lon=' + pos.lng, { headers: { 'Authorization': 'Bearer ' + getAuthToken() } })
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
                $('#' + gridName).find(':checkbox').trigger('click');//.prop('checked', item.value);
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
                if (state === 0) {
                    return "Inactive";
                } else if (state === 1) {
                    return `Active <input type='text' id='activeProtocol_${id}' name='activeProtocol_${id}' style='display:none;' value='1'></input><input type='text' id='protocolCode_${id}' name='protocolCode_${id}' style='display:none;' value='${code}'></input>`;
                } else if (state === 2) {
                    return `<a id="answerProcotolQuestions_${id}" class="btn btn-warning btn-xs" data-toggle="modal" data-target="#protocolQuestionWindow" data-protocolId="${id}">Answer Questions</a> <input type='text' id='pendingProtocol_${id}' name='pendingProtocol_${id}' style='display:none;' value='0'></input><input type='text' id='protocolCode_${id}' name='protocolCode_${id}' style='display:none;' value='${code}'></input>`;
                } else {
                    return "Unknown";
                }
            }
            newcall.getStatusField = getStatusField;

            checkForProtocols();
        })(newcall = dispatch.newcall || (dispatch.newcall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
