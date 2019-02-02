
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var smallunitsgrid;
        (function (smallunitsgrid) {
            $(document).ready(function () {
                $("#smallUnitsGrid").kendoGrid({
                    dataSource: {
                        //type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Units/GetUnits'
                        },
                        schema: {
                            model: {
                                fields: {
                                    UnitId: { type: "int" },
                                    Name: { type: "string" },
                                    Type: { type: "string" },
                                    Station: { type: "string" }
                                }
                            }
                        },
                        pageSize: 6,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    //height: 365,
                    filterable: true,
                    sortable: true,
                    pageable: true,
                    scrollable: true,
                    columns: [
                        {
                            field: "Name",
                            title: "Name",
                            width: 150
                        },
                        {
                            field: "Type",
                            title: "Type",
                            width: 150
                        },
                        {
                            field: "Station",
                            title: "Station",
                            width: 150
                        },
                        {
                            template: kendo.template($("#smallUnitRowActionColumnTemplate").html()),
                            width: 125
                        }
                    ]
                });
            });
            function refreshGrid() {
                var grid = $("#smallUnitsGrid").data("kendoGrid");
                grid.dataSource.page(1);
                grid.dataSource.read();
            }
            smallunitsgrid.refreshGrid = refreshGrid;
            function selectUnit(unitId, unitName) {
                var event = {};
                event['UnitId'] = unitId;
                event['UnitName'] = unitName;
                $('.unitsWindow').trigger(resgrid.units.smallunitsgrid.selectUnitButton, event);
            }
            smallunitsgrid.selectUnit = selectUnit;
            smallunitsgrid.selectUnitButton = "selectUnit";
        })(smallunitsgrid = units.smallunitsgrid || (units.smallunitsgrid = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
