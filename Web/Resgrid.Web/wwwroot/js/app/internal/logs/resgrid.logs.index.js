var resgrid;
(function (resgrid) {
    var logs;
    (function (logs) {
        var index;
        (function (index) {
            var logsTable;
            $(document).ready(function () {
                resgrid.common.analytics.track('Logs List');
                logsTable = $("#logsIndexList").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Logs/GetLogsList?year=' + $("#Year").val(),
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        { data: 'Type', title: 'Type' },
                        { data: 'Group', title: 'Group' },
                        { data: 'LoggedBy', title: 'Logged By' },
                        { data: 'LoggedOn', title: 'Logged On' },
                        { data: 'Narrative', title: 'Narrative', visible: false, searchable: true },
                        { data: 'SearchTerms', title: 'SearchTerms', visible: false, searchable: true },
                        {
                            data: 'LogId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data, type, row) {
                                var html = '<a class="btn btn-sm btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Logs/View?logId=' + data + '">View</a> ';
                                if (row.CanDelete) {
                                    html += '<a class="btn btn-sm btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Logs/DeleteWorkLog?logId=' + data + '">Delete</a>';
                                }
                                return html;
                            }
                        }
                    ]
                });
                $("#Year").change(function () {
                    logsTable.ajax.url(resgrid.absoluteBaseUrl + '/User/Logs/GetLogsList?year=' + $(this).val()).load();
                });
            });
        })(index = logs.index || (logs.index = {}));
    })(logs = resgrid.logs || (resgrid.logs = {}));
})(resgrid || (resgrid = {}));
