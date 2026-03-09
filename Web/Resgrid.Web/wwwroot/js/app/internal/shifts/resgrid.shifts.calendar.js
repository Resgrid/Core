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
                    events: {
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftCalendarItemsForShift?shiftId=' + shiftCalendarId,
                        method: 'GET',
                        failure: function () {
                            console.warn('Failed to load shift calendar items.');
                        }
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
