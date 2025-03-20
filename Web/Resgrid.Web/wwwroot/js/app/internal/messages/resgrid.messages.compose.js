
var resgrid;
(function (resgrid) {
    var message;
    (function (message) {
        var compose;
        (function (compose) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Messaging - Compose');

                var quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#messageForm', function () {
                    $('#Message_Body').val(quill.root.innerHTML);

                    return true;
                });

                $('#MessageType').on("change", function (e) { switchInputs(e); });
                compose.wndRecipients = $("#selectRecipientWindow")
                    .kendoWindow({
                    title: "Select Recipient",
                    modal: true,
                    visible: false,
                    resizable: false,
                    content: '/User/Department/RecipientsGrid',
                    width: 750,
                    height: 465
                }).data("kendoWindow");
                $('.selectRecipientWindow').on('selectRecipient', function (e, data) {
                    compose.wndRecipients.close();
                });
                $('#SendToAll').change(function () {
                    if (this.checked) {
                        $('#SendToMatchOnly').prop('checked', false);
                        $('#SendToMatchOnly').prop('disabled', true);
                        $('#groups').data("kendoMultiSelect").enable(false);
                        $('#roles').data("kendoMultiSelect").enable(false);
                        $('#users').data("kendoMultiSelect").enable(false);
                    }
                    else {
                        $('#SendToMatchOnly').prop('disabled', false);
                        $('#groups').data("kendoMultiSelect").enable(true);
                        $('#roles').data("kendoMultiSelect").enable(true);
                        $('#users').data("kendoMultiSelect").enable(true);
                    }
                });
                $('#SendToMatchOnly').change(function () {
                    if (this.checked) {
                        $('#SendToAll').prop('checked', false);
                        $('#SendToAll').prop('disabled', true);
                        $('#groups').data("kendoMultiSelect").enable(true);
                        $('#roles').data("kendoMultiSelect").enable(true);
                        $('#users').data("kendoMultiSelect").enable(false);
                    }
                    else {
                        $('#SendToAll').prop('disabled', false);
                        $('#groups').data("kendoMultiSelect").enable(true);
                        $('#roles').data("kendoMultiSelect").enable(true);
                        $('#users').data("kendoMultiSelect").enable(true);
                    }
                });
                $("#groups").kendoMultiSelect({
                    placeholder: "Select groups...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=1'
                        }
                    }
                });
                $("#roles").kendoMultiSelect({
                    placeholder: "Select roles...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=2'
                        }
                    }
                });
                $("#users").kendoMultiSelect({
                    placeholder: "Select users...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterSelf=true'
                        }
                    }
                });
                $("#exludedShifts").kendoMultiSelect({
                    placeholder: "Select shifts to exclude...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftsForDepartmentJson'
                        }
                    }
                });
                $('#Message_ExpireOn').datetimepicker({ step: 15 });
                $("#Message_ExpireOn").keypress(function (e) {
                    e.preventDefault();
                });
            });
            function showRecipients() {
                compose.wndRecipients.center().open();
            }
            compose.showRecipients = showRecipients;
            function switchInputs(v) {
                var value = $('#MessageType').val();
                if (value) {
                    if (value == "Normal") {
                        $('#expiresBlock').hide();
                        $('#shiftsBlock').hide();
                        var multiSelect = $('#exludedShifts').data("kendoMultiSelect");
                        multiSelect.value([]);
                        $('#Message_ExpireOn').val('');
                    }
                    else if (value == "Callback") {
                        $('#expiresBlock').show();
                        $('#shiftsBlock').show();
                    }
                    else if (value == "Poll") {
                        $('#expiresBlock').show();
                        $('#shiftsBlock').hide();
                        $('#Message_ExpireOn').val('');
                        var multiSelect = $('#exludedShifts').data("kendoMultiSelect");
                        multiSelect.value([]);
                    }
                }
            }
            compose.switchInputs = switchInputs;
        })(compose = message.compose || (message.compose = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
