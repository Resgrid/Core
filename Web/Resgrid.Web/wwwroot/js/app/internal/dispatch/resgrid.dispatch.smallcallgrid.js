
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var smallcallgrid;
        (function (smallcallgrid) {
            $(document).ready(function () {
                $("#smallActiveCallsGrid").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: '/User/Dispatch/GetActiveCallsForGrid'
                        },
                        schema: {
                            model: {
                                fields: {
                                    CallId: { type: "int" },
                                    Priority: { type: "string" },
                                    Name: { type: "string" },
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
                            template: kendo.template($("#smallActiveCallRowActionColumnTemplate").html()),
                            width: 125
                        }
                    ]
                });
            });
            function refreshGrid() {
                var grid = $("#smallActiveCallsGrid").data("kendoGrid");
                grid.dataSource.page(1);
                grid.dataSource.read();
            }
            smallcallgrid.refreshGrid = refreshGrid;
            function respondToCall(callId) {
                kendo.ui.progress($("#smallActiveCallsGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/UserRespondingToCall?callId=' + callId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    var event = {
                        callId: callId
                    };
                    $('.respondToACallWindow').trigger(resgrid.dispatch.smallcallgrid.respondToCallButton, event);
                    kendo.ui.progress($("#smallActiveCallsGrid"), false);
                });
            }
            smallcallgrid.respondToCall = respondToCall;
            smallcallgrid.respondToCallButton = "respondToCall";
        })(smallcallgrid = dispatch.smallcallgrid || (dispatch.smallcallgrid = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
