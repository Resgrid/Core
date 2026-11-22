var resgrid;
(function (resgrid) {
    var dispatcherdesk;
    (function (dispatcherdesk) {
        var board;
        (function (board) {
            // Shifts -> Dispatcher Desk: the live board. Polls the Board endpoint every few seconds, refetches at once on the
            // desk's realtime events, and ticks the break countdowns and queue ages every second between fetches.
            var options = null;
            var data = null;
            var skew = 0;
            var loading = false;
            var pending = null;
            // The alert shows either a failed board load or an action's answer: a good load clears only its own failure.
            var alertIsLoadFailure = false;
            // Actions in flight, by the item or dispatcher they act on: a second click waits for the first answer.
            var busy = {};

            var STATE_CLASS = { 0: 'label-default', 1: 'label-primary', 2: 'label-warning', 3: 'label-default', 4: 'label-info' };
            var STATE_KEY = { 0: 'Offline', 1: 'Available', 2: 'Busy', 3: 'Away', 4: 'OnBreak' };
            var KIND_KEY = { 1: 'PendingCall', 2: 'ScheduledCall', 3: 'Text', 4: 'Voice', 5: 'Email', 6: 'Chat', 7: 'Other' };
            var KIND_ICON = { 1: 'fa-bullhorn', 2: 'fa-clock-o', 3: 'fa-comment', 4: 'fa-phone', 5: 'fa-envelope', 6: 'fa-comments', 7: 'fa-inbox' };
            var QUEUE_KEY = { 0: 'Queued', 1: 'Offered', 2: 'Accepted' };
            var QUEUE_CLASS = { 0: 'label-danger', 1: 'label-warning', 2: 'label-primary' };
            var BREAK_KEY = { 1: 'Break', 2: 'Meal', 3: 'Personal', 4: 'Other' };

            function t(key) {
                return dispatcherdesk.common.text(options.strings, key);
            }

            function esc(value) {
                return dispatcherdesk.common.escape(value);
            }

            function now() {
                return Date.now() + skew;
            }

            function label(css, text) {
                return '<span class="label ' + css + '">' + esc(text) + '</span>';
            }

            function memberRow(m) {
                var badges = '';
                if (m.IsSupervisor) { badges += ' ' + label('label-primary', t('DeskSupervisorBadge')); }
                if (m.IsMe) { badges += ' ' + label('label-info', t('YouBadge')); }
                if (!m.IsOnDuty) { badges += ' ' + label('label-default', t('OffDutyBadge')); }
                if (!m.IsSignedIn) { badges += ' ' + label('label-default', t('NotSignedInBadge')); }

                var name = '<strong>' + esc(m.Name) + '</strong>' + badges;
                if (m.DeviceKind) { name += '<br /><small class="text-muted"><i class="fa fa-mobile"></i> ' + esc(m.DeviceKind) + '</small>'; }

                var state = label(STATE_CLASS[m.State] || 'label-default', t('State.' + (STATE_KEY[m.State] || 'Offline')));
                if (m.AwayReason) { state += '<br /><small class="text-muted">' + esc(m.AwayReason) + '</small>'; }

                var shift = esc(m.ShiftName || '');
                if (m.DutyEnds) { shift += '<br /><small class="text-muted">' + esc(dispatcherdesk.common.format(t('EndsFormat'), m.DutyEnds)) + '</small>'; }

                var breakCell = '';
                if (m.State === 4) {
                    breakCell = esc(t('BreakType.' + (BREAK_KEY[m.BreakType] || 'Break')));
                    if (m.BreakStarted) { breakCell += ' <small class="text-muted">' + esc(m.BreakStarted) + '</small>'; }
                    if (m.BreakDueBackMs) {
                        breakCell += '<br /><span class="desk-countdown" data-due="' + m.BreakDueBackMs + '"></span>';
                    }
                } else if (m.NextBreak) {
                    breakCell = '<small class="text-muted">' + esc(t('NextBreakLabel')) + ' ' + esc(m.NextBreak) + '</small>';
                }

                var work = esc(dispatcherdesk.common.format(t('WorkLoadFormat'), m.ActiveItems, m.Capacity || 1));
                if (m.OfferedItems > 0) { work += ' ' + label('label-info', '+' + m.OfferedItems); }

                var actions = '';
                if ((data.CanSupervise || m.IsMe) && m.IsSignedIn) {
                    var id = esc(m.UserId);
                    if (m.State === 4) {
                        actions += '<button type="button" class="btn btn-xs btn-primary desk-end-break" data-user="' + id + '" data-name="' + esc(m.Name) + '">' + esc(t('EndBreakButton')) + '</button>';
                    } else if (m.State === 1 || m.State === 2) {
                        actions += '<button type="button" class="btn btn-xs btn-white desk-set-away" data-user="' + id + '">' + esc(t('SetAwayButton')) + '</button>';
                    } else if (m.State === 3) {
                        actions += '<button type="button" class="btn btn-xs btn-primary desk-set-available" data-user="' + id + '">' + esc(t('SetAvailableButton')) + '</button>';
                    }
                }

                return '<tr>' +
                    '<td>' + name + '</td>' +
                    '<td>' + state + '</td>' +
                    '<td>' + shift + '</td>' +
                    '<td>' + breakCell + '</td>' +
                    '<td>' + work + '</td>' +
                    '<td class="desk-actions">' + actions + '</td>' +
                    '</tr>';
            }

            function queueRow(q) {
                var kind = KIND_KEY[q.Kind] || 'Other';
                var item = '<i class="fa ' + (KIND_ICON[q.Kind] || 'fa-inbox') + ' desk-kind-icon" aria-hidden="true"></i> ' +
                    '<small class="text-muted">' + esc(t('Kind.' + kind)) + '</small> ' +
                    '<strong>' + esc(q.Source || '') + '</strong>';
                if (q.Subject) { item += '<br />' + esc(q.Subject); }
                if (q.Preview) { item += '<span class="desk-preview" title="' + esc(q.Preview) + '">' + esc(q.Preview) + '</span>'; }

                var age = '<span class="desk-age" data-created="' + q.CreatedMs + '"></span>';
                if (q.Due) { age += '<br /><small class="text-muted"><i class="fa fa-clock-o"></i> ' + esc(q.Due) + '</small>'; }

                var state = label(QUEUE_CLASS[q.State] || 'label-default', t('QueueState.' + (QUEUE_KEY[q.State] || 'Queued')));
                if (q.Escalated) { state += ' ' + label('label-danger', t('EscalatedBadge')); }
                state += '<br /><small class="text-muted">' + esc(q.AssignedName || t('Unassigned')) + '</small>';

                var actions = '';
                var mayMove = (data.CanSupervise || q.AssignedToMe) && (q.State === 1 || q.State === 2);
                if (mayMove) {
                    actions += '<button type="button" class="btn btn-xs btn-white desk-release" data-item="' + esc(q.ItemId) + '">' + esc(t('ReleaseButton')) + '</button>';
                }
                if (data.CanSupervise || q.AssignedToMe) {
                    actions += '<button type="button" class="btn btn-xs btn-white desk-transfer" data-item="' + esc(q.ItemId) + '" data-assigned="' + esc(q.AssignedUserId || '') + '">' + esc(t('TransferButton')) + '</button>';
                }
                if (q.CallId) {
                    actions += '<a class="btn btn-xs btn-white" href="' + esc(options.viewCallUrl + '?callId=' + encodeURIComponent(q.CallId)) + '">' + esc(t('ViewCallButton')) + '</a>';
                }

                return '<tr>' +
                    '<td>' + item + '</td>' +
                    '<td>' + age + '</td>' +
                    '<td>' + state + '</td>' +
                    '<td class="desk-actions">' + actions + '</td>' +
                    '</tr>';
            }

            function coverageCell(slot) {
                var css = 'desk-coverage-cell';
                if (slot.OnDuty <= 0) {
                    css += '';
                } else if (slot.BelowMinimum || slot.Available <= 0) {
                    css += ' desk-below';
                } else {
                    css += ' desk-ok';
                }
                if (slot.BreaksOverlap) { css += ' desk-overlap'; }
                if (slot.IsNow) { css += ' desk-now'; }

                var title = dispatcherdesk.common.format(t('CoverageSlotFormat'), slot.Label, slot.Available, slot.OnBreak, slot.OnDuty);
                return '<div class="' + css + '" title="' + esc(title) + '"></div>';
            }

            function render() {
                if (!data) {
                    return;
                }

                $('#deskOnDuty').text(data.OnDutyCount);
                $('#deskAvailable').text(data.AvailableCount);
                $('#deskOnBreak').text(data.OnBreakCount);
                $('#deskWaiting').text($.grep(data.Queue || [], function (q) { return q.State === 0; }).length);
                $('#deskMinimum').text(data.MinimumAvailable > 0 ? '/ ' + data.MinimumAvailable : '');
                $('#deskAvailableWidget').toggleClass('desk-stat-below', !!data.BelowMinimum)
                    .attr('title', data.BelowMinimum ? dispatcherdesk.common.format(t('BelowMinimumFormat'), data.AvailableCount, data.MinimumAvailable) : '');
                $('#deskUpdated').text(dispatcherdesk.common.format(t('BoardUpdatedFormat'), data.GeneratedOn));

                var members = data.Members || [];
                $('#deskMembers').html(members.length
                    ? $.map(members, memberRow).join('')
                    : '<tr><td colspan="6" class="text-muted">' + esc(t('NoDispatchersOnDesk')) + '</td></tr>');

                var queue = data.Queue || [];
                $('#deskQueue').html(queue.length
                    ? $.map(queue, queueRow).join('')
                    : '<tr><td colspan="4" class="text-muted">' + esc(t('QueueEmpty')) + '</td></tr>');

                $('#deskCoverage').html($.map(data.Coverage || [], coverageCell).join(''));
                tick();
            }

            function tick() {
                var current = now();

                $('#deskMembers .desk-countdown').each(function () {
                    var due = parseInt($(this).attr('data-due'), 10);
                    if (isNaN(due)) { return; }
                    var seconds = (due - current) / 1000;
                    if (seconds > 0) {
                        $(this).removeClass('desk-overdue').text(dispatcherdesk.common.format(t('BackInFormat'), dispatcherdesk.common.duration(seconds)));
                    } else if (seconds > -60) {
                        $(this).addClass('desk-overdue').text(t('DueBackNow'));
                    } else {
                        $(this).addClass('desk-overdue').text(dispatcherdesk.common.format(t('OverdueFormat'), Math.floor(-seconds / 60)));
                    }
                });

                $('#deskQueue .desk-age').each(function () {
                    var created = parseInt($(this).attr('data-created'), 10);
                    if (isNaN(created)) { return; }
                    $(this).text(dispatcherdesk.common.duration((current - created) / 1000));
                });
            }

            function load() {
                if (loading) {
                    return;
                }

                loading = true;
                $.ajax({ url: options.boardUrl, type: 'GET', dataType: 'json', cache: false }).done(function (result) {
                    data = result;
                    skew = (result && result.ServerNowMs ? result.ServerNowMs : Date.now()) - Date.now();
                    if (alertIsLoadFailure) {
                        $('#deskBoardAlert').hide();
                        alertIsLoadFailure = false;
                    }
                    render();
                }).fail(function () {
                    $('#deskBoardAlert').removeClass('alert-warning alert-success').addClass('alert-danger').text(t('BoardLoadFailed')).show();
                    alertIsLoadFailure = true;
                }).always(function () {
                    loading = false;
                });
            }

            // Several events arrive together (an offer is a desk and a queue change): fetch once.
            function soon() {
                if (pending) {
                    return;
                }
                pending = setTimeout(function () {
                    pending = null;
                    load();
                }, 750);
            }

            function showActionResult(result) {
                alertIsLoadFailure = false;
                dispatcherdesk.common.showResult('#deskBoardAlert', result, t('ActionFailed'));
            }

            function act(url, payload) {
                var key = payload && payload.itemId ? 'item:' + payload.itemId : (payload && payload.userId ? 'user:' + payload.userId : null);
                if (key && busy[key]) {
                    return;
                }
                if (key) {
                    busy[key] = true;
                }

                dispatcherdesk.common.post(url, payload).done(function (result) {
                    showActionResult(result);
                    load();
                }).fail(function () {
                    showActionResult(null);
                }).always(function () {
                    if (key) {
                        delete busy[key];
                    }
                });
            }

            function openTransfer(itemId, assignedUserId) {
                var select = $('#deskTransferTo').empty();
                $.each(data.Members || [], function (i, m) {
                    if (m.IsSignedIn && m.IsOnDuty && m.State !== 4 && m.State !== 0 && m.UserId !== assignedUserId) {
                        select.append($('<option></option>').val(m.UserId).text(m.Name));
                    }
                });

                if (select.children().length === 0) {
                    showActionResult({ Success: false, Message: t('NoTransferTargets'), Warnings: [] });
                    return;
                }

                $('#deskTransferItemId').val(itemId);
                $('#deskTransferNote').val('');
                $('#deskTransferModal').modal('show');
            }

            function bind() {
                var page = $('#dispatcherDeskBoard');

                page.on('click', '.desk-end-break', function () {
                    var userId = $(this).attr('data-user');
                    var name = $(this).attr('data-name');
                    if (userId !== data.MyUserId && !window.confirm(dispatcherdesk.common.format(t('EndBreakConfirm'), name))) {
                        return;
                    }
                    act(options.endBreakUrl, { userId: userId });
                });
                page.on('click', '.desk-set-available', function () {
                    act(options.setAvailableUrl, { userId: $(this).attr('data-user') });
                });
                page.on('click', '.desk-set-away', function () {
                    $('#deskAwayUserId').val($(this).attr('data-user'));
                    $('#deskAwayReason').val('');
                    $('#deskAwayModal').modal('show');
                });
                page.on('click', '.desk-release', function () {
                    if (!window.confirm(t('ReleaseConfirm'))) {
                        return;
                    }
                    act(options.releaseUrl, { itemId: $(this).attr('data-item') });
                });
                page.on('click', '.desk-transfer', function () {
                    openTransfer($(this).attr('data-item'), $(this).attr('data-assigned'));
                });

                $('#deskAwaySave').on('click', function () {
                    $('#deskAwayModal').modal('hide');
                    act(options.setAwayUrl, { userId: $('#deskAwayUserId').val(), reason: $('#deskAwayReason').val() });
                });
                $('#deskTransferSave').on('click', function () {
                    $('#deskTransferModal').modal('hide');
                    act(options.transferUrl, { itemId: $('#deskTransferItemId').val(), toUserId: $('#deskTransferTo').val(), note: $('#deskTransferNote').val() });
                });
            }

            function init(settings) {
                options = settings;
                bind();
                load();

                var poll = Math.max(5, parseInt(options.pollSeconds, 10) || 15) * 1000;
                setInterval(load, poll);
                setInterval(tick, 1000);
                dispatcherdesk.common.subscribe(soon);

                if (resgrid.common && resgrid.common.analytics) {
                    resgrid.common.analytics.track('Dispatcher Desk - Board');
                }
            }
            board.init = init;
        })(board = dispatcherdesk.board || (dispatcherdesk.board = {}));
    })(dispatcherdesk = resgrid.dispatcherdesk || (resgrid.dispatcherdesk = {}));
})(resgrid || (resgrid = {}));
