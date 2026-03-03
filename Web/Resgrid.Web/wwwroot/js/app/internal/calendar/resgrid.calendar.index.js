var resgrid;
(function (resgrid) {
    var calendar;
    (function (calendar) {
        var index;
        (function (index) {
            var calendarInstance = null;

            $(document).ready(function () {
                resgrid.common.analytics.track('Calendar');

                var calendarEl = document.getElementById('calendar');
                if (!calendarEl) return;

                calendarInstance = new FullCalendar.Calendar(calendarEl, {
                    plugins: [],   // bundled in index.global.min.js — no separate plugin imports needed
                    initialView: 'dayGridMonth',
                    headerToolbar: {
                        left: 'prev,next today',
                        center: 'title',
                        right: 'dayGridMonth,timeGridWeek,timeGridDay,listWeek'
                    },
                    height: 'auto',
                    nowIndicator: true,
                    navLinks: true,
                    editable: false,
                    selectable: false,
                    dayMaxEvents: true,   // show "+N more" when too many events
                    events: {
                        url: resgrid.absoluteBaseUrl + '/User/Calendar/GetV2CalendarEntriesForCal',
                        failure: function () {
                            console.error('Failed to load calendar events.');
                        }
                    },
                    // Map the JSON fields returned by GetV2CalendarEntriesForCal to FullCalendar fields.
                    eventDataTransform: function (rawEvent) {
                        return {
                            id: rawEvent.id,
                            title: rawEvent.title,
                            start: rawEvent.start,
                            end: rawEvent.end,
                            allDay: rawEvent.allDay || false,
                            url: rawEvent.url,
                            backgroundColor: rawEvent.backgroundColor || '#1e90ff',
                            borderColor: rawEvent.backgroundColor || '#1e90ff'
                        };
                    },
                    eventClick: function (info) {
                        if (info.event.url) {
                            info.jsEvent.preventDefault();
                            window.location.href = info.event.url;
                        }
                    }
                });

                calendarInstance.render();
            });

            index.getCalendar = function () { return calendarInstance; };
        })(index = calendar.index || (calendar.index = {}));
    })(calendar = resgrid.calendar || (resgrid.calendar = {}));
})(resgrid || (resgrid = {}));
