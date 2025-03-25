
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

            });
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
