var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var reporting;
        (function (reporting) {
            var reportingTable;
            $(document).ready(function () {
                reportingTable = $("#reportingScheduleGrid").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Profile/GetScheduledReportingForGrid',
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        { data: 'Data', title: 'Report' },
                        { data: 'ScheduleType', title: 'Type' },
                        { data: 'IsActive', title: 'Active' },
                        { data: 'SpecifcDate', title: 'Date' },
                        { data: 'DaysOfWeek', title: 'Days Of Week' },
                        {
                            data: null,
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data, type, row) {
                                var html = '<a class="btn btn-sm btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Profile/EditReportingSchedule?scheduleId=' + row.ScheduleId + '">Edit</a> ';
                                if (row.IsActive) {
                                    html += '<a class="btn btn-sm btn-warning" onclick="resgrid.profile.reporting.deactivateSchedule(' + row.ScheduleId + ');">Deactivate</a> ';
                                } else {
                                    html += '<a class="btn btn-sm btn-success" onclick="resgrid.profile.reporting.activateSchedule(' + row.ScheduleId + ');">Activate</a> ';
                                }
                                html += '<a class="btn btn-sm btn-danger" onclick="resgrid.profile.reporting.deleteSchedule(' + row.ScheduleId + ');">Delete</a>';
                                return html;
                            }
                        }
                    ]
                });
                $('#checkAllSchedules').on('click', function () {
                    $('#reportingScheduleGrid').find(':checkbox').prop('checked', this.checked);
                });
            });
            function activateSchedule(scheduleId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Profile/ActivateSchedule?scheduleId=' + scheduleId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function () { refreshGrid(); });
            }
            reporting.activateSchedule = activateSchedule;
            function deactivateSchedule(scheduleId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Profile/DeactivateSchedule?scheduleId=' + scheduleId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function () { refreshGrid(); });
            }
            reporting.deactivateSchedule = deactivateSchedule;
            function deleteSchedule(scheduleId) {
                if (confirm("Are you sure you want to delete this schedule?")) {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Profile/DeleteSchedule?scheduleId=' + scheduleId,
                        contentType: 'application/json; charset=utf-8',
                        type: 'POST'
                    }).done(function () { refreshGrid(); });
                }
            }
            reporting.deleteSchedule = deleteSchedule;
            function refreshGrid() {
                if (reportingTable) {
                    reportingTable.ajax.reload();
                }
            }
            reporting.refreshGrid = refreshGrid;
        })(reporting = profile.reporting || (profile.reporting = {}));
    })(profile = resgrid.profile || (resgrid.profile = {}));
})(resgrid || (resgrid = {}));
