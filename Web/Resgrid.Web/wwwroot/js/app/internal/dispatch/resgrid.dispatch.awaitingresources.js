var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        // Calls awaiting resources (conditional run cards): the top-bar badge, the toast when a freed unit matches a
        // waiting call (or one was sent automatically), and the Awaiting Resources page list. The badge polls; pages
        // holding the eventing connection also refresh and alert on the outstandingRequirementsUpdated event.
        var awaitingresources;
        (function (awaitingresources) {
            var refreshTimer = null;
            var calls = [];

            function getText(key, fallback) {
                var $nav = $('#awaitingResourcesNav');
                var fromNav = $nav.length ? $nav.data(key.toLowerCase()) : null;
                if (fromNav) return fromNav;
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }

            function escapeHtml(value) {
                return $('<div/>').text(value == null ? '' : String(value)).html();
            }

            function prop(obj, name) {
                if (!obj) return undefined;
                if (obj[name] !== undefined) return obj[name];
                var pascal = name.charAt(0).toUpperCase() + name.slice(1);
                return obj[pascal];
            }

            function formatSeconds(seconds) {
                if (seconds === null || seconds === undefined) return '';
                var total = Math.max(0, Math.round(seconds));
                var rest = total % 60;
                return Math.floor(total / 60) + ':' + (rest < 10 ? '0' : '') + rest;
            }

            function token() {
                return $('#awaitingResourcesToken input[name="__RequestVerificationToken"]').val()
                    || $('input[name="__RequestVerificationToken"]').first().val();
            }

            function refresh() {
                if (!$('#awaitingResourcesNav').length && !$('#awaitingResourcesList').length) {
                    return;
                }

                // Only the Awaiting Resources page needs the list; everywhere else the badge asks for the count.
                var countOnly = !$('#awaitingResourcesList').length;

                $.ajax({ url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetAwaitingResources', data: { countOnly: countOnly }, type: 'GET', cache: false }).done(function (response) {
                    if (!prop(response, 'success') || !prop(response, 'enabled')) {
                        $('#awaitingResourcesNav').hide();
                        return;
                    }

                    var count = countOnly ? (prop(response, 'count') || 0) : (prop(response, 'calls') || []).length;
                    $('#awaitingResourcesCount').text(count);
                    $('#awaitingResourcesNav').toggle(count > 0);

                    if (!countOnly) {
                        calls = prop(response, 'calls') || [];
                        renderList();
                    }
                });
            }
            awaitingresources.refresh = refresh;

            function renderList() {
                var $list = $('#awaitingResourcesList');
                if (!$list.length) return;

                if (!calls.length) {
                    $list.html('<div class="alert alert-success">' + escapeHtml(getText('awaitingNone', 'No calls are awaiting resources.')) + '</div>');
                    return;
                }

                var html = '';
                calls.forEach(function (call) {
                    var callId = prop(call, 'callId');
                    var color = prop(call, 'priorityColor');
                    html += '<div class="panel panel-default"><div class="panel-heading"' + (color ? ' style="border-left: 6px solid ' + escapeHtml(color) + '"' : '') + '>';
                    html += '<a href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/ViewCall?callId=' + callId + '"><strong>' + escapeHtml((prop(call, 'callNumber') || callId) + ' ' + (prop(call, 'callName') || '')) + '</strong></a>';
                    if (prop(call, 'priorityName')) html += ' <span class="label label-default">' + escapeHtml(prop(call, 'priorityName')) + '</span>';
                    if (prop(call, 'address')) html += ' <span class="text-muted">' + escapeHtml(prop(call, 'address')) + '</span>';
                    html += '<span class="pull-right text-muted">' + escapeHtml(typeof moment !== 'undefined' ? moment.utc(prop(call, 'loggedOn')).fromNow() : '') + '</span></div><div class="panel-body">';

                    (prop(call, 'requirements') || []).forEach(function (r) {
                        var requirementId = prop(r, 'callOutstandingRequirementId');
                        html += '<div class="m-b-sm"><b>' + escapeHtml(prop(r, 'label')) + '</b> (' + escapeHtml((prop(r, 'requiredCount') - prop(r, 'resolvedCount'))) + ')';
                        if (prop(r, 'responseCode')) html += ' <span class="label label-default">' + escapeHtml(prop(r, 'responseCode')) + '</span>';
                        if (prop(r, 'sentInstead')) html += ' <span class="text-muted">' + escapeHtml(getText('awaitingSentInstead', 'sent instead: {0}').replace('{0}', prop(r, 'sentInstead'))) + '</span>';
                        html += ' <a href="#" class="awaiting-dismiss" data-call-id="' + callId + '" data-requirement-id="' + requirementId + '">' + escapeHtml(getText('awaitingDismiss', 'Dismiss')) + '</a>';

                        var units = prop(r, 'availableUnits') || [];
                        if (units.length) {
                            html += '<div>' + escapeHtml(getText('awaitingAvailable', 'Available now:')) + ' ';
                            units.slice(0, 5).forEach(function (u) {
                                html += '<button type="button" class="btn btn-xs btn-primary awaiting-dispatch" data-call-id="' + callId + '" data-unit-id="' + prop(u, 'unitId') + '">'
                                    + escapeHtml(getText('awaitingDispatchUnit', 'Dispatch {0}').replace('{0}', prop(u, 'unitName')))
                                    + (prop(u, 'responseSeconds') !== null && prop(u, 'responseSeconds') !== undefined ? ' (' + escapeHtml(formatSeconds(prop(u, 'responseSeconds'))) + ')' : '') + '</button> ';
                            });
                            html += '</div>';
                        } else {
                            html += '<div class="text-muted">' + escapeHtml(getText('awaitingNoneAvailable', 'No matching unit is available.')) + '</div>';
                        }
                        html += '</div>';
                    });

                    html += '</div></div>';
                });

                $list.html(html);
            }

            function dispatchUnit(callId, unitId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/DispatchToAwaitingCall',
                    type: 'POST',
                    data: { callId: callId, unitId: unitId, __RequestVerificationToken: token() }
                }).done(function (response) {
                    if (resgrid.common && resgrid.common.notifications) {
                        if (prop(response, 'success')) resgrid.common.notifications.showSuccess(getText('awaitingTitle', 'Awaiting resources'), prop(response, 'message') || '');
                        else resgrid.common.notifications.showWarning(getText('awaitingTitle', 'Awaiting resources'), prop(response, 'message') || '');
                    }
                }).always(refresh);
            }
            awaitingresources.dispatchUnit = dispatchUnit;

            function dismiss(callId, requirementId) {
                var note = window.prompt(getText('awaitingDismissPrompt', 'Why is this resource no longer needed? (optional)'), '');
                if (note === null) return;

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Dispatch/DismissOutstandingRequirement',
                    type: 'POST',
                    data: { callId: callId, requirementId: requirementId, note: note, __RequestVerificationToken: token() }
                }).always(refresh);
            }

            // A freed unit matching waiting calls stays on screen until acted on or closed; closing it does not clear the
            // waiting flag.
            function showAlert(alert) {
                var kind = prop(alert, 'kind');
                if (kind !== 1 && kind !== 2) return;
                if (typeof toastr === 'undefined') return;

                var message = escapeHtml(prop(alert, 'message') || '');
                if (kind === 1) {
                    (prop(alert, 'calls') || []).slice(0, 3).forEach(function (c) {
                        message += '<br/><button type="button" class="btn btn-xs btn-white awaiting-toast-dispatch" data-call-id="' + prop(c, 'callId') + '" data-unit-id="' + prop(alert, 'unitId') + '">'
                            + escapeHtml(getText('awaitingDispatchTo', 'Dispatch to {0}').replace('{0}', prop(c, 'callNumber') || prop(c, 'callId')))
                            + (prop(c, 'responseSeconds') !== null && prop(c, 'responseSeconds') !== undefined ? ' (' + escapeHtml(formatSeconds(prop(c, 'responseSeconds'))) + ')' : '') + '</button>';
                    });
                    toastr.warning(message, escapeHtml(getText('awaitingTitle', 'Awaiting resources')), { timeOut: 0, extendedTimeOut: 0, closeButton: true, tapToDismiss: false, escapeHtml: false });
                } else {
                    toastr.info(message, escapeHtml(getText('awaitingTitle', 'Awaiting resources')), { timeOut: 15000, closeButton: true });
                }
            }

            $(document).on('click', '.awaiting-dispatch, .awaiting-toast-dispatch', function (evt) {
                evt.preventDefault();
                $(this).prop('disabled', true);
                dispatchUnit($(this).data('call-id'), $(this).data('unit-id'));
                var $toast = $(this).closest('.toast');
                if ($toast.length) $toast.remove();
            });

            $(document).on('click', '.awaiting-dismiss', function (evt) {
                evt.preventDefault();
                dismiss($(this).data('call-id'), $(this).data('requirement-id'));
            });

            document.addEventListener('resgrid:outstanding-requirements-updated', function (evt) {
                refresh();
                var payload = evt && evt.detail ? evt.detail.payload : null;
                if (!payload) return;
                try { showAlert(typeof payload === 'string' ? JSON.parse(payload) : payload); } catch (e) { /* an older server sends no payload */ }
            });

            $(document).ready(function () {
                if (!$('#awaitingResourcesNav').length && !$('#awaitingResourcesList').length) return;
                refresh();
                refreshTimer = setInterval(refresh, 60000);
            });
        })(awaitingresources = dispatch.awaitingresources || (dispatch.awaitingresources = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
