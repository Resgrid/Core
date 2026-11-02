
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var viewType;
        (function (viewType) {
            'use strict';
            var map;
            var markers = [];

            function renderTextValue(data, type) {
                if (data === null || data === undefined) {
                    return '';
                }

                if (type === 'display' || type === 'filter') {
                    var safeWrapper = document.createElement('div');
                    safeWrapper.textContent = String(data);
                    return safeWrapper.innerHTML;
                }

                return data;
            }

            function createSafeTextElement(text) {
                var safeElement = document.createElement('span');
                safeElement.textContent = text === null || text === undefined ? '' : String(text);
                return safeElement;
            }

            $(document).ready(function () {
                resgrid.common.analytics.track('Mapping - View Type');

                var poisTable = $("#poisGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Mapping/GetPoisForType?poiTypeId=' + poiTypeId,
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        { data: 'Name', title: 'Name', defaultContent: '', render: renderTextValue },
                        { data: 'Address', title: 'Address', defaultContent: '', render: renderTextValue },
                        { data: 'Note', title: 'Note', defaultContent: '', render: renderTextValue },
                        { data: 'Latitude', title: 'Latitude' },
                        { data: 'Longitude', title: 'Longitude' },
                        {
                            data: 'PoiId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                if (!canManagePois) {
                                    return '';
                                }

                                return '<a class="btn btn-xs btn-info" href="' + resgrid.absoluteBaseUrl + '/User/Mapping/EditPOI?poiId=' + data + '">Edit</a> ' +
                                    '<button type="button" class="btn btn-xs btn-danger" onclick="resgrid.mapping.viewType.deletePoi(' + data + ')">Delete</button>';
                            }
                        }
                    ]
                });

                resgrid.maps.create('map', { center: [parseFloat(centerLat) || 39.7392, parseFloat(centerLon) || -104.9903], zoom: 9 }).then(function (created) {
                    map = created;
                    initMap();
                });
            });

            function createMarkerOptions(poi) {
                var iconClass = resgrid.escapeHtml(poi.ImagePath || 'map-icon-map-pin');
                var color = resgrid.escapeHtml(poi.Color || '#3388ff');
                return {
                    html: '<span class="map-icon ' + iconClass + '" style="color:' + color + '; font-size: 24px;"></span>',
                    iconSize: [24, 24],
                    iconAnchor: [12, 24],
                    title: poi.Title || '',
                    popupHtml: poi.InfoWindowContent ? resgrid.escapeHtml(poi.InfoWindowContent) : null
                };
            }

            function initMap() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Mapping/GetTypesMapData?poiTypeId=' + poiTypeId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (data && data.Pois && map) {
                        var positions = [];
                        data.Pois.forEach(function (poi) {
                            var position = [Number(poi.Latitude), Number(poi.Longitude)];
                            markers.push(map.addMarker(position, createMarkerOptions(poi)));
                            positions.push(position);
                        });

                        if (positions.length > 0) {
                            map.fitBounds(positions, { padding: 20 });
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

            function deletePoi(poiId) {
                if (!confirm('Delete this POI?')) return;
                document.getElementById('deletePoiId').value = poiId;
                document.getElementById('deletePoiForm').submit();
            }
            viewType.deletePoi = deletePoi;

        })(viewType = mapping.viewType || (mapping.viewType = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
