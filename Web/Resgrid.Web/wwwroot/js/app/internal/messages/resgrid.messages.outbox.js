var resgrid;
(function (resgrid) {
    var message;
    (function (message) {
        var outbox;
        (function (outbox) {
            var outboxTable;
            $(document).ready(function () {
                resgrid.common.analytics.track('Messaging - Outbox');
                outboxTable = $("#outboxMailList").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Messages/GetOutboxMessageList',
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
                        { data: 'Subject', title: 'Subject' },
                        { data: 'SentOn', title: 'Sent On' },
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
                outboxTable.on('draw', function () {
                    $('#outboxMailList thead th:first').html('<label><input type="checkbox" id="checkAllMessages"/></label>');
                });
                $(document).on('click', '#checkAllMessages', function () {
                    $('#outboxMailList tbody :checkbox').prop('checked', this.checked);
                });
            });
            function deleteMessages() {
                var values = $("input[name=message]:checked").map(function () { return this.value; }).get().join(",");
                if (values && values.length > 0) {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Messages/DeleteOutboxMessages?messageIds=' + values,
                        type: 'DELETE',
                        success: function () {
                            window.location.assign(resgrid.absoluteBaseUrl + '/User/Messages/Outbox');
                        }
                    });
                } else {
                    alert('You have no messages selected to delete');
                }
            }
            outbox.deleteMessages = deleteMessages;
        })(outbox = message.outbox || (message.outbox = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
