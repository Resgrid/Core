var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var smallallcallgrid;
        (function (smallallcallgrid) {
            var allCallsTable;
            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }
            $(document).ready(function () {
                allCallsTable = $("#smallCallsGrid").DataTable({
                    ajax: {
                        url: '/User/Dispatch/GetAllCallsForGrid',
                        dataSrc: ''
                    },
                    pageLength: 6,
                    columns: [
                        { data: 'Priority', title: getText('priority', 'Priority') },
                        { data: 'DispatchTime', title: getText('dispatchTime', 'Dispatch Time') },
                        { data: 'Name', title: getText('name', 'Name') },
                        { data: 'State', title: getText('state', 'State') },
                        {
                            data: 'CallId',
                            title: getText('actions', 'Actions'),
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<a class="btn btn-success selectCallButton" onclick="resgrid.dispatch.smallallcallgrid.selectCall(' + data + ');">' + getText('respondToCall', 'Respond To Call') + '</a>';
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
