
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var archive;
        (function (archive) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Dispatch Archive');

                $("#archiveCallsList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: {
                                url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetArchivedCallsList',
                                dataType: "json",
                                data: {
                                    year: $("#Year").val()
                                }
                            }
                        },
                        schema: {
                            model: {
                                fields: {
                                    CallId: { type: "number" },
                                    Number: { type: "string" },
                                    Priority: { type: "string" },
                                    Color: { type: "string" },
                                    Name: { type: "string" },
                                    State: { type: "string" },
                                    StateColor: { type: "string" },
                                    Address: { type: "string" },
                                    Timestamp: { type: "string" },
                                    CanDeleteCall: { type: "boolean" }
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
                        "Number",
                        "Name",
                        "Timestamp",
                        {
                            field: "Priority",
                            title: "Priority",
                            template: kendo.template($("#archiveCallPriority-template").html())
                        },
                        {
                            field: "State",
                            title: "State",
                            template: kendo.template($("#archiveCallState-template").html())
                        },
                        {
                            field: "UnitId",
                            title: "Actions",
                            filterable: false,
                            template: kendo.template($("#archiveCallCommand-template").html())
                        }
                    ]
                });

                $("#Year").change(function () {
                    $("#archiveCallsList").data("kendoGrid").dataSource.read({ year: $("#Year").val() });
                });
            });
        })(archive = dispatch.archive || (dispatch.archive = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
