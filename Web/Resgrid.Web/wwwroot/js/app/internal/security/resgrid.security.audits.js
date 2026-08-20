var resgrid;
(function (resgrid) {
    var security;
    (function (security) {
        var audits;
        (function (audits) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Security Audits');

                var textRenderer = $.fn.dataTable.render.text();
                var table = $("#auditLogsList").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Security/GetAuditLogsList',
                        dataSrc: ''
                    },
                    pageLength: 50,
                    order: [[1, 'desc']],
                    language: {
                        search: 'Search audit logs:',
                        searchPlaceholder: 'Name, ID, email, date/time, or type'
                    },
                    initComplete: function () {
                        var api = this.api();
                        var typeColumn = api.column('auditType:name');
                        var typeFilter = $('#auditLogTypeFilter');

                        typeColumn.data().unique().sort().each(function (type) {
                            if (type) {
                                $('<option>').val(type).text(type).appendTo(typeFilter);
                            }
                        });
                    },
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
                        {
                            data: 'Timestamp',
                            title: 'Timestamp',
                            render: function (data, type, row) {
                                if (type === 'sort' || type === 'type') {
                                    return row.TimestampSort == null ? -1 : row.TimestampSort;
                                }

                                if (type === 'display' || type === 'filter') {
                                    return textRenderer[type](data);
                                }

                                return data;
                            }
                        },
                        { data: 'Type', name: 'auditType', title: 'Type', render: textRenderer },
                        { data: 'Name', title: 'Logged By', render: textRenderer },
                        {
                            data: 'Successful',
                            title: 'Result',
                            render: function (data, type) {
                                if (type === 'display') {
                                    return data
                                        ? '<span class="label label-success">Successful</span>'
                                        : '<span class="label label-danger">Failed</span>';
                                }

                                if (type === 'filter') {
                                    return data ? 'Successful' : 'Failed';
                                }

                                return data ? 1 : 0;
                            }
                        },
                        { data: 'Message', title: 'Message', render: textRenderer },
                        {
                            data: 'SearchTerms',
                            title: 'Search Terms',
                            visible: false,
                            searchable: true,
                            orderable: false
                        },
                        {
                            data: 'AuditLogId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<a class="btn btn-sm btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Security/ViewAudit?auditLogId=' + data + '">View</a>';
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

                $('#auditLogTypeFilter').on('change', function () {
                    var selectedType = $.fn.dataTable.util.escapeRegex($(this).val());
                    table.column('auditType:name')
                        .search(selectedType ? '^' + selectedType + '$' : '', true, false)
                        .draw();
                });
            });
        })(audits = security.audits || (security.audits = {}));
    })(security = resgrid.security || (resgrid.security = {}));
})(resgrid || (resgrid = {}));
