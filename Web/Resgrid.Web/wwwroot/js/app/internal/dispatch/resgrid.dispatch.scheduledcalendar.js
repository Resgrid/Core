var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var scheduledcalendar;
        (function (scheduledcalendar) {
            // Scheduled Calls → Calendar: every scheduled call waiting to go out, the calls recurring schedules created, and the
            // occurrences still to come, waiting for a dispatcher or missed. Calls open the call; an occurrence opens a dialog to send
            // it now, skip it, restore it, create its call now (to change it on its own) or edit its schedule. Times are the
            // department's local times. A banner links to the Recurring Calls alerts when any are open.
            var calendarInstance = null;
            var current = null;
            var text = window.resgridScheduledCalendarText || {};

            function token() {
                return $('#dispatchCallNowForm input[name="__RequestVerificationToken"]').val();
            }

            // The occurrence a request was made for: its answer only touches the dialog while that occurrence is still the one open
            // (the dialog may have been closed, or another occurrence opened, before it came back).
            function isOpen(props) {
                return current === props && $('#scheduledOccurrenceModal').hasClass('in');
            }

            function post(action, onSuccess) {
                if (!current) return;

                var props = current;
                $('#scheduledOccurrenceError').hide();
                $('#scheduledOccurrenceModal .modal-footer button').prop('disabled', true);

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/RecurringCalls/' + action,
                    type: 'POST',
                    data: { id: props.recurringCallId, occurrence: props.occurrence, __RequestVerificationToken: token() }
                }).done(function (result) {
                    if (result && result.success) {
                        onSuccess(result, isOpen(props));
                    } else if (isOpen(props)) {
                        $('#scheduledOccurrenceError').show();
                    }
                }).fail(function () {
                    if (isOpen(props)) {
                        $('#scheduledOccurrenceError').show();
                    }
                }).always(function () {
                    if (current === props) {
                        $('#scheduledOccurrenceModal .modal-footer button').prop('disabled', false);
                    }
                });
            }

            // After a change: the dialog closes if it is still this occurrence's, and the calendar is reloaded either way.
            function done(stillOpen) {
                if (stillOpen) {
                    $('#scheduledOccurrenceModal').modal('hide');
                }
                calendarInstance.refetchEvents();
            }

            function openOccurrence(props, title) {
                current = props;

                $('#scheduledOccurrenceTitle').text(props.scheduleName || title);
                $('#scheduledOccurrenceModal .modal-footer button').prop('disabled', false);
                $('#scheduledOccurrenceWhen').text(props.when || '');
                $('#scheduledOccurrenceAddress').text(props.address || '');
                $('#scheduledOccurrenceError').hide();

                var skipped = props.kind === 'skipped';
                var help;
                if (!props.canAct) {
                    help = text.readOnlyHelp;
                } else if (skipped) {
                    help = text.skippedHelp;
                } else if (props.kind === 'due') {
                    help = text.dueHelp;
                } else if (props.kind === 'missed') {
                    help = text.missedHelp;
                } else {
                    help = text.projectedHelp;
                }
                $('#scheduledOccurrenceHelp').text(help || '');

                $('#scheduledOccurrenceSend').toggle(props.canAct && !skipped);
                $('#scheduledOccurrenceSkip').toggle(props.canAct && !skipped);
                $('#scheduledOccurrenceCreate').toggle(props.canAct && !skipped);
                $('#scheduledOccurrenceRestore').toggle(props.canAct && skipped);

                if (props.canManage && props.editUrl) {
                    $('#scheduledOccurrenceEdit').attr('href', props.editUrl).show();
                } else {
                    $('#scheduledOccurrenceEdit').hide();
                }

                $('#scheduledOccurrenceModal').modal('show');
            }

            function render() {
                var calendarEl = document.getElementById('scheduledCallsCalendar');
                if (!calendarEl || typeof FullCalendar === 'undefined') return;

                if (calendarInstance) {
                    calendarInstance.updateSize();
                    return;
                }

                calendarInstance = new FullCalendar.Calendar(calendarEl, {
                    initialView: 'timeGridWeek',
                    headerToolbar: {
                        left: 'prev,next today',
                        center: 'title',
                        right: 'dayGridMonth,timeGridWeek,timeGridDay,listWeek'
                    },
                    height: 'auto',
                    // Dates and headings in the page's language (button labels stay English: no FullCalendar locale bundles are shipped).
                    locale: text.locale || undefined,
                    nowIndicator: true,
                    navLinks: true,
                    editable: false,
                    selectable: false,
                    dayMaxEvents: true,
                    slotMinTime: '00:00:00',
                    scrollTime: '06:00:00',
                    events: {
                        url: resgrid.absoluteBaseUrl + '/User/RecurringCalls/GetCalendarEvents',
                        success: function () {
                            $('#scheduledCalendarError').hide();
                        },
                        failure: function () {
                            // Shown on the calendar, not only the console: an empty calendar must not read as "nothing scheduled".
                            $('#scheduledCalendarError').show();
                        }
                    },
                    eventClick: function (info) {
                        var props = info.event.extendedProps || {};

                        if (props.kind === 'projected' || props.kind === 'skipped' || props.kind === 'due' || props.kind === 'missed') {
                            info.jsEvent.preventDefault();
                            openOccurrence(props, info.event.title);
                        } else if (info.event.url) {
                            info.jsEvent.preventDefault();
                            window.location.href = info.event.url;
                        }
                    }
                });

                calendarInstance.render();
            }

            $(document).ready(function () {
                if (!document.getElementById('scheduledCallsCalendar')) return;

                // FullCalendar measures its container, so it is drawn when its tab is shown (or straight away when it opens there).
                $('#scheduledCalendarTabLink').on('shown.bs.tab', function () {
                    resgrid.common.analytics.track('Dispatch Scheduled Calls Calendar');
                    render();
                });

                if ($('#scheduledCalendarTab').hasClass('active')) {
                    render();
                }

                $('#scheduledCalendarRetry').on('click', function (e) {
                    e.preventDefault();
                    $('#scheduledCalendarError').hide();
                    if (calendarInstance) calendarInstance.refetchEvents();
                });

                $('#scheduledOccurrenceSend').on('click', function () {
                    post('DispatchOccurrenceNow', function (result, stillOpen) {
                        if (result.url && stillOpen) {
                            window.location.href = result.url;
                        } else {
                            done(stillOpen);
                        }
                    });
                });

                $('#scheduledOccurrenceSkip').on('click', function () {
                    post('SkipOccurrence', function (result, stillOpen) {
                        done(stillOpen);
                    });
                });

                $('#scheduledOccurrenceRestore').on('click', function () {
                    post('RestoreOccurrence', function (result, stillOpen) {
                        done(stillOpen);
                    });
                });

                $('#scheduledOccurrenceCreate').on('click', function () {
                    post('CreateOccurrenceNow', function (result, stillOpen) {
                        if (result.url && stillOpen) {
                            window.location.href = result.url;
                        } else {
                            done(stillOpen);
                        }
                    });
                });
            });

            // Open recurring call alerts (missed, nobody to send, time to send): a banner pointing at the Recurring Calls page.
            function loadAlertCount() {
                if (!document.getElementById('recurringAlertsBanner')) return;

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/RecurringCalls/GetAlertCount',
                    type: 'GET',
                    cache: false
                }).done(function (result) {
                    var count = result && result.count ? result.count : 0;
                    if (count > 0) {
                        $('#recurringAlertsBannerText').text((text.alertsBanner || '{0}').replace('{0}', count));
                        $('#recurringAlertsBanner').show();
                    } else {
                        $('#recurringAlertsBanner').hide();
                    }
                });
            }

            $(document).ready(loadAlertCount);

            scheduledcalendar.getCalendar = function () { return calendarInstance; };
        })(scheduledcalendar = dispatch.scheduledcalendar || (dispatch.scheduledcalendar = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
