
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var smallallcallgrid;
        (function (smallallcallgrid) {
            $(document).ready(function () {
                $("#smallCallsGrid").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: '/User/Dispatch/GetAllCallsForGrid'
                        },
                        schema: {
                            model: {
                                fields: {
                                    CallId: { type: "int" },
                                    Priority: { type: "string" },
                                    Name: { type: "string" },
                                    State: { type: "string" },
                                    DispatchTime: { type: "date" }
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
                            field: "Priority",
                            title: "Priority",
                            width: 85
                        },
                        {
                            field: "DispatchTime",
                            title: "Dispatch Time",
                            format: "{0:MM/dd/yyyy hh:mm:ss}",
                            width: 140
                        },
                        {
                            field: "Name",
                            title: "Name",
                            width: 280
                        },
                        {
                            field: "State",
                            title: "State",
                            width: 85
                        },
                        {
                            template: kendo.template($("#smallCallRowActionColumnTemplate").html()),
                            width: 125
                        }
                    ]
                });
            });
            function refreshGrid() {
                var grid = $("#smallCallsGrid").data("kendoGrid");
                grid.dataSource.page(1);
                grid.dataSource.read();
            }
            smallallcallgrid.refreshGrid = refreshGrid;
            function selectCall(callId) {
                event['CallId'] = callId;
                $('.callsWindow').trigger(resgrid.dispatch.smallallcallgrid.selectCallButton, event);
            }
            smallallcallgrid.selectCall = selectCall;
            smallallcallgrid.selectCallButton = "selectCall";
        })(smallallcallgrid = dispatch.smallallcallgrid || (dispatch.smallallcallgrid = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
