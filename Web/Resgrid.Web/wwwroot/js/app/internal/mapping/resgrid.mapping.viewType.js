
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var viewType;
        (function (viewType) {
            'use strict';
            var map;
            var markers = [];

            $(document).ready(function () {
                resgrid.common.analytics.track('Mapping - View Type');

                var poisTable = $("#poisGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Mapping/GetPoisForType?poiTypeId=' + poiTypeId,
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        {
                            data: 'PoiId',
                            title: '',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<input type="checkbox" id="dispatchRole_' + data + '" name="dispatchRole_' + data + '" />';
                            }
                        },
                        { data: 'Latitude', title: 'Latitude' },
                        { data: 'Longitude', title: 'Longitude' },
                        { data: 'Note', title: 'Note' }
                    ]
                });
                poisTable.on('draw', function () {
                    $('#poisGrid thead th:first').html('<label><input type="checkbox" id="checkAllRoles"/></label>');
                });
                $(document).on('click', '#checkAllRoles', function () {
                    $('#poisGrid tbody :checkbox').prop('checked', this.checked);
                });

                map = L.map('map').setView([parseFloat(centerLat) || 39.7392, parseFloat(centerLon) || -104.9903], 9);
                L.tileLayer(osmTileUrl, { maxZoom: 19, attribution: osmTileAttribution }).addTo(map);

                initMap();
            });

            function createMarkerIcon(poi) {
                var iconClass = poi.ImagePath || 'map-icon-map-pin';
                var color = poi.Color || '#3388ff';
                return L.divIcon({
                    className: '',
                    html: '<span class="map-icon ' + iconClass + '" style="color:' + color + '; font-size: 24px;"></span>',
                    iconSize: [24, 24],
                    iconAnchor: [12, 24]
                });
            }

            function initMap() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Mapping/GetTypesMapData?poiTypeId=' + poiTypeId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (data && data.Pois) {
                        data.Pois.forEach(function (poi) {
                            var marker = L.marker([poi.Latitude, poi.Longitude], {
                                icon: createMarkerIcon(poi)
                            }).addTo(map);
                            markers.push(marker);
                        });

                        if (markers.length > 0) {
                            var group = new L.featureGroup(markers);
                            map.fitBounds(group.getBounds(), { padding: [20, 20] });
                        }
                    }
                });
            }
            viewType.initMap = initMap;

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
