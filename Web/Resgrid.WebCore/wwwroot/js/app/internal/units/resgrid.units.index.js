
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Units List');
                $("#unitsIndexList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    UnitId: { type: "number" },
                                    Name: { type: "string" },
                                    Type: { type: "string" },
                                    Station: { type: "string" },
                                    StateId: { type: "number" },
                                    State: { type: "string" },
                                    StateColor: { type: "string" },
                                    TextColor: { type: "string" },
                                    Timestamp: { type: "string" }
                                }
                            }
                        },
                        pageSize: 50
                    },
                    //height: 400,
                    filterable: true,
                    sortable: true,
                    scrollable: true,
                    pageable: {
                        refresh: true,
                        pageSizes: true,
                        buttonCount: 5
                    },
                    columns: [
                        "Name",
                        "Type",
                        "Station",
                        {
                            field: "State",
                            title: "State",
                            template: kendo.template($("#state-template").html())
                        },
                        "Timestamp",
                        {
                            field: "UnitId",
                            title: " ",
                            filterable: false,
                            sortable: false,
                            template: kendo.template($("#unitstatus-template").html())
                        },
                        {
                            field: "UnitId",
                            title: "Actions",
                            filterable: false,
                            sortable: false,
                            width: 250,
                            template: kendo.template($("#unitcommand-template").html())
                        }
                    ]
                });

                $(document).on('click', '.stateDropdown',
                    function () {
                        var unitId = $(this).attr("data-id");
                        $(".unitStateList_" + unitId).remove();

                        //if (!$(".unitStateListMain_" + unitId).length) {
                            var that = this;
                            $.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitOptionsDropdown?unitId=' + unitId, function (data) {
                                $(that).after(data);
                                //$(that).dropdown();

                               // $(that).parent().one('hide.bs.dropdown', function () {
                               //     var unitId = $(this).attr("data-id");

                               //     if (unitId) {
                               //         $(".unitStateList_" + unitId).remove();
                               //     }
                               // });
                            });
                        //}
                    });
            });
        })(index = units.index || (units.index = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
