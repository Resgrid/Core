var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var smallstationsgrid;
        (function (smallstationsgrid) {
            $(document).ready(function () {
                $("#smallStationGroupsGrid").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: '/User/Department/GetStationsForGrid'
                        },
                        schema: {
                            model: {
                                fields: {
                                    GroupId: { type: "int" },
                                    Name: { type: "string" }
                                }
                            }
                        },
                        pageSize: 10,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    filterable: true,
                    sortable: true,
                    pageable: true,
                    scrollable: true,
                    columns: [
                        {
                            field: "Name",
                            title: "Name"
                        },
                        {
                            template: kendo.template($("#smallStationGroupRowActionColumnTemplate").html()),
                            width: 190
                        }
                    ]
                });
            });
            function refreshGrid() {
                var grid = $("#smallStationGroupsGrid").data("kendoGrid");
                grid.dataSource.page(1);
                grid.dataSource.read();
            }
            smallstationsgrid.refreshGrid = refreshGrid;
            function respondToStation(groupId) {
                kendo.ui.progress($("#smallStationGroupsGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/UserRespondingToStation?stationId=' + groupId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    var event = {
                        stationId: groupId
                    };
                    $('.respondToAStationWindow').trigger(resgrid.department.smallstationsgrid.respondToStationButton, event);
                    kendo.ui.progress($("#smallStationGroupsGrid"), false);
                });
            }
            smallstationsgrid.respondToStation = respondToStation;
            smallstationsgrid.respondToStationButton = "respondToAStation";
        })(smallstationsgrid = department.smallstationsgrid || (department.smallstationsgrid = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
