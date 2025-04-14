
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var home;
        (function (home) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Dispatch Index');
                resgrid.common.signalr.init(refreshCalls, refreshPersonnel, refreshPersonnel, refreshUnits);
                $("#activeCallsList").kendoGrid({
                    dataSource: {
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Dispatch/GetActiveCallsList'
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
                        {
                            field: "Number",
                            title: "Number",
                            width: 100
                        },
                        "Name",
                        {
                            field: "Timestamp",
                            title: "Timestamp",
                            width: 175
                        },
                        {
                            field: "Priority",
                            title: "Priority",
                            width: 100,
                            template: kendo.template($("#callPriority-template").html())
                        },
                        {
                            field: "UnitId",
                            title: "Actions",
                            filterable: false,
                            template: kendo.template($("#callCommand-template").html())
                        }
                    ]
                });
                $("#unitsStatusesList").kendoGrid({
                    dataSource: {
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
                            filterable: false,
                            template: kendo.template($("#state-template").html())
                        },
                        "Timestamp"
                    ]
                });
            });
            function refreshCalls() {
                $('#activeCallsList').data('kendoGrid').dataSource.read();
                $('#activeCallsList').data('kendoGrid').refresh();
            }
            home.refreshCalls = refreshCalls;
            function refreshUnits() {
                $('#unitsStatusesList').data('kendoGrid').dataSource.read();
                $('#unitsStatusesList').data('kendoGrid').refresh();
            }
            home.refreshUnits = refreshUnits;
            function refreshPersonnel() {
            }
            home.refreshPersonnel = refreshPersonnel;
        })(home = dispatch.home || (dispatch.home = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
