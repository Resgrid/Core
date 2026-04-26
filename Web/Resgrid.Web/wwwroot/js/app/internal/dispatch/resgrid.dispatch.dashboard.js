var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var home;
        (function (home) {
            var activeCallsTable, unitsTable;
            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }
            $(document).ready(function () {
                resgrid.common.analytics.track('Dispatch Index');
                resgrid.common.signalr.init(refreshCalls, refreshPersonnel, refreshPersonnel, refreshUnits);

                activeCallsTable = $("#activeCallsList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetActiveCallsList', dataSrc: '' },
                    pageLength: 50,
                    order: [[3, 'desc']],
                    columns: [
                        { data: 'Number', title: getText('number', 'Number') },
                        { data: 'Name', title: getText('name', 'Name') },
                        {
                            data: 'Priority', title: getText('priority', 'Priority'),
                            render: function (data, type, row) {
                                return '<span style="background-color:' + row.Color + ';color:#fff;padding:2px 6px;border-radius:3px;">' + row.Priority + '</span>';
                            }
                        },
                        {
                            data: 'Timestamp', title: getText('timestamp', 'Timestamp'), defaultContent: '',
                            render: function (data, type, row) {
                                if (type === 'sort' || type === 'type') { return row.LoggedOn || 0; }
                                return data || '';
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

                unitsTable = $("#unitsStatusesList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsList', dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Name', title: getText('name', 'Name') },
                        { data: 'Type', title: getText('type', 'Type') },
                        { data: 'Station', title: getText('station', 'Station') },
                        {
                            data: null, title: getText('state', 'State'), orderable: false,
                            render: function (data, type, row) {
                                return '<span style="background-color:' + row.StateColor + ';color:' + row.TextColor + ';padding:2px 6px;border-radius:3px;">' + row.State + '</span>';
                            }
                        },
                        { data: 'Timestamp', title: getText('timestamp', 'Timestamp') }
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
