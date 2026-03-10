var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var home;
        (function (home) {
            var activeCallsTable, unitsTable;
            $(document).ready(function () {
                resgrid.common.analytics.track('Dispatch Index');
                resgrid.common.signalr.init(refreshCalls, refreshPersonnel, refreshPersonnel, refreshUnits);

                activeCallsTable = $("#activeCallsList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetActiveCallsList', dataSrc: '' },
                    pageLength: 50,
                    order: [[3, 'desc']],
                    columns: [
                        { data: 'Number', title: 'Number' },
                        { data: 'Name', title: 'Name' },
                        {
                            data: 'Priority', title: 'Priority',
                            render: function (data, type, row) {
                                return '<span style="background-color:' + row.Color + ';color:#fff;padding:2px 6px;border-radius:3px;">' + row.Priority + '</span>';
                            }
                        },
                        {
                            data: 'Timestamp', title: 'Timestamp', defaultContent: '',
                            render: function (data, type, row) {
                                if (type === 'sort' || type === 'type') { return row.LoggedOn || 0; }
                                return data || '';
                            }
                        },
                        {
                            data: 'CallId', title: 'Actions', orderable: false,
                            render: function (data, type, row) {
                                var html = '<a class="btn btn-xs btn-info" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/ViewCall?callId=' + data + '">View</a> ';
                                if (row.CanUpdateCall) {
                                    html += '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/UpdateCall?callId=' + data + '">Update</a> ';
                                }
                                if (row.CanCloseCall) {
                                    html += '<a class="btn btn-xs btn-warning" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/CloseCall?callId=' + data + '">Close</a> ';
                                }
                                if (row.CanDeleteCall) {
                                    html += '<a class="btn btn-xs btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/DeleteCall?callId=' + data + '">Delete</a>';
                                }
                                return html;
                            }
                        }
                    ]
                });

                unitsTable = $("#unitsStatusesList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsList', dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Name', title: 'Name' },
                        { data: 'Type', title: 'Type' },
                        { data: 'Station', title: 'Station' },
                        {
                            data: null, title: 'State', orderable: false,
                            render: function (data, type, row) {
                                return '<span style="background-color:' + row.StateColor + ';color:' + row.TextColor + ';padding:2px 6px;border-radius:3px;">' + row.State + '</span>';
                            }
                        },
                        { data: 'Timestamp', title: 'Timestamp' }
                    ]
                });
            });
            function refreshCalls() {
                if (activeCallsTable) { activeCallsTable.ajax.reload(); }
            }
            home.refreshCalls = refreshCalls;
            function refreshUnits() {
                if (unitsTable) { unitsTable.ajax.reload(); }
            }
            home.refreshUnits = refreshUnits;
            function refreshPersonnel() { }
            home.refreshPersonnel = refreshPersonnel;
        })(home = dispatch.home || (dispatch.home = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
