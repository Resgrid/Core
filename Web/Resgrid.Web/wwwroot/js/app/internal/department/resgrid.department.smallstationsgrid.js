var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var smallstationsgrid;
        (function (smallstationsgrid) {
            var stationsTable;
            $(document).ready(function () {
                stationsTable = $("#smallStationGroupsGrid").DataTable({
                    ajax: {
                        url: '/User/Department/GetStationsForGrid',
                        dataSrc: ''
                    },
                    pageLength: 10,
                    columns: [
                        { data: 'Name', title: 'Name' },
                        {
                            data: 'GroupId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<a class="btn btn-success respondToStationButton" onclick="resgrid.department.smallstationsgrid.respondToStation(' + data + ');">Respond to Station</a>';
                            }
                        }
                    ]
                });
            });
            function refreshGrid() {
                if (stationsTable) { stationsTable.ajax.reload(); }
            }
            smallstationsgrid.refreshGrid = refreshGrid;
            function respondToStation(groupId) {
                resgrid.showProgress('#smallStationGroupsGridContainer', true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/UserRespondingToStation?stationId=' + groupId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    var event = { stationId: groupId };
                    $('.respondToAStationWindow').trigger(resgrid.department.smallstationsgrid.respondToStationButton, event);
                    resgrid.showProgress('#smallStationGroupsGridContainer', false);
                });
            }
            smallstationsgrid.respondToStation = respondToStation;
            smallstationsgrid.respondToStationButton = "respondToAStation";
        })(smallstationsgrid = department.smallstationsgrid || (department.smallstationsgrid = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
