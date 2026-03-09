var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var viewevents;
        (function (viewevents) {
            $(document).ready(function () {
                viewevents.markers = [];
                viewevents.map = L.map('eventsMap').setView([centerLat, centerLon], 13);

                L.tileLayer(osmTileUrl, {
                    attribution: ''
                }).addTo(viewevents.map);

                markers = [];

                var eventsTable = $("#eventsGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitEvents?UnitId=' + unitId,
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
                        { data: 'State', title: 'State' },
                        { data: 'DestinationName', title: 'Destination' },
                        { data: 'Timestamp', title: 'Timestamp' },
                        { data: 'Note', title: 'Note' }
                    ]
                });

                // Add check-all header checkbox after table is drawn
                eventsTable.on('draw', function () {
                    updateMapMarkers(eventsTable);
                });

                $(document).on('click', '#checkAllEvents', function () {
                    $('#eventsGrid tbody :checkbox').prop('checked', this.checked);
                });

                // Add the select-all checkbox to the first column header
                $('#eventsGrid thead th:first').html('<label><input type="checkbox" id="checkAllEvents"/></label>');

                function updateMapMarkers(table) {
                    $.each(viewevents.markers, function (index, item) {
                        item.setMap && item.setMap(null);
                        item.remove && item.remove();
                    });
                    viewevents.markers = [];

                    var data = table.rows().data();
                    $.each(data, function (index, item) {
                        if (item.Latitude && item.Latitude.length > 0 && item.Longitude && item.Longitude.length > 0) {
                            var marker = L.marker([item.Latitude, item.Longitude], {
                                icon: new L.icon({
                                    iconUrl: "/images/Mapping/Event.png",
                                    iconSize: [32, 37],
                                    iconAnchor: [16, 37]
                                }),
                                draggable: false,
                                title: item.State + ' ' + item.Timestamp,
                                tooltip: item.State + ' ' + item.Timestamp
                            }).bindTooltip(item.State + ' ' + item.Timestamp,
                                {
                                    permanent: true,
                                    direction: 'bottom'
                                }).addTo(viewevents.map);

                            viewevents.markers.push(marker);
                        }
                    });

                    if (viewevents.markers && viewevents.markers.length > 0) {
                        var group = new L.featureGroup(viewevents.markers);
                        viewevents.map.fitBounds(group.getBounds());
                    }
                }
            });
        })(viewevents = units.viewevents || (units.viewevents = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
