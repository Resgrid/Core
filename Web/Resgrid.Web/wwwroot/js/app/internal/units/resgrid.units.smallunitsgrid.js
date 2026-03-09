var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var smallunitsgrid;
        (function (smallunitsgrid) {
            var unitsTable;
            $(document).ready(function () {
                unitsTable = $("#smallUnitsGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Units/GetUnits',
                        dataSrc: ''
                    },
                    pageLength: 6,
                    columns: [
                        { data: 'Name', title: 'Name' },
                        { data: 'Type', title: 'Type' },
                        { data: 'Station', title: 'Station' },
                        {
                            data: 'UnitId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data, type, row) {
                                return '<a class="btn btn-sm btn-success" onclick="resgrid.units.smallunitsgrid.selectUnit(' + data + ', \'' + row.Name + '\');">Select Unit</a>';
                            }
                        }
                    ]
                });
            });
            function refreshGrid() {
                if (unitsTable) {
                    unitsTable.ajax.reload();
                }
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
