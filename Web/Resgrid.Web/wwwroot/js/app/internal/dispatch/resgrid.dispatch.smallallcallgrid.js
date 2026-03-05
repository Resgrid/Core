var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var smallallcallgrid;
        (function (smallallcallgrid) {
            var allCallsTable;
            $(document).ready(function () {
                allCallsTable = $("#smallCallsGrid").DataTable({
                    ajax: {
                        url: '/User/Dispatch/GetAllCallsForGrid',
                        dataSrc: ''
                    },
                    pageLength: 6,
                    columns: [
                        { data: 'Priority', title: 'Priority' },
                        { data: 'DispatchTime', title: 'Dispatch Time' },
                        { data: 'Name', title: 'Name' },
                        { data: 'State', title: 'State' },
                        {
                            data: 'CallId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<a class="btn btn-success selectCallButton" onclick="resgrid.dispatch.smallallcallgrid.selectCall(' + data + ');">Respond To Call</a>';
                            }
                        }
                    ]
                });
            });
            function refreshGrid() {
                if (allCallsTable) { allCallsTable.ajax.reload(); }
            }
            smallallcallgrid.refreshGrid = refreshGrid;
            function selectCall(callId) {
                var event = {};
                event['CallId'] = callId;
                $('.callsWindow').trigger(resgrid.dispatch.smallallcallgrid.selectCallButton, event);
            }
            smallallcallgrid.selectCall = selectCall;
            smallallcallgrid.selectCallButton = "selectCall";
        })(smallallcallgrid = dispatch.smallallcallgrid || (dispatch.smallallcallgrid = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
