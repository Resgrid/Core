var resgrid;
(function (resgrid) {
    var message;
    (function (message) {
        var inbox;
        (function (inbox) {
            var inboxTable;
            $(document).ready(function () {
                resgrid.common.analytics.track('Messaging - Inbox');
                inboxTable = $("#inboxMailList").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Messages/GetInboxMessageList',
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        {
                            data: 'MessageId',
                            title: '',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<input type="checkbox" id="message" name="message" value="' + data + '" />';
                            }
                        },
                        {
                            data: 'Subject',
                            title: 'Subject',
                            render: function (data, type, row) {
                                return row.Read ? data : '<strong>' + data + '</strong>';
                            }
                        },
                        {
                            data: 'SentBy',
                            title: 'Sent By',
                            render: function (data, type, row) {
                                return row.Read ? data : '<strong>' + data + '</strong>';
                            }
                        },
                        {
                            data: 'SentOn',
                            title: 'Sent On',
                            render: function (data, type, row) {
                                return row.Read ? data : '<strong>' + data + '</strong>';
                            }
                        },
                        {
                            data: 'MessageId',
                            title: 'Actions',
                            orderable: false,
                            searchable: false,
                            render: function (data) {
                                return '<a class="btn btn-sm btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Messages/View?messageId=' + data + '">View</a>';
                            }
                        }
                    ]
                });
                inboxTable.on('draw', function () {
                    $('#inboxMailList thead th:first').html('<label><input type="checkbox" id="checkAllMessages"/></label>');
                });
                $(document).on('click', '#checkAllMessages', function () {
                    $('#inboxMailList tbody :checkbox').prop('checked', this.checked);
                });
            });
            function deleteMessages() {
                var values = $("input[name=message]:checked").map(function () { return this.value; }).get().join(",");
                if (values && values.length > 0) {
                    swal({
                        title: "Are you sure?",
                        text: "Do you want to permanently delete the selected messages.",
                        icon: "warning",
                        buttons: true,
                        dangerMode: true
                    }).then((willDelete) => {
                        if (willDelete) {
                            resgrid.showProgress('#inboxMailList', true);
                            $.ajax({
                                url: resgrid.absoluteBaseUrl + '/User/Messages/DeleteMessages?messageIds=' + values,
                                type: 'DELETE',
                                success: function () {
                                    resgrid.showProgress('#inboxMailList', false);
                                    window.location.assign(resgrid.absoluteBaseUrl + '/User/Messages/Inbox');
                                }
                            });
                        }
                    });
                } else {
                    alert('You have no messages selected to delete');
                }
            }
            inbox.deleteMessages = deleteMessages;
            function markMessagesAsRead() {
                var values = $("input[name=message]:checked").map(function () { return this.value; }).get().join(",");
                if (values && values.length > 0) {
                    swal({
                        title: "Are you sure?",
                        text: "Do you want to mark the selected messages as read?",
                        icon: "warning",
                        buttons: true,
                        dangerMode: true
                    }).then((willDelete) => {
                        if (willDelete) {
                            resgrid.showProgress('#inboxMailList', true);
                            $.ajax({
                                url: resgrid.absoluteBaseUrl + '/User/Messages/MarkMessagesAsRead?messageIds=' + values,
                                type: 'PUT',
                                success: function () {
                                    resgrid.showProgress('#inboxMailList', false);
                                    window.location.assign(resgrid.absoluteBaseUrl + '/User/Messages/Inbox');
                                }
                            });
                        }
                    });
                } else {
                    alert('You have no messages selected to mark as read');
                }
            }
            inbox.markMessagesAsRead = markMessagesAsRead;
        })(inbox = message.inbox || (message.inbox = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
