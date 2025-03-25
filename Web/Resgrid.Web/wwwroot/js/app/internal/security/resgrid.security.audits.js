
var resgrid;
(function (resgrid) {
    var security;
    (function (security) {
        var audits;
        (function (audits) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Security Audits');
                $("#auditLogsList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Security/GetAuditLogsList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    AuditLogId: { type: "number" },
                                    Type: { type: "string" },
                                    Timestamp: { type: "string" },
                                    Message: { type: "string" }
                                }
                            }
                        },
                        pageSize: 50
                    },
                    //height: 400,
                    filterable: true,
                    sortable: true,
                    scrollable: true,
                    pageable: {
                        refresh: true,
                        pageSizes: true,
                        buttonCount: 5
                    },
                    columns: [
                        {
                            field: "AuditLogId",
                            title: "",
                            width: 25,
                            filterable: false,
                            sortable: false,
                            headerTemplate: '<label><input type="checkbox" id="checkAllAuditLogs"/></label>',
                            template: "<input type=\"checkbox\" id=\"selectAuditLog_#=AuditLogId#\" name=\"selectAuditLog_#=AuditLogId#\" />"
                        },
                        {
                            field: "Timestamp",
                            title: "Timestamp",
                            width: 250
                        },
                        {
                            field: "Type",
                            title: "Type",
                            width: 250
                        },
                        "Message",
                        {
                            field: "AuditLogId",
                            title: "Actions",
                            filterable: false,
                            width: 100,
                            template: kendo.template($("#auditCommand-template").html())
                        }
                    ]
                });
                $('#checkAllAuditLogs').on('click', function () {
                    $('#auditLogsList').find(':checkbox').prop('checked', this.checked);
                });
            });
        })(audits = security.audits || (security.audits = {}));
    })(security = resgrid.security || (resgrid.security = {}));
})(resgrid || (resgrid = {}));
