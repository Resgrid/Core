var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        // Calls waiting to be dispatched: pending calls (saved, nobody notified) and scheduled calls not yet sent.
        // Shared by the Calls dashboard (panel + badges), the Pending Calls page and the Scheduled Calls page.
        var waitingcalls;
        (function (waitingcalls) {
            var pendingTable = null;
            var panelMode = false;

            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }

            function escapeHtml(value) {
                return $('<div/>').text(value == null ? '' : String(value)).html();
            }

            function refreshCounts() {
                if (!$('#pendingCallsCount').length && !$('#scheduledCallsCount').length) {
                    return;
                }

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetWaitingCallCounts',
                    type: 'GET',
                    cache: false
                }).done(function (counts) {
                    $('#pendingCallsCount').text(counts && counts.pending ? counts.pending : '');
                    $('#scheduledCallsCount').text(counts && counts.scheduled ? counts.scheduled : '');
                });
            }

            // Posts DispatchCallNow (antiforgery) for a call and reports the result; onDone refreshes the caller.
            function dispatchNow(callId, onDone) {
                if (!window.confirm(getText('dispatchNowConfirm', 'Dispatch this call now to everyone on its dispatch list?'))) {
                    return;
                }

                var token = $('#dispatchCallNowForm input[name="__RequestVerificationToken"]').val();

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/DispatchCallNow',
                    type: 'POST',
                    data: { callId: callId, __RequestVerificationToken: token },
                    headers: { 'RequestVerificationToken': token, 'X-Requested-With': 'XMLHttpRequest' }
                }).done(function (result) {
                    if (result && result.success) {
                        if (typeof toastr !== 'undefined') { toastr.success(result.message); }
                    } else {
                        var message = result && result.message ? result.message : '';
                        if (typeof toastr !== 'undefined') { toastr.error(message); } else { window.alert(message); }
                        // Nobody to send it to yet: the edit page is where the dispatcher picks recipients. Any other
                        // failure (queue refused, already sent) keeps the list so the dispatcher can retry from it.
                        if (result && result.noRecipients) {
                            window.location.href = resgrid.absoluteBaseUrl + '/User/Dispatch/UpdateCall?callId=' + encodeURIComponent(callId);
                            return;
                        }
                    }
                    if (onDone) { onDone(); }
                    refresh();
                }).fail(function () {
                    if (typeof toastr !== 'undefined') { toastr.error(getText('dispatchNow', 'Dispatch Now')); }
                });
            }
            waitingcalls.dispatchNow = dispatchNow;

            function bindDispatchNow(tableSelector, onDone) {
                $(tableSelector).on('click', 'button[data-dispatch-now]', function (e) {
                    e.preventDefault();
                    dispatchNow($(this).attr('data-dispatch-now'), onDone);
                });
            }
            waitingcalls.bindDispatchNow = bindDispatchNow;

            // The pending calls grid. In panel mode (the dashboard) the panel hides itself while there are none.
            function initPendingTable(selector, pageLength, isPanel) {
                panelMode = !!isPanel;

                pendingTable = $(selector).DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetPendingCallsList',
                        dataSrc: function (rows) {
                            if (panelMode) {
                                $('#pendingCallsPanel').toggle(rows && rows.length > 0);
                            }
                            return rows || [];
                        }
                    },
                    pageLength: pageLength || 50,
                    searching: !panelMode,
                    lengthChange: !panelMode,
                    order: [[3, 'asc']],
                    language: { emptyTable: getText('noPendingCalls', 'No calls are waiting to be dispatched.') },
                    columns: [
                        { data: 'Number', title: getText('number', 'Number') },
                        { data: 'Name', title: getText('name', 'Name'), render: function (data) { return escapeHtml(data); } },
                        { data: 'Address', title: getText('address', 'Address'), render: function (data) { return escapeHtml(data); } },
                        {
                            data: 'LoggedOn', title: getText('received', 'Received'),
                            render: function (data, type, row) {
                                if (type === 'sort' || type === 'type') { return data; }
                                return escapeHtml(row.Timestamp);
                            }
                        },
                        {
                            data: 'Priority', title: getText('priority', 'Priority'),
                            render: function (data, type, row) {
                                return '<span style="background-color:' + escapeHtml(row.Color) + ';color:#fff;padding:2px 6px;border-radius:3px;">' + escapeHtml(row.Priority) + '</span>';
                            }
                        },
                        {
                            data: 'CallId', title: getText('actions', 'Actions'), orderable: false,
                            render: function (data, type, row) {
                                var html = '<a class="btn btn-xs btn-info" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/ViewCall?callId=' + data + '">' + escapeHtml(getText('view', 'View')) + '</a> ';
                                if (row.CanUpdateCall) {
                                    html += '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/UpdateCall?callId=' + data + '">' + escapeHtml(getText('update', 'Update')) + '</a> ';
                                    html += '<button type="button" class="btn btn-xs btn-success" data-dispatch-now="' + data + '">' + escapeHtml(getText('dispatchNow', 'Dispatch Now')) + '</button> ';
                                }
                                if (row.CanCloseCall) {
                                    html += '<a class="btn btn-xs btn-warning" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/CloseCall?callId=' + data + '">' + escapeHtml(getText('close', 'Close')) + '</a> ';
                                }
                                if (row.CanDeleteCall) {
                                    html += '<a class="btn btn-xs btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/DeleteCall?callId=' + data + '">' + escapeHtml(getText('delete', 'Delete')) + '</a>';
                                }
                                return html;
                            }
                        }
                    ]
                });

                bindDispatchNow(selector, null);
            }
            waitingcalls.initPendingTable = initPendingTable;

            function refresh() {
                if (pendingTable) { pendingTable.ajax.reload(null, false); }
                refreshCounts();
            }
            waitingcalls.refresh = refresh;

            $(document).ready(function () {
                if ($('#pendingCallsPanel').length && $('#pendingCallsList').length) {
                    initPendingTable('#pendingCallsList', 10, true);
                }
                refreshCounts();
            });
        })(waitingcalls = dispatch.waitingcalls || (dispatch.waitingcalls = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
