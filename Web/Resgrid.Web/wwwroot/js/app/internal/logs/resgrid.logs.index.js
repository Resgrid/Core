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
                                    html += '<button type="button" class="btn btn-sm btn-danger log-delete" data-log-id="' + data + '">Delete</button>';
                                }
                                return html;
                            }
                        }
                    ]
                });
                // DeleteWorkLog is POST + antiforgery: confirm, then submit the page's token form.
                $(document).on('click', '.log-delete', function (e) {
                    e.preventDefault();

                    var message = $('#logsIndexList').attr('data-delete-confirm');
                    if (message && !window.confirm(message))
                        return;

                    var form = document.getElementById('deleteLogForm');
                    form.elements.namedItem('logId').value = $(this).attr('data-log-id');
                    form.submit();
                });
                $("#Year").change(function () {
                    logsTable.ajax.url(resgrid.absoluteBaseUrl + '/User/Logs/GetLogsList?year=' + $(this).val()).load();
                });
            });
        })(index = logs.index || (logs.index = {}));
    })(logs = resgrid.logs || (resgrid.logs = {}));
})(resgrid || (resgrid = {}));
