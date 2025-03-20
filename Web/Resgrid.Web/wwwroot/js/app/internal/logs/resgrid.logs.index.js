
var resgrid;
(function (resgrid) {
    var logs;
    (function (logs) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Logs List');
                $("#logsIndexList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: {
                                url: resgrid.absoluteBaseUrl + '/User/Logs/GetLogsList',
                                dataType: "json",
                                data: {
                                    year: $("#Year").val()
                                }
                            }
                        },
                        schema: {
                            model: {
                                fields: {
                                    LogId: { type: "number" },
                                    Type: { type: "string" },
                                    Group: { type: "string" },
                                    LoggedOn: { type: "string" },
                                    LoggedBy: { type: "string" },
                                    CanDelete: { type: "boolean" }
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
                        "Type",
                        "Group",
                        "LoggedBy",
                        "LoggedOn",
                        {
                            field: "LogId",
                            title: "Actions",
                            filterable: false,
                            template: kendo.template($("#logsCommand-template").html())
                        }
                    ]
                });
                $("#Year").change(function () {
                    $("#logsIndexList").data("kendoGrid").dataSource.read({ year: $("#Year").val() });
                });
            });
        })(index = logs.index || (logs.index = {}));
    })(logs = resgrid.logs || (resgrid.logs = {}));
})(resgrid || (resgrid = {}));
