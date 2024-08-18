
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var addArchivedCall;
        (function (addArchivedCall) {
            $(document).ready(function () {
                callMarker = null;
                map = null;

                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                let quill2 = new Quill('#editor-container2', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#addArchivedCallForm', function () {
                    $('#Call_NatureOfCall').val(quill.root.innerHTML);
                    $('#Call_Notes').val(quill2.root.innerHTML);

                    return true;
                });


                $('#Call_LoggedOn').datetimepicker({ step: 5 });
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
                    resgrid.dispatch.addArchivedCall.setMarkerLocation(e.latlng.lat, e.latlng.lng);

                    $("#Latitude").val(e.latlng.lat.toString());
                    $("#Longitude").val(e.latlng.lng.toString());
                    //$("#What3Word").val('');

                    map.panTo(e.latlng);

                    resgrid.dispatch.addArchivedCall.geocodeCoordinates(e.latlng.lat, e.latlng.lng);
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

                navigator.geolocation.getCurrentPosition(foundLocation, noLocation, { timeout: 10000 });
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

                                    resgrid.dispatch.addArchivedCall.setMarkerLocation(results[0].geometry.location.lat(), results[0].geometry.location.lng());
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

                            resgrid.dispatch.addArchivedCall.geocodeCoordinates(data.Latitude, data.Longitude);

                            resgrid.dispatch.addArchivedCall.setMarkerLocation(data.Latitude, data.Longitude);
                        }
                        else {
                            alert("What3Words was unable to find a location for those words. Ensure its 3 words separated by periods.");
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
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val()
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
                        pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 600,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: false,
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
                        pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 600,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: false,
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
                            read: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val()
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
                        pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 600,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: false,
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
                        pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 600,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: false,
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
                centerMap();
            });
            function centerMap() {
                if (centerLat && centerLng) {
                    map.panTo(new L.LatLng(centerLat, centerLng));
                }
            }
            addArchivedCall.centerMap = centerMap;
            function foundLocation(position) {
                map.panTo(new L.LatLng(position.coords.latitude, position.coords.longitude));
            }
            addArchivedCall.foundLocation = foundLocation;
            function noLocation() {
                map.panTo(new L.LatLng(-34.397, 150.644));
            }
            addArchivedCall.noLocation = noLocation;
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

                        resgrid.dispatch.addArchivedCall.geocodeCoordinates(position.lat, position.lng);
                    });
                }
            }
            addArchivedCall.setMarkerLocation = setMarkerLocation;
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
            addArchivedCall.geocodeCoordinates = geocodeCoordinates;
            function findLocation(pos) {
                var geocoder = new google.maps.Geocoder();
                geocoder.geocode({
                    latLng: pos
                }, function (results, status) {
                    if (status == google.maps.GeocoderStatus.OK) {
                        $("#Call_Address").val(results[0].formatted_address);
                    }
                    else {
                        alert("Geocode was not successful for the following reason: " + status);
                    }
                });
                $("#Latitude").val(pos.lat().toString());
                $("#Longitude").val(pos.lng().toString());
            }
            addArchivedCall.findLocation = findLocation;
            function refreshPersonnelGrid() {
                var personnelGrid = $('#personnelGrid').data('kendoGrid');
                var unitsGrid = $('#unitsGrid').data('kendoGrid');
                personnelGrid.dataSource.transport.options.read.url = resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val();
                unitsGrid.dataSource.transport.options.read.url = resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForCallGrid?callLat=' + $("#Latitude").val() + '&callLong=' + $("#Longitude").val();
                personnelGrid.dataSource.read();
                personnelGrid.refresh();
                unitsGrid.dataSource.read();
                unitsGrid.refresh();
            }
            addArchivedCall.refreshPersonnelGrid = refreshPersonnelGrid;
        })(addArchivedCall = dispatch.addArchivedCall || (dispatch.addArchivedCall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
