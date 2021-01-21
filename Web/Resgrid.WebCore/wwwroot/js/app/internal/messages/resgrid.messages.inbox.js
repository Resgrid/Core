
var resgrid;
(function (resgrid) {
    var message;
    (function (message) {
        var inbox;
        (function (inbox) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Messaging - Inbox');
                $("#inboxMailList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Messages/GetInboxMessageList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    MessageId: { type: "number" },
                                    Subject: { type: "string" },
                                    Body: { type: "string" },
                                    SentBy: { type: "string" },
                                    SentOn: { type: "string" },
                                    ReadOn: { type: "string" },
                                    Type: { type: "number" },
                                    SystemGenerated: { type: "boolean" },
                                    IsDeleted: { type: "boolean" },
                                    Read: { type: "boolean" }
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
                        {
                            field: "Subject",
                            title: "Subject",
                            template: "#if(Read){# #=Subject# #}else{# <strong>#=Subject#</strong> #}#"
                        },
                        {
                            field: "SentBy",
                            title: "Sent By",
                            template: "#if(Read){# #=SentBy# #}else{# <strong>#=SentBy#</strong> #}#"
                        },
                        {
                            field: "SentOn",
                            title: "Sent On",
                            template: "#if(Read){# #=SentOn# #}else{# <strong>#=SentOn#</strong> #}#"
                        },
                        {
                            field: "MessageId",
                            title: "Actions",
                            filterable: false,
                            sortable: false,
                            template: kendo.template($("#inboxCommand-template").html())
                        }
                    ]
                });
                $('#checkAllMessages').on('click', function () {
                    $('#inboxMailList').find(':checkbox').prop('checked', this.checked);
                });
            });
            function deleteMessages() {
                var values = $("input[name=message]:checked").map(function () {
                    return this.value;
                }).get().join(",");

                if (values && values.length > 0) {
                    swal({
                        title: "Are you sure?",
                        text: "Do you want to permanently delete the selected messages.",
                        icon: "warning",
                        buttons: true,
                        dangerMode: true
                    }).then((willDelete) => {
                        if (willDelete) {
                            kendo.ui.progress($("#inboxPageList"), true);
                            $.ajax({
                                url: resgrid.absoluteBaseUrl + '/User/Messages/DeleteMessages?messageIds=' + values,
                                type: 'DELETE',
                                success: function (result) {
                                    kendo.ui.progress($("#inboxPageList"), false);
                                    window.location.assign(resgrid.absoluteBaseUrl + '/User/Messages/Inbox');
                                }
                            });
                        }
                    });
                }
                else {
                    alert('You have no messages selected to delete');
                }
            }
            inbox.deleteMessages = deleteMessages;

            function markMessagesAsRead() {
                var values = $("input[name=message]:checked").map(function () {
                    return this.value;
                }).get().join(",");
                if (values && values.length > 0) {
                    swal({
                        title: "Are you sure?",
                        text: "Do you want to mark the selected messages as read?",
                        icon: "warning",
                        buttons: true,
                        dangerMode: true
                    }).then((willDelete) => {
                        if (willDelete) {
                            kendo.ui.progress($("#inboxPageList"), true);
                            $.ajax({
                                url: resgrid.absoluteBaseUrl + '/User/Messages/MarkMessagesAsRead?messageIds=' + values,
                                type: 'PUT',
                                success: function (result) {
                                    kendo.ui.progress($("#inboxPageList"), false);
                                    window.location.assign(resgrid.absoluteBaseUrl + '/User/Messages/Inbox');
                                }
                            });
                        }
                    });
                }
                else {
                    alert('You have no messages selected to mark as read');
                }
            }
            inbox.markMessagesAsRead = markMessagesAsRead;
        })(inbox = message.inbox || (message.inbox = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
