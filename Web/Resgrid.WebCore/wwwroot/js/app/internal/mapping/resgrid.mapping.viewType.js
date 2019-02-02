
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var viewType;
        (function (viewType) {
            'use strict';
            var map;
            var markers = [];
            var svgMarkers = [];
            var paths = [];
            var zoomLevel = 9;
            var height;
            var width;
            $(document).ready(function () {
                resgrid.common.analytics.track('Mapping - View Type');
                var mapCenter = new google.maps.LatLng(centerLat, centerLon);
                var mapOptions = {
                    zoom: zoomLevel,
                    center: mapCenter
                };
                var mapDom = document.getElementById('map');
                if (mapDom) {
                    //var widget = document.getElementById('map').parentNode.parentNode.parentNode.parentNode.parentNode;
                    //var tempHeight = widget.clientHeight - 70;
                    //height = tempHeight + "px";
                    height = "650px";
                    width = "100%";
                }
                $("#poisGrid").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Mapping/GetPoisForType?poiTypeId=' + poiTypeId
                        },
                        schema: {
                            model: {
                                fields: {
                                    PoiId: { type: "number" },
                                    Latitude: { type: "string" },
                                    Longitude: { type: "string" },
                                    Note: { type: "string" }
                                }
                            }
                        },
                        pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 650,
                    width: 210,
                    filterable: true,
                    sortable: true,
                    pageable: true,
                    columns: [
                        {
                            field: "PoiId",
                            title: "",
                            width: 28,
                            filterable: false,
                            sortable: false,
                            headerTemplate: '<label><input type="checkbox" id="checkAllRoles"/></label>',
                            template: "<input type=\"checkbox\" id=\"dispatchRole_#=PoiId#\" name=\"dispatchRole_#=PoiId#\" />"
                        },
                        "Latitude",
                        "Longitude",
                        "Note"
                    ]
                });
                map = new google.maps.Map(document.getElementById('map'), mapOptions);
                initMap();
            });
            function initMap() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Mapping/GetTypesMapData?poiTypeId=' + poiTypeId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (data && data.Pois) {
                        data.Pois.forEach(function (poi) {
                            var marker;
                            if (poi.Marker === "MAP_PIN") {
                                marker = new Marker({
                                    map: map,
                                    position: new google.maps.LatLng(poi.Latitude, poi.Longitude),
                                    icon: {
                                        path: MAP_PIN,
                                        fillColor: poi.Color,
                                        fillOpacity: 1,
                                        strokeColor: '',
                                        strokeWeight: 0
                                    },
                                    map_icon_label: '<span class="map-icon ' + poi.ImagePath + '"></span>'
                                });
                            }
                            else if (poi.Marker === "SQUARE_PIN") {
                                marker = new Marker({
                                    map: map,
                                    position: new google.maps.LatLng(poi.Latitude, poi.Longitude),
                                    icon: {
                                        path: SQUARE_PIN,
                                        fillColor: poi.Color,
                                        fillOpacity: 1,
                                        strokeColor: '',
                                        strokeWeight: 0
                                    },
                                    map_icon_label: '<span class="map-icon ' + poi.ImagePath + '"></span>'
                                });
                            }
                            else if (poi.Marker === "SHIELD") {
                                marker = new Marker({
                                    map: map,
                                    position: new google.maps.LatLng(poi.Latitude, poi.Longitude),
                                    icon: {
                                        path: SHIELD,
                                        fillColor: poi.Color,
                                        fillOpacity: 1,
                                        strokeColor: '',
                                        strokeWeight: 0
                                    },
                                    map_icon_label: '<span class="map-icon ' + poi.ImagePath + '"></span>'
                                });
                            }
                            else if (poi.Marker === "ROUTE") {
                                marker = new Marker({
                                    map: map,
                                    position: new google.maps.LatLng(poi.Latitude, poi.Longitude),
                                    icon: {
                                        path: ROUTE,
                                        fillColor: poi.Color,
                                        fillOpacity: 1,
                                        strokeColor: '',
                                        strokeWeight: 0
                                    },
                                    map_icon_label: '<span class="map-icon ' + poi.ImagePath + '"></span>'
                                });
                            }
                            else if (poi.Marker === "SQUARE") {
                                marker = new Marker({
                                    map: map,
                                    position: new google.maps.LatLng(poi.Latitude, poi.Longitude),
                                    icon: {
                                        path: SQUARE,
                                        fillColor: poi.Color,
                                        fillOpacity: 1,
                                        strokeColor: '',
                                        strokeWeight: 0
                                    },
                                    map_icon_label: '<span class="map-icon ' + poi.ImagePath + '"></span>'
                                });
                            }
                            else if (poi.Marker === "SQUARE_ROUNDED") {
                                marker = new Marker({
                                    map: map,
                                    position: new google.maps.LatLng(poi.Latitude, poi.Longitude),
                                    icon: {
                                        path: SQUARE_ROUNDED,
                                        fillColor: poi.Color,
                                        fillOpacity: 1,
                                        strokeColor: '',
                                        strokeWeight: 0
                                    },
                                    map_icon_label: '<span class="map-icon ' + poi.ImagePath + '"></span>'
                                });
                            }
                            svgMarkers.push(marker);
                        });
                    }
                });
            }
            viewType.initMap = initMap;
            function createPolyline(polyC, boundaryColor) {
                var path = new google.maps.Polyline({
                    path: polyC,
                    strokeColor: boundaryColor,
                    strokeOpacity: 1.0,
                    strokeWeight: 2
                });
                path.setMap(map);
                paths.push(path);
            }
            viewType.createPolyline = createPolyline;
            function boolToCheckbox(boolean) {
                if (boolean === true)
                    return "on";
                else
                    return "off";
            }
            viewType.boolToCheckbox = boolToCheckbox;
            function checkboxToBool(checkbox) {
                if (checkbox === "on")
                    return true;
                else
                    return false;
            }
            viewType.checkboxToBool = checkboxToBool;
        })(viewType = mapping.viewType || (mapping.viewType = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
