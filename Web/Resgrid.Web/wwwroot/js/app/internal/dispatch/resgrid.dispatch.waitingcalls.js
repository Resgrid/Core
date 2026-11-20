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
            // Intake calls (SMS, email, chat, webhook, voice) say which channel they came from: callId -> { text: "via SMS: Hospital line",
            // expires }. Every entry expires (a call with no source yet is asked again soon, as its intake record may still be being
            // written), so a long-open page neither keeps stale answers nor grows without bound. An entry is placed while its request
            // is in flight so a refresh does not ask for the same calls twice.
            var intakeSources = {};
            var SourceBatchSize = 200;   // the endpoint answers at most this many calls per request
            var SourceTtlMs = 30 * 60 * 1000;
            var MissingSourceTtlMs = 2 * 60 * 1000;

            function intakeSourceText(callId) {
                var entry = intakeSources[callId];
                return entry && entry.expires > Date.now() ? entry.text : '';
            }

            function loadIntakeSources(rows) {
                var now = Date.now();
                $.each(Object.keys(intakeSources), function (_, id) {
                    if (intakeSources[id].expires <= now) { delete intakeSources[id]; }
                });

                var seen = {};
                var ids = $.map(rows || [], function (row) {
                    if (!row || !row.CallId || seen[row.CallId] || intakeSources.hasOwnProperty(row.CallId)) { return null; }
                    seen[row.CallId] = true;
                    return row.CallId;
                });

                for (var i = 0; i < ids.length; i += SourceBatchSize) {
                    requestIntakeSources(ids.slice(i, i + SourceBatchSize));
                }
            }

            // Only the ids sent are cached: an id the endpoint was never asked about must not be taken for "no source".
            function requestIntakeSources(batch) {
                var placeholderUntil = Date.now() + MissingSourceTtlMs;
                $.each(batch, function (_, id) { intakeSources[id] = { text: '', expires: placeholderUntil }; });

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/IntakeChannels/CallSources',
                    type: 'GET',
                    cache: false,
                    data: { callIds: batch.join(',') }
                }).done(function (sources) {
                    var now = Date.now();
                    $.each(batch, function (_, id) {
                        var text = (sources && sources[id]) || '';
                        intakeSources[id] = { text: text, expires: now + (text ? SourceTtlMs : MissingSourceTtlMs) };
                    });
                    if (pendingTable) { pendingTable.rows().invalidate('data').draw(false); }
                }).fail(function () {
                    // Asked again on the next refresh.
                    $.each(batch, function (_, id) { delete intakeSources[id]; });
                });
            }
            waitingcalls.loadIntakeSources = loadIntakeSources;
            waitingcalls.intakeSourceText = intakeSourceText;

            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }

            function escapeHtml(value) {
                return $('<div/>').text(value == null ? '' : String(value)).html().replace(/"/g, '&quot;').replace(/'/g, '&#39;');
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
                            loadIntakeSources(rows);
                            return rows || [];
                        }
                    },
                    pageLength: pageLength || 50,
                    searching: !panelMode,
                    lengthChange: !panelMode,
                    order: [[3, 'asc']],
                    language: { emptyTable: getText('noPendingCalls', 'No calls are waiting to be dispatched.') },
                    columns: [
                        { data: 'Number', title: getText('number', 'Number'), type: 'string', render: resgrid.callNumberCell },
                        {
                            data: 'Name', title: getText('name', 'Name'),
                            render: function (data, type, row) {
                                var source = intakeSourceText(row.CallId);
                                if (type !== 'display' || !source) { return escapeHtml(data); }
                                return escapeHtml(data) + '<div class="text-muted small intake-source">' + escapeHtml(source) + '</div>';
                            }
                        },
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
