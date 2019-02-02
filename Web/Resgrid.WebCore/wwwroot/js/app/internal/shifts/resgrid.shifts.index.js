
var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Index');
                $("#shiftCalendar").kendoScheduler({
                    date: new Date(),
                    startTime: new Date(),
                    height: 2250,
                    showWorkHours: false,
                    views: [
                        {
                            type: "day",
                            startTime: new Date("2013/6/6 00:00"),
                            endTime: new Date("2013/6/6 24:00")
                        },
                        {
                            type: "week",
                            startTime: new Date("2013/6/6 00:00"),
                            endTime: new Date("2013/6/6 24:00")
                        },
                        {
                            type: "month", selected: true, eventHeight: 200
                        }
                    ],
                    toolbar: ["pdf"],
                    pdf: {
                        fileName: "Resgrid Shifts Calendar.pdf",
                        proxyURL: resgrid.absoluteBaseUrl + "/User/Shifts/GetShiftCalendarItems"
                    },
                    footer: {
                        command: false
                    },
                    //timezone: '@Model.TimeZone',
                    //editable: false,
                    editable: {
                        destroy: false,
                        create: false,
                        move: false,
                        resize: false,
                        window: {
                            open: {
                                duration: 10000
                            },
                            visible: false
                        }
                    },
                    eventTemplate: $("#shift-template").html(),
                    edit: function (e) {
                        var data = e;
                        window.location.href = resgrid.absoluteBaseUrl + '/User/Shifts/ViewShift?shiftDayId=' + e.event.calendarItemId;
                    },
                    dataSource: {
                        batch: true,
                        transport: {
                            read: {
                                url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftCalendarItems',
                                dataType: "json"
                            },
                            parameterMap: function (options, operation) {
                                if (operation !== "read" && options.models) {
                                    return kendo.stringify(options.models[0]);
                                }
                            }
                        },
                        schema: {
                            model: {
                                id: "calendarItemId",
                                fields: {
                                    calendarItemId: { from: "CalendarItemId", type: "number" },
                                    title: { from: "Title", defaultValue: "No title", validation: { required: true } },
                                    start: { type: "date", from: "Start" },
                                    end: { type: "date", from: "End" },
                                    startTimezone: { from: "StartTimezone" },
                                    endTimezone: { from: "EndTimezone" },
                                    description: { from: "Description" },
                                    recurrenceId: { from: "RecurrenceId" },
                                    recurrenceRule: { from: "RecurrenceRule" },
                                    recurrenceException: { from: "RecurrenceException" },
                                    itemType: { from: "ItemType" },
                                    signupType: { from: "SignupType", type: "number" },
                                    isAllDay: { type: "boolean", from: "IsAllDay" },
                                    filled: { type: "boolean", from: "Filled" },
                                    shiftId: { from: "ShiftId", type: "number" },
                                    userSignedUp: { type: "boolean", from: "UserSignedUp" }
                                }
                            }
                        }
                    },
                    resources: [
                        {
                            field: "itemType",
                            title: "Type",
                            dataSource: {
                                batch: true,
                                transport: {
                                    read: {
                                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftCalendarItemTypes',
                                        dataType: "json"
                                    }
                                },
                                schema: {
                                    model: {
                                        fields: {
                                            value: { from: "CalendarItemTypeId", type: "number" },
                                            text: { from: "Name" },
                                            color: { from: "Color" }
                                        }
                                    }
                                }
                            }
                        }
                    ]
                });
            });
        })(index = shifts.index || (shifts.index = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
