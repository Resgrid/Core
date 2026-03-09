var userId;
var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var schedule;
        (function (schedule) {
            var scheduleTable;
            $(document).ready(function () {
                scheduleTable = $("#staffingScheduleGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Profile/GetScheduledStaffingTasksForGrid?userId=' + userId,
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        { data: 'ScheduleType', title: 'Type' },
                        { data: 'IsActive', title: 'Active' },
                        { data: 'SpecifcDate', title: 'Date' },
                        { data: 'DaysOfWeek', title: 'Days Of Week' },
                        { data: 'Data', title: 'Staffing To' },
                        {
                            data: null,
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data, type, row) {
                                var html = '<a class="btn btn-sm btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Profile/EditStaffingSchedule/' + row.ScheduleId + '">Edit</a> ';
                                if (row.IsActive) {
                                    html += '<a class="btn btn-sm btn-warning" onclick="resgrid.profile.schedule.deactivateSchedule(' + row.ScheduleId + ');">Deactivate</a> ';
                                } else {
                                    html += '<a class="btn btn-sm btn-success" onclick="resgrid.profile.schedule.activateSchedule(' + row.ScheduleId + ');">Activate</a> ';
                                }
                                html += '<a class="btn btn-sm btn-danger" onclick="resgrid.profile.schedule.deleteSchedule(' + row.ScheduleId + ');">Delete</a>';
                                return html;
                            }
                        }
                    ]
                });
                $('#checkAllSchedules').on('click', function () {
                    $('#staffingScheduleGrid').find(':checkbox').prop('checked', this.checked);
                });
            });
            function activateSchedule(scheduleId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Profile/ActivateSchedule?scheduleId=' + scheduleId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function () { refreshGrid(); });
            }
            schedule.activateSchedule = activateSchedule;
            function deactivateSchedule(scheduleId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Profile/DeactivateSchedule?scheduleId=' + scheduleId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function () { refreshGrid(); });
            }
            schedule.deactivateSchedule = deactivateSchedule;
            function deleteSchedule(scheduleId) {
                if (confirm("Are you sure you want to delete this schedule?")) {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Profile/DeleteSchedule?scheduleId=' + scheduleId,
                        contentType: 'application/json; charset=utf-8',
                        type: 'POST'
                    }).done(function () { refreshGrid(); });
                }
            }
            schedule.deleteSchedule = deleteSchedule;
            function refreshGrid() {
                if (scheduleTable) {
                    scheduleTable.ajax.reload();
                }
            }
            schedule.refreshGrid = refreshGrid;
        })(schedule = profile.schedule || (profile.schedule = {}));
    })(profile = resgrid.profile || (resgrid.profile = {}));
})(resgrid || (resgrid = {}));
