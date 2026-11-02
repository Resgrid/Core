var resgrid;
(function (resgrid) {
    var personnel;
    (function (personnel) {
        var viewevents;
        (function (viewevents) {
            $(document).ready(function () {
                viewevents.markers = [];
                // resgrid.maps (MapLibre or Leaflet by the department's Maps.MapLibreWeb flag); markers wait for the map.
                var mapReady = resgrid.maps.create('eventsMap', { center: [Number(centerLat), Number(centerLon)], zoom: 13 }).then(function (map) {
                    viewevents.map = map;
                    return map;
                });

                viewevents.markers = [];

                var eventsTable = $("#eventsGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelEvents?userId=' + personnelUserId,
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        {
                            data: 'EventId',
                            title: '',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<input type="checkbox" id="selectEvent_' + data + '" name="selectEvent_' + data + '" />';
                            }
                        },
                        { data: 'State', title: 'State', render: resgrid.textCell },
                        { data: 'DestinationName', title: 'Destination', render: resgrid.textCell },
                        { data: 'Timestamp', title: 'Timestamp' },
                        { data: 'Note', title: 'Note', render: resgrid.textCell }
                    ]
                });

                // Update map markers and inject select-all checkbox after each draw
                eventsTable.on('draw', function () {
                    updateMapMarkers(eventsTable);
                });

                $(document).on('click', '#checkAllEvents', function () {
                    $('#eventsGrid tbody :checkbox').prop('checked', this.checked);
                });

                // Add the select-all checkbox to the first column header
                $('#eventsGrid thead th:first').html('<label><input type="checkbox" id="checkAllEvents"/></label>');

                function updateMapMarkers(table) {
                    mapReady.then(function (map) { drawMarkers(map, table); });
                }

                function drawMarkers(map, table) {
                    $.each(viewevents.markers, function (index, item) {
                        item.remove && item.remove();
                    });
                    viewevents.markers = [];
                    var positions = [];

                    var data = table.rows().data();
                    $.each(data, function (index, item) {
                        if (item.Latitude && item.Latitude.length > 0 && item.Longitude && item.Longitude.length > 0) {
                            var position = [Number(item.Latitude), Number(item.Longitude)];
                            var marker = map.addMarker(position, {
                                iconUrl: '/images/Mapping/Event.png',
                                iconSize: [32, 37],
                                iconAnchor: [16, 37],
                                title: item.State + ' ' + item.Timestamp,
                                label: item.State + ' ' + item.Timestamp
                            });

                            viewevents.markers.push(marker);
                            positions.push(position);
                        }
                    });

                    if (positions.length > 0) {
                        map.fitBounds(positions);
                    }
                }
            });
        })(viewevents = personnel.viewevents || (personnel.viewevents = {}));
    })(personnel = resgrid.personnel || (resgrid.personnel = {}));
})(resgrid || (resgrid = {}));
