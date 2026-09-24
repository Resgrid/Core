var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var calendar;
        (function (calendar) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Shift Calendar');

                var calendarEl = document.getElementById('shiftCalendar');
                var cal = new FullCalendar.Calendar(calendarEl, {
                    initialView: 'dayGridMonth',
                    height: 'auto',
                    headerToolbar: {
                        left: 'prev,next today',
                        center: 'title',
                        right: 'dayGridMonth,timeGridWeek,timeGridDay'
                    },
                    // FullCalendar adds the visible range as start/end, so each view loads only its own days.
                    events: {
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftCalendarItemsForShift?shiftId=' + encodeURIComponent(shiftCalendarId),
                        method: 'GET',
                        failure: function () {
                            console.warn('Failed to load shift calendar items.');
                        }
                    },
                    // The site serializes JSON with PascalCase names, which FullCalendar does not read on its own.
                    eventDataTransform: function (item) {
                        return {
                            id: item.CalendarItemId,
                            title: item.Title,
                            start: item.Start,
                            end: item.End,
                            allDay: item.IsAllDay,
                            extendedProps: {
                                calendarItemId: item.CalendarItemId,
                                color: item.Color,
                                filled: item.Filled,
                                userSignedUp: item.UserSignedUp
                            }
                        };
                    },
                    eventClick: function (info) {
                        if (info.event.extendedProps && info.event.extendedProps.calendarItemId) {
                            window.location.href = resgrid.absoluteBaseUrl + '/User/Shifts/ViewShift?shiftDayId=' + info.event.extendedProps.calendarItemId;
                        }
                    },
                    eventDidMount: function (info) {
                        if (info.event.extendedProps && info.event.extendedProps.color) {
                            info.el.style.backgroundColor = info.event.extendedProps.color;
                        }
                    }
                });
                cal.render();
            });
        })(calendar = shifts.calendar || (shifts.calendar = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
