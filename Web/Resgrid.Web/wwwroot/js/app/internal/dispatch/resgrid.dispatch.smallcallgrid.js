var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var smallcallgrid;
        (function (smallcallgrid) {
            var callsTable;
            $(document).ready(function () {
                callsTable = $("#smallActiveCallsGrid").DataTable({
                    ajax: {
                        url: '/User/Dispatch/GetActiveCallsForGrid',
                        dataSrc: ''
                    },
                    pageLength: 6,
                    columns: [
                        { data: 'Priority', title: 'Priority' },
                        { data: 'DispatchTime', title: 'Dispatch Time' },
                        { data: 'Name', title: 'Name' },
                        {
                            data: 'CallId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<a class="btn btn-success respondToCallButton" onclick="resgrid.dispatch.smallcallgrid.respondToCall(' + data + ');">Respond To Call</a>';
                            }
                        }
                    ]
                });
            });
            function refreshGrid() {
                if (callsTable) { callsTable.ajax.reload(); }
            }
            smallcallgrid.refreshGrid = refreshGrid;
            function respondToCall(callId) {
                resgrid.showProgress('#smallActiveCallsGrid', true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/UserRespondingToCall?callId=' + callId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    var event = { callId: callId };
                    $('.respondToACallWindow').trigger(resgrid.dispatch.smallcallgrid.respondToCallButton, event);
                    resgrid.showProgress('#smallActiveCallsGrid', false);
                });
            }
            smallcallgrid.respondToCall = respondToCall;
            smallcallgrid.respondToCallButton = "respondToCall";
        })(smallcallgrid = dispatch.smallcallgrid || (dispatch.smallcallgrid = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
