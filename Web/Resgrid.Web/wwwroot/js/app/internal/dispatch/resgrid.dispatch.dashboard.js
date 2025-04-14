
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var home;
        (function (home) {
            var map;
            var markers = [];
            var zoomLevel = 9;
            var height;
            var width;
            $(document).ready(function () {
                resgrid.common.analytics.track('Dispatch Index');
                resgrid.common.signalr.init(refreshCalls, refreshPersonnel, refreshPersonnel, refreshUnits);
                $("#activeCallsList").kendoGrid({
                    dataSource: {
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Dispatch/GetActiveCallsList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    CallId: { type: "number" },
                                    Number: { type: "string" },
                                    Priority: { type: "string" },
                                    Color: { type: "string" },
                                    Name: { type: "string" },
                                    State: { type: "string" },
                                    StateColor: { type: "string" },
                                    Address: { type: "string" },
                                    Timestamp: { type: "string" },
                                    CanDeleteCall: { type: "boolean" }
                                }
                            }
                        },
                        pageSize: 50
                    },
                    //height: 400,
                    filterable: true,
                    sortable: true,
                    scrollable: true,
                    pageable: {
                        refresh: true,
                        pageSizes: true,
                        buttonCount: 5
                    },
                    columns: [
                        {
                            field: "Number",
                            title: "Number",
                            width: 100
                        },
                        "Name",
                        {
                            field: "Timestamp",
                            title: "Timestamp",
                            width: 175
                        },
                        {
                            field: "Priority",
                            title: "Priority",
                            width: 100,
                            template: kendo.template($("#callPriority-template").html())
                        },
                        {
                            field: "UnitId",
                            title: "Actions",
                            filterable: false,
                            template: kendo.template($("#callCommand-template").html())
                        }
                    ]
                });
                $("#unitsStatusesList").kendoGrid({
                    dataSource: {
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsList'
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
                                    Timestamp: { type: "string" }
                                }
                            }
                        },
                        pageSize: 50
                    },
                    //height: 400,
                    filterable: true,
                    sortable: true,
                    scrollable: true,
                    pageable: {
                        refresh: true,
                        pageSizes: true,
                        buttonCount: 5
                    },
                    columns: [
                        "Name",
                        "Type",
                        "Station",
                        {
                            field: "State",
                            title: "State",
                            filterable: false,
                            template: kendo.template($("#state-template").html())
                        },
                        "Timestamp"
                    ]
                });
                //var mapCenter = new google.maps.LatLng(centerLat, centerLon);
                //var mapOptions = {
                //    zoom: zoomLevel,
                //    center: mapCenter
                //};
                //var mapDom = document.getElementById('map');
               // if (mapDom) {
               //     //var widget = document.getElementById('map').parentNode.parentNode.parentNode.parentNode.parentNode;
               //     //var tempHeight = widget.clientHeight - 70;
               //     //height = tempHeight + "px";
               //     height = "330px";
               //     width = "100%";
               // }
               map = new google.maps.Map(document.getElementById('map'), mapOptions);
                //initMap();
            });
            function refreshCalls() {
                $('#activeCallsList').data('kendoGrid').dataSource.read();
                $('#activeCallsList').data('kendoGrid').refresh();
                initMap();
            }
            home.refreshCalls = refreshCalls;
            function refreshUnits() {
                $('#unitsStatusesList').data('kendoGrid').dataSource.read();
                $('#unitsStatusesList').data('kendoGrid').refresh();
                initMap();
            }
            home.refreshUnits = refreshUnits;
            function refreshPersonnel() {
            }
            home.refreshPersonnel = refreshPersonnel;
            function initMap() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Mapping/GetMapData?ShowCalls=true&ShowPersonnel=true&ShowUnits=true&ShowStations=true&ShowDistricts=false&ShowPOIs=false',
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (data && data.Markers) {
                        var newMarkers = data.Markers;
                        if (newMarkers && newMarkers.length > 0) {
                            // clear map markers
                            if (markers && markers.length) {
                                // remove current markers.
                                markers.forEach(function (marker) {
                                    marker.setMap(null);
                                });
                                markers = [];
                            }
                            newMarkers.forEach(function (marker) {
                                var latLng = new google.maps.LatLng(marker.Latitude, marker.Longitude);
                                var mapMarker = new MarkerWithLabel({
                                    position: latLng,
                                    draggable: false,
                                    raiseOnDrag: false,
                                    map: map,
                                    title: marker.Title,
                                    icon: "/images/Mapping/" + marker.ImagePath + ".png",
                                    labelContent: marker.Title,
                                    labelAnchor: new google.maps.Point(35, 0),
                                    labelClass: "labels",
                                    labelStyle: { opacity: 0.60 }
                                });
                                markers.push(mapMarker);
                            });
                            var latlngbounds = new google.maps.LatLngBounds();
                            newMarkers.forEach(function (marker) {
                                var latLng = new google.maps.LatLng(marker.Latitude, marker.Longitude);
                                latlngbounds.extend(latLng);
                            });
                            map.setCenter(latlngbounds.getCenter());
                            map.fitBounds(latlngbounds);
                            var zoom = map.getZoom();
                            map.setZoom(zoom > zoomLevel ? zoomLevel : zoom);
                        }
                    }
                });
            }
            home.initMap = initMap;
        })(home = dispatch.home || (dispatch.home = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
