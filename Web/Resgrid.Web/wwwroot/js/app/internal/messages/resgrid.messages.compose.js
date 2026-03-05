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

                // Replace kendoWindow with Bootstrap modal loaded via AJAX
                $('#selectRecipientWindow').on('show.bs.modal', function () {
                    var $body = $(this).find('.modal-body');
                    if ($body.is(':empty')) {
                        $body.load('/User/Department/RecipientsGrid', function () {
                            // listen for selectRecipient events from the loaded content
                        });
                    }
                });

                $(document).on('selectRecipient', function (e, data) {
                    $('#selectRecipientWindow').modal('hide');
                });

                function initSelect2(selector, placeholder, url) {
                    $(selector).select2({
                        placeholder: placeholder,
                        allowClear: true,
                        ajax: {
                            url: url,
                            dataType: 'json',
                            processResults: function (data) {
                                return {
                                    results: $.map(data, function (item) {
                                        return { id: item.Id, text: item.Name };
                                    })
                                };
                            }
                        }
                    });
                }

                $('#SendToAll').change(function () {
                    var disable = this.checked;
                    $('#SendToMatchOnly').prop('checked', false).prop('disabled', disable);
                    $('#groups, #roles, #users').prop('disabled', disable).trigger('change.select2');
                });

                $('#SendToMatchOnly').change(function () {
                    var checked = this.checked;
                    $('#SendToAll').prop('checked', false).prop('disabled', checked);
                    $('#groups, #roles').prop('disabled', false).trigger('change.select2');
                    $('#users').prop('disabled', checked).trigger('change.select2');
                });

                initSelect2('#groups', 'Select groups...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=1');
                initSelect2('#roles', 'Select roles...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=2');
                initSelect2('#users', 'Select users...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterSelf=true');
                initSelect2('#exludedShifts', 'Select shifts to exclude...', resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftsForDepartmentJson');

                $('#Message_ExpireOn').datetimepicker({ step: 15 });
                $("#Message_ExpireOn").keypress(function (e) { e.preventDefault(); });
            });

            function showRecipients() {
                $('#selectRecipientWindow').modal('show');
            }
            compose.showRecipients = showRecipients;

            function switchInputs(v) {
                var value = $('#MessageType').val();
                if (value) {
                    if (value == "Normal") {
                        $('#expiresBlock').hide();
                        $('#shiftsBlock').hide();
                        $('#exludedShifts').val(null).trigger('change');
                        $('#Message_ExpireOn').val('');
                    } else if (value == "Callback") {
                        $('#expiresBlock').show();
                        $('#shiftsBlock').show();
                    } else if (value == "Poll") {
                        $('#expiresBlock').show();
                        $('#shiftsBlock').hide();
                        $('#Message_ExpireOn').val('');
                        $('#exludedShifts').val(null).trigger('change');
                    }
                }
            }
            compose.switchInputs = switchInputs;
        })(compose = message.compose || (message.compose = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
