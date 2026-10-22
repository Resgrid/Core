var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var smallunitsgrid;
        (function (smallunitsgrid) {
            var unitsTable;

            // Names, types, stations and the department's words are text: encoded for HTML and attributes alike.
            function escapeHtml(value) {
                return $('<div/>').text(value == null ? '' : String(value)).html().replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            }

            function textColumn(data, type) {
                return type === 'display' ? escapeHtml(data) : data;
            }

            $(document).ready(function () {
                unitsTable = $("#smallUnitsGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Units/GetUnits',
                        dataSrc: ''
                    },
                    pageLength: 6,
                    columns: [
                        { data: 'Name', title: 'Name', render: textColumn },
                        { data: 'Type', title: 'Type', render: textColumn },
                        { data: 'Station', title: 'Station', render: textColumn },
                        {
                            data: 'UnitId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data, type, row) {
                                return '<a class="btn btn-sm btn-success small-units-select" data-unit-id="' + escapeHtml(data) + '" data-unit-name="' + escapeHtml(row.Name) + '">'
                                    + escapeHtml(resgrid.terms.apply('Select Unit')) + '</a>';
                            }
                        }
                    ]
                });

                // Bound here rather than inline, so a unit name is never part of a script.
                $("#smallUnitsGrid").on('click', '.small-units-select', function () {
                    selectUnit(parseInt($(this).attr('data-unit-id'), 10), $(this).attr('data-unit-name'));
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
