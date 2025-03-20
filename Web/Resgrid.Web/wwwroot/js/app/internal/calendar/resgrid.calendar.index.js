var resgrid;
(function (resgrid) {
    var calendar;
    (function (calendar) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Calendar');

                var date = new Date();
                var d = date.getDate();
                var m = date.getMonth();
                var y = date.getFullYear();

                $('#calendar').fullCalendar({
                    header: {
                        left: 'month,agendaWeek,agendaDay custom1',
                        center: 'title',
                        right: 'custom2 prevYear,prev,next,nextYear'
                    },
                    footer: {
                        left: 'custom1,custom2',
                        center: '',
                        right: 'prev,next'
                    },
                    defaultView: 'month',
                    editable: true,
                    droppable: false,
                    drop: function () {
                       
                    },
                    events: resgrid.absoluteBaseUrl + '/User/Calendar/GetV2CalendarEntriesForCal'
                });
            });

            
        })(index = calendar.index || (calendar.index = {}));
    })(calendar = resgrid.calendar || (resgrid.calendar = {}));
})(resgrid || (resgrid = {}));
