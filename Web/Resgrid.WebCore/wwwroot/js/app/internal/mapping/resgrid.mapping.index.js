
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var index;
        (function (index) {
            'use strict';
            var map;
            var markers = [];
            var svgMarkers = [];
            var paths = [];
            var zoomLevel = 9;
            var height;
            var width;
            $(document).ready(function () {
                resgrid.common.analytics.track('Mapping - District');
                $("#saveMapOptionButtons").click(function () {
                    initMap();
                });
                $('#showCalls').on('ifChecked', function (event) {
                    SetShowCalls(true);
                });
                $('#showCalls').on('ifUnchecked', function (event) {
                    SetShowCalls(false);
                });
                $('#showCalls').change(function () {
                    SetShowPersonnel(this.checked);
                });
                $('#showUnits').change(function () {
                    SetShowUnits(this.checked);
                });
                $('#showStations').change(function () {
                    SetShowStations(this.checked);
                });
                $('#showDistricts').change(function () {
                    SetShowDistricts(this.checked);
                });
                $('#showPOIs').change(function () {
                    SetShowPOIs(this.checked);
                });
                if (GetShowCalls() === "true") {
                    $('#showCalls').prop('checked', true);
                }
                else {
                    $('#showCalls').prop('checked', false);
                }
                if (GetShowPersonnel() === "true") {
                    $('#showPersonnel').prop('checked', true);
                }
                else {
                    $('#showPersonnel').prop('checked', false);
                }
                if (GetShowUnits() === "true") {
                    $('#showUnits').prop('checked', true);
                }
                else {
                    $('#showUnits').prop('checked', false);
                }
                if (GetShowStations() === "true") {
                    $('#showStations').prop('checked', true);
                }
                else {
                    $('#showStations').prop('checked', false);
                }
                if (GetShowDistricts() === "true") {
                    $('#showDistricts').prop('checked', true);
                }
                else {
                    $('#showDistricts').prop('checked', false);
                }
                if (GetShowPOIs() === "true") {
                    $('#showPOIs').prop('checked', true);
                }
                else {
                    $('#showPOIs').prop('checked', false);
                }
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
                    height = "750px";
                    width = "100%";
                }
                map = new google.maps.Map(document.getElementById('map'), mapOptions);
                initMap();
            });
            function initMap() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Mapping/GetMapData?ShowCalls=' + GetShowCalls() + '&ShowPersonnel=' + GetShowPersonnel() + '&ShowUnits=' + GetShowUnits() + '&ShowStations=' + GetShowStations() + '&ShowDistricts=' + GetShowDistricts() + '&ShowPOIs=' + GetShowPOIs(),
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
                    if (data && data.Geofences) {
                        data.Geofences.forEach(function (geofence) {
                            createPolyline(JSON.parse(geofence.Fence), geofence.Color);
                        });
                    }
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
            index.initMap = initMap;
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
            index.createPolyline = createPolyline;
            function GetShowCalls() {
                if (typeof (Storage) !== "undefined") {
                    return window.localStorage.getItem("districtMapShowCalls");
                }
                else {
                    return "false";
                }
            }
            index.GetShowCalls = GetShowCalls;
            function GetShowPersonnel() {
                if (typeof (Storage) !== "undefined") {
                    return window.localStorage.getItem("districtMapShowPersonnel");
                }
                else {
                    return "false";
                }
            }
            index.GetShowPersonnel = GetShowPersonnel;
            function GetShowUnits() {
                if (typeof (Storage) !== "undefined") {
                    return window.localStorage.getItem("districtMapShowUnits");
                }
                else {
                    return "false";
                }
            }
            index.GetShowUnits = GetShowUnits;
            function GetShowStations() {
                if (typeof (Storage) !== "undefined") {
                    return window.localStorage.getItem("districtMapShowStations");
                }
                else {
                    return "false";
                }
            }
            index.GetShowStations = GetShowStations;
            function GetShowDistricts() {
                if (typeof (Storage) !== "undefined") {
                    return window.localStorage.getItem("districtMapShowDistricts");
                }
                else {
                    return "false";
                }
            }
            index.GetShowDistricts = GetShowDistricts;
            function GetShowPOIs() {
                if (typeof (Storage) !== "undefined") {
                    return window.localStorage.getItem("districtMapShowPOIs");
                }
                else {
                    return "false";
                }
            }
            index.GetShowPOIs = GetShowPOIs;
            function SetShowCalls(value) {
                if (typeof (Storage) !== "undefined") {
                    window.localStorage.setItem("districtMapShowCalls", value.toString());
                }
            }
            index.SetShowCalls = SetShowCalls;
            function SetShowPersonnel(value) {
                if (typeof (Storage) !== "undefined") {
                    window.localStorage.setItem("districtMapShowPersonnel", value.toString());
                }
            }
            index.SetShowPersonnel = SetShowPersonnel;
            function SetShowUnits(value) {
                if (typeof (Storage) !== "undefined") {
                    window.localStorage.setItem("districtMapShowUnits", value.toString());
                }
            }
            index.SetShowUnits = SetShowUnits;
            function SetShowStations(value) {
                if (typeof (Storage) !== "undefined") {
                    window.localStorage.setItem("districtMapShowStations", value.toString());
                }
            }
            index.SetShowStations = SetShowStations;
            function SetShowDistricts(value) {
                if (typeof (Storage) !== "undefined") {
                    window.localStorage.setItem("districtMapShowDistricts", value.toString());
                }
            }
            index.SetShowDistricts = SetShowDistricts;
            function SetShowPOIs(value) {
                if (typeof (Storage) !== "undefined") {
                    window.localStorage.setItem("districtMapShowPOIs", value.toString());
                }
            }
            index.SetShowPOIs = SetShowPOIs;
            function boolToCheckbox(boolean) {
                if (boolean === true)
                    return "on";
                else
                    return "off";
            }
            index.boolToCheckbox = boolToCheckbox;
            function checkboxToBool(checkbox) {
                if (checkbox === "on")
                    return true;
                else
                    return false;
            }
            index.checkboxToBool = checkboxToBool;
        })(index = mapping.index || (mapping.index = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
