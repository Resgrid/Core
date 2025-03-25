
var userId;
var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var schedule;
        (function (schedule) {
            $(document).ready(function () {
                $("#staffingScheduleGrid").kendoGrid({
                    dataSource: {
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Profile/GetScheduledStaffingTasksForGrid?userId=' + userId
                        },
                        schema: {
                            model: {
                                fields: {
                                    SscheduleId: { type: "number" },
                                    ScheduleType: { type: "string" },
                                    IsActive: { type: "boolean" },
                                    SpecifcDate: { type: "date" },
                                    DaysOfWeek: { type: "string" },
                                    Data: { type: "string" }
                                }
                            }
                        },
                        pageSize: 50,
                        serverPaging: false,
                        serverFiltering: false,
                        serverSorting: false
                    },
                    height: 400,
                    filterable: true,
                    sortable: true,
                    pageable: true,
                    scrollable: true,
                    columns: [
                        {
                            field: "ScheduleType",
                            title: "Type",
                            width: 60
                        },
                        {
                            field: "IsActive",
                            title: "Active",
                            width: 50
                        },
                        {
                            field: "SpecifcDate",
                            title: "Date",
                            width: 100
                        },
                        {
                            field: "DaysOfWeek",
                            title: "Days Of Week",
                            width: 125
                        },
                        {
                            field: "Data",
                            title: "Staffing To",
                            width: 75
                        },
                        {
                            template: kendo.template($("#command-template").html()),
                            width: 150
                        }
                    ]
                });
                $('#checkAllSchedules').on('click', function () {
                    $('#staffingScheduleGrid').find(':checkbox').prop('checked', this.checked);
                });
            });
            function editSchedule(e) {
                e.preventDefault();
                var row = $(e.target).closest("tr");
                var item = $("#staffingScheduleGrid").data("kendoGrid").dataItem(row);
                window.location.assign(resgrid.absoluteBaseUrl + '/User/Profile/EditStaffingSchedule/' + item.get('ScheduleId'));
            }
            schedule.editSchedule = editSchedule;
            function activateSchedule(scheduleId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Profile/ActivateSchedule?scheduleId=' + scheduleId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    refreshGrid();
                });
            }
            schedule.activateSchedule = activateSchedule;
            function deactivateSchedule(scheduleId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Profile/DeactivateSchedule?scheduleId=' + scheduleId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    refreshGrid();
                });
            }
            schedule.deactivateSchedule = deactivateSchedule;
            function deleteSchedule(scheduleId) {
                var conf = confirm("Are you sure you want to delete this schedule?");
                if (conf == true) {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Profile/DeleteSchedule?scheduleId=' + scheduleId,
                        contentType: 'application/json; charset=utf-8',
                        type: 'POST'
                    }).done(function (results) {
                        refreshGrid();
                    });
                }
            }
            schedule.deleteSchedule = deleteSchedule;
            function refreshGrid() {
                var grid = $("#staffingScheduleGrid").data("kendoGrid");
                grid.dataSource.page(1);
                grid.dataSource.read();
            }
            schedule.refreshGrid = refreshGrid;
        })(schedule = profile.schedule || (profile.schedule = {}));
    })(profile = resgrid.profile || (resgrid.profile = {}));
})(resgrid || (resgrid = {}));
