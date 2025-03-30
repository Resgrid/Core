
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var editcall;
        (function (editcall) {
            $(document).ready(function () {
                callMarker = null;
                map = null;

                let quillNote2 = new Quill('#nature-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $("#Call_Address").bind("keypress", function (event) {
                    if (event.keyCode == 13) {
                        $("#searchButton").click();
                        return false;
                    }
                });
                $("#What3Word").bind("keypress", function (event) {
                    if (event.keyCode == 13) {
                        $("#findw3wButton").click();
                        return false;
                    }
                });

                $('#PrimaryContact').select2();
                $('#AdditionalContacts').select2();

                let quillNote = new Quill('#note-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#updateCallForm', function () {
                    $('#Call_Notes').val(quillNote.root.innerHTML);
                    $('#Call_NatureOfCall').val(quillNote2.root.innerHTML);

                    return true;
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
                    if (result) {
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
                            //$("#What3Word").val('');
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

                    if (google && google.maps) {
                        var geocoder = new google.maps.Geocoder();

                        if (geocoder) {
                            geocoder.geocode({ 'address': where }, function (results, status) {
                                if (status == google.maps.GeocoderStatus.OK && results && results.length >= 0) {

                                    map.panTo(new L.LatLng(results[0].geometry.location.lat(), results[0].geometry.location.lng()));

                                    $("#Latitude").val(results[0].geometry.location.lat().toString());
                                    $("#Longitude").val(results[0].geometry.location.lng().toString());

                                    resgrid.dispatch.editcall.setMarkerLocation(results[0].geometry.location.lat().toString(), results[0].geometry.location.lng().toString());
                                }
                                else {
                                    console.log("Geocode was not successful for the following reason: " + status);
                                }
                            });
                        }
                    }
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
                            map.panTo(new L.LatLng(data.Latitude, data.Longitude));

                            $("#Latitude").val(data.Latitude);
                            $("#Longitude").val(data.Longitude);
                            //$("#What3Word").val('');

                            resgrid.dispatch.editcall.geocodeCoordinates(data.Latitude, data.Longitude);

                            resgrid.dispatch.editcall.setMarkerLocation(data.Latitude, data.Longitude);
                        }
                        else {
                            alert("What3Words was unable to find a location for those workds. Ensure its 3 words seperated by periods.");
                        }
                    });
                    evt.preventDefault();
                });
                $('#addNewLinkedCall').click(function () {
                    var data = $('#selectLinkedCall').select2('data');

                    $('#linkedCalls tbody').first().append(`<tr><td style='max-width: 215px;'>${data[0].text}<input type='hidden' id='linkedCall_${data[0].id}' name='linkedCall_${data[0].id}' value='${data[0].id}' /></td><td>${$('#selectCallNote').val()}<input type='hidden' id='linkedCallNote_${data[0].id}' name='linkedCallNote_${data[0].id}' value='${$('#selectCallNote').val()}' /></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this call link'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>`);
                    $('#selectCallNote').val('');
                    $('#selectLinkedCall').empty();
                });
                $("#personnelGrid").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + encodeURI($("#Latitude").val()) + '&callLong=' + encodeURI($("#Longitude").val())
                        },
                        schema: {
                            model: {
                                fields: {
                                    UserId: { type: "string" },
                                    Name: { type: "string" },
                                    Eta: { type: "string" },
                                    Status: { type: "string" },
                                    StatusColor: { type: "string" },
                                    Staffing: { type: "string" },
                                    StaffingColor: { type: "string" },
                                    Group: { type: "string" },
                                    Roles: { type: "string" }
                                }
                            }
                        },
                        //pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 600,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: false,
                    dataBound: function (e) {
                        resgrid.dispatch.editcall.updateDispatchedEntities();
                    },
                    columns: [
                        {
                            field: "UserId",
                            title: "",
                            width: 9,
                            filterable: false,
                            sortable: false,
                            headerTemplate: '<label><input type="checkbox" id="checkAllPersonnel"/></label>',
                            template: "<input type=\"checkbox\" id=\"dispatchUser_#=UserId#\" name=\"dispatchUser_#=UserId#\" />"
                        },
                        {
                            field: "Name",
                            title: "Name",
                            width: 50
                        },
                        {
                            field: "Eta",
                            title: "Eta",
                            width: 18
                        },
                        {
                            field: "Status",
                            title: "Status",
                            width: 30,
                            template: "<span style='color:#=StatusColor#;'>#=Status#</span>"
                        },
                        {
                            field: "Staffing",
                            title: "Staffing",
                            width: 30,
                            template: "<span style='color:#=StaffingColor#;'>#=Staffing#</span>"
                        },
                        {
                            field: "Group",
                            title: "Group",
                            width: 50
                        },
                        {
                            field: "Roles",
                            title: "Roles",
                            width: 100
                        }
                    ]
                });
                $("#groupsGrid").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Groups/GetGroupsForCallGrid'
                        },
                        schema: {
                            model: {
                                fields: {
                                    GroupId: { type: "number" },
                                    Name: { type: "string" },
                                    Count: { type: "number" }
                                }
                            }
                        },
                        //pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 600,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: false,
                    dataBound: function (e) {
                        resgrid.dispatch.editcall.updateDispatchedEntities();
                    },
                    columns: [
                        {
                            field: "GroupId",
                            title: "",
                            width: 28,
                            filterable: false,
                            sortable: false,
                            headerTemplate: '<label><input type="checkbox" id="checkAllGroups"/></label>',
                            template: "<input type=\"checkbox\" id=\"dispatchGroup_#=GroupId#\" name=\"dispatchGroup_#=GroupId#\" />"
                        },
                        "Name",
                        {
                            field: "Count",
                            title: "Personnel Count"
                        }
                    ]
                });
                $("#unitsGrid").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid?callLat=' + encodeURI($("#Latitude").val()) + '&callLong=' + encodeURI($("#Longitude").val())
                        },
                        schema: {
                            model: {
                                fields: {
                                    UnitId: { type: "number" },
                                    Name: { type: "string" },
                                    Type: { type: "string" },
                                    Station: { type: "string" },
                                    StateId: { type: "number" },
                                    State: { type: "string" },
                                    StateColor: { type: "string" },
                                    TextColor: { type: "string" },
                                    Timestamp: { type: "string" },
                                    Eta: { type: "string" }
                                }
                            }
                        },
                        //pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 600,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: false,
                    dataBound: function (e) {
                        resgrid.dispatch.editcall.updateDispatchedEntities();
                    },
                    columns: [
                        {
                            field: "UnitId",
                            title: "",
                            width: 28,
                            filterable: false,
                            sortable: false,
                            headerTemplate: '<label><input type="checkbox" id="checkAllUnits"/></label>',
                            template: "<input type=\"checkbox\" id=\"dispatchUnit_#=UnitId#\" name=\"dispatchUnit_#=UnitId#\" />"
                        },
                        "Name",
                        "Eta",
                        "Type",
                        {
                            field: "Type",
                            title: "Type"
                        },
                        {
                            field: "State",
                            title: "Status",
                            template: "<span style='color:#=StateColor#;'>#=State#</span>"
                        }
                    ]
                });
                $("#rolesGrid").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetRolesForCallGrid'
                        },
                        schema: {
                            model: {
                                fields: {
                                    RoleId: { type: "number" },
                                    Name: { type: "string" },
                                    Count: { type: "number" }
                                }
                            }
                        },
                        //pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 600,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: false,
                    dataBound: function (e) {
                        resgrid.dispatch.editcall.updateDispatchedEntities();
                    },
                    columns: [
                        {
                            field: "RoleId",
                            title: "",
                            width: 28,
                            filterable: false,
                            sortable: false,
                            headerTemplate: '<label><input type="checkbox" id="checkAllRoles"/></label>',
                            template: "<input type=\"checkbox\" id=\"dispatchRole_#=RoleId#\" name=\"dispatchRole_#=RoleId#\" />"
                        },
                        "Name",
                        {
                            field: "Count",
                            title: "Personnel Count"
                        }
                    ]
                });
                $('#checkAllPersonnel').on('click', function () {
                    $('#personnelGrid').find(':checkbox').prop('checked', this.checked);
                });
                $('#checkAllGroups').on('click', function () {
                    $('#groupsGrid').find(':checkbox').prop('checked', this.checked);
                });
                $('#checkAllUnits').on('click', function () {
                    $('#unitsGrid').find(':checkbox').prop('checked', this.checked);
                });
                $('#checkAllRoles').on('click', function () {
                    $('#rolesGrid').find(':checkbox').prop('checked', this.checked);
                });
                $('a[data-toggle="tab"]').on('shown.bs.tab', function (e) {
                    if (e.target && e.target.textContent === "Personnel") {
                        var personnelsGrid = $('#personnelGrid');
                        var personnelDataArea = personnelsGrid.find('.k-grid-content');
                        personnelDataArea.height(556);
                        personnelsGrid.height(600);
                    }
                    else if (e.target && e.target.textContent === "Groups") {
                        var groupsGrid = $('#groupsGrid');
                        var groupsDataArea = groupsGrid.find('.k-grid-content');
                        groupsDataArea.height(556);
                        groupsGrid.height(600);
                    }
                    else if (e.target && e.target.textContent === "Units") {
                        var gridElement = $('#unitsGrid');
                        var dataArea = gridElement.find('.k-grid-content');
                        dataArea.height(556);
                        gridElement.height(600);
                    }
                    else if (e.target && e.target.textContent === "Roles") {
                        var rolesGrid = $('#rolesGrid');
                        var rolesDataArea = rolesGrid.find('.k-grid-content');
                        rolesDataArea.height(556);
                        rolesGrid.height(600);
                    }
                });
            });
            function findLocation(pos) {
                if (google && google.maps) {
                    var geocoder = new google.maps.Geocoder();

                    if (geocoder) {
                        geocoder.geocode({
                            latLng: pos
                        }, function (results, status) {
                            if (status == google.maps.GeocoderStatus.OK) {
                                $("#Call_Address").val(results[0].formatted_address);
                            }
                            else {
                                console.log("Geocode was not successful for the following reason: " + status);
                            }
                        });
                    }
                    $("#Latitude").val(pos.lat().toString());
                    $("#Longitude").val(pos.lng().toString());
                }
            }
            editcall.findLocation = findLocation;
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

                        resgrid.dispatch.editcall.geocodeCoordinates(position.lat, position.lng);
                    });
                }
            }
            editcall.setMarkerLocation = setMarkerLocation;
            function geocodeCoordinates(lat, lng) {
                if (google && google.maps) {
                    let geocoder = new google.maps.Geocoder();

                    if (geocoder) {
                        geocoder.geocode({
                            latLng: new google.maps.LatLng(lat, lng)
                        }, function (results, status) {
                            if (status == google.maps.GeocoderStatus.OK && results && results.length >= 0) {
                                $("#Call_Address").val(results[0].formatted_address);
                            }
                            else {
                                console.log("Geocode was not successful for the following reason: " + status);
                            }
                        });
                    }
                }
            }
            editcall.geocodeCoordinates = geocodeCoordinates;
            function refreshPersonnelGrid() {
                var personnelGrid = $('#personnelGrid').data('kendoGrid');
                var unitsGrid = $('#unitsGrid').data('kendoGrid');
                personnelGrid.dataSource.transport.options.read.url = resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + encodeURI($("#Latitude").val()) + '&callLong=' + encodeURI($("#Longitude").val());
                unitsGrid.dataSource.transport.options.read.url = resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid?callLat=' + encodeURI($("#Latitude").val()) + '&callLong=' + encodeURI($("#Longitude").val());
                personnelGrid.dataSource.read();
                personnelGrid.refresh();
                unitsGrid.dataSource.read();
                unitsGrid.refresh();
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
        })(editcall = dispatch.editcall || (dispatch.editcall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
