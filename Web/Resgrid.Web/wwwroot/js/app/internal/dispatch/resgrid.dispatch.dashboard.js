var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var home;
        (function (home) {
            var activeCallsTable, unitsTable;
            // A unit's or call's name with its type icon (uploaded or built-in, from TypeIconUrl) in front, both escaped;
            // sorting and search keep using the plain name.
            function typeIconNameCell(name, type, row) {
                if (type !== 'display') { return name; }
                var text = $('<div/>').text(name || '').html();
                if (!row || !row.TypeIconUrl) { return text; }
                return '<img src="' + $('<div/>').text(row.TypeIconUrl).html() + '" alt="" aria-hidden="true" class="rg-type-icon" ' +
                    'style="width:20px;height:20px;object-fit:contain;vertical-align:middle;margin-right:4px;" />' + text;
            }

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
                        { data: 'Number', title: getText('number', 'Number'), type: 'string', render: resgrid.callNumberCell },
                        { data: 'Name', title: getText('name', 'Name'), render: typeIconNameCell },
                        {
                            data: 'Priority', title: getText('priority', 'Priority'),
                            render: function (data, type, row) {
                                return '<span style="background-color:' + resgrid.escapeHtml(row.Color) + ';color:#fff;padding:2px 6px;border-radius:3px;">' + resgrid.escapeHtml(row.Priority) + '</span>';
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
                        { data: 'Name', title: getText('name', 'Name'), render: typeIconNameCell },
                        { data: 'Type', title: getText('type', 'Type'), render: resgrid.textCell },
                        { data: 'Station', title: getText('station', 'Station'), render: resgrid.textCell },
                        {
                            data: null, title: getText('state', 'State'), orderable: false,
                            render: function (data, type, row) {
                                return '<span style="background-color:' + resgrid.escapeHtml(row.StateColor) + ';color:' + resgrid.escapeHtml(row.TextColor) + ';padding:2px 6px;border-radius:3px;">' + resgrid.escapeHtml(row.State) + '</span>';
                            }
                        },
                        { data: 'Timestamp', title: getText('timestamp', 'Timestamp') }
                    ]
                });
            });
            function refreshCalls() {
                if (activeCallsTable) { activeCallsTable.ajax.reload(); }
                // Pending and scheduled calls change on the same call events (created, edited, dispatched, closed).
                if (resgrid.dispatch.waitingcalls) { resgrid.dispatch.waitingcalls.refresh(); }
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
