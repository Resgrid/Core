var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var scheduledcalls;
        (function (scheduledcalls) {
            var scheduledCallsTable;
            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }
            $(document).ready(function () {
                resgrid.common.analytics.track('Dispatch Scheduled Calls');

                scheduledCallsTable = $("#scheduledCallsList").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetScheduledCallsList',
                        dataSrc: ''
                    },
                    pageLength: 50,
                    order: [[3, 'asc']],
                    columns: [
                        { data: 'Number', title: getText('number', 'Number') },
                        { data: 'Name', title: getText('name', 'Name') },
                        { data: 'Timestamp', title: getText('timestamp', 'Timestamp') },
                        {
                            data: 'DispatchOn', title: getText('scheduledDispatch', 'Scheduled Dispatch'),
                            render: function (data, type, row) {
                                if (data) {
                                    var d = new Date(data * 1000);
                                    return d.toLocaleString();
                                }
                                return '';
                            }
                        },
                        {
                            data: 'Priority', title: getText('priority', 'Priority'),
                            render: function (data, type, row) {
                                return '<span style="background-color:' + row.Color + ';color:#fff;padding:2px 6px;border-radius:3px;">' + row.Priority + '</span>';
                            }
                        },
                        {
                            data: null, title: getText('state', 'State'), orderable: false,
                            render: function (data, type, row) {
                                return '<span style="background-color:' + row.StateColor + ';color:#fff;padding:2px 6px;border-radius:3px;">' + row.State + '</span>';
                            }
                        },
                        {
                            data: 'CallId', title: getText('actions', 'Actions'), orderable: false,
                            render: function (data, type, row) {
                                var html = '<a class="btn btn-xs btn-info" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/ViewCall?callId=' + data + '">' + getText('view', 'View') + '</a> ';
                                if (row.CanUpdateCall) {
                                    html += '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/UpdateCall?callId=' + data + '">' + getText('update', 'Update') + '</a> ';
                                }
                                if (row.CanCloseCall) {
                                    html += '<a class="btn btn-xs btn-warning" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/CloseCall?callId=' + data + '">' + getText('close', 'Close') + '</a> ';
                                }
                                if (row.CanDeleteCall) {
                                    html += '<a class="btn btn-xs btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/DeleteCall?callId=' + data + '">' + getText('delete', 'Delete') + '</a>';
                                }
                                return html;
                            }
                        }
                    ]
                });
            });
        })(scheduledcalls = dispatch.scheduledcalls || (dispatch.scheduledcalls = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
