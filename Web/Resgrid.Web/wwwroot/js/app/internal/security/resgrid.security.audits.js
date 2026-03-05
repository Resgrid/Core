var resgrid;
(function (resgrid) {
    var security;
    (function (security) {
        var audits;
        (function (audits) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Security Audits');

                var table = $("#auditLogsList").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Security/GetAuditLogsList',
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        {
                            data: 'AuditLogId',
                            title: '',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<input type="checkbox" id="selectAuditLog_' + data + '" name="selectAuditLog_' + data + '" />';
                            }
                        },
                        { data: 'Timestamp', title: 'Timestamp' },
                        { data: 'Type', title: 'Type' },
                        { data: 'Message', title: 'Message' },
                        {
                            data: 'AuditLogId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<a class="btn btn-sm btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Security/ViewAuditLog?auditLogId=' + data + '">View</a>';
                            }
                        }
                    ]
                });

                table.on('draw', function () {
                    $('#auditLogsList thead th:first').html('<label><input type="checkbox" id="checkAllAuditLogs"/></label>');
                });

                $(document).on('click', '#checkAllAuditLogs', function () {
                    $('#auditLogsList tbody :checkbox').prop('checked', this.checked);
                });
            });
        })(audits = security.audits || (security.audits = {}));
    })(security = resgrid.security || (resgrid.security = {}));
})(resgrid || (resgrid = {}));
