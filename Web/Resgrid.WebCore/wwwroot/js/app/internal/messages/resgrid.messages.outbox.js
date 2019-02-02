
var resgrid;
(function (resgrid) {
    var message;
    (function (message) {
        var outbox;
        (function (outbox) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Messaging - Outbox');
                $("#outboxMailList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Messages/GetOutboxMessageList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    MessageId: { type: "number" },
                                    Subject: { type: "string" },
                                    Body: { type: "string" },
                                    SentOn: { type: "string" },
                                    ReadOn: { type: "string" },
                                    Type: { type: "number" },
                                    SystemGenerated: { type: "boolean" },
                                    IsDeleted: { type: "boolean" }
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
                            field: "MessageId",
                            title: "",
                            width: 28,
                            filterable: false,
                            sortable: false,
                            headerTemplate: '<label><input type="checkbox" id="checkAllMessages"/></label>',
                            template: "<input type=\"checkbox\" id=\"message\" name=\"message\" value=\"#=MessageId#\" />"
                        },
                        "Subject",
                        {
                            field: "SentOn",
                            title: "Sent On"
                        },
                        {
                            field: "MessageId",
                            title: "Actions",
                            filterable: false,
                            sortable: false,
                            template: kendo.template($("#outboxCommand-template").html())
                        }
                    ]
                });
                $('#checkAllMessages').on('click', function () {
                    $('#outboxMailList').find(':checkbox').prop('checked', this.checked);
                });
            });
            function deleteMessages() {
                var values = $("input[name=message]:checked").map(function () {
                    return this.value;
                }).get().join(",");
                if (values && values.length > 0) {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Messages/DeleteOutboxMessages?messageIds=' + values,
                        type: 'DELETE',
                        success: function (result) {
                            window.location.assign(resgrid.absoluteBaseUrl + '/User/Messages/Outbox');
                        }
                    });
                }
                else {
                    alert('You have no messages selected to delete');
                }
            }
            outbox.deleteMessages = deleteMessages;
        })(outbox = message.outbox || (message.outbox = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
