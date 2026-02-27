var resgrid;
(function (resgrid) {
    var home;
    (function (home) {
        var edituserprofile;
        (function (edituserprofile) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Edit User Profile');
                $('#Profile_TimeZone').select2();
                $('#Carrier').select2();
                $('#UserGroup').select2();
                $('#PhysicalCountry').select2();
                $('#MailingCountry').select2();
                $("#edit_user").validate({
                    errorClass: "help-inline",
                    errorElement: "span",
                    highlight: function (element, errorClass, validClass) {
                        $(element).parents('.form-group').removeClass('has-success').addClass('has-error');
                    },
                    unhighlight: function (element, errorClass, validClass) {
                        $(element).parents('.form-group').removeClass('has-error').addClass('has-success');
                    }
                });
                $('#MailingAddressSameAsPhysical').change(function () {
                    if (this.checked) {
                        disableMailingAddressControls();
                    }
                    else {
                        enableMailingAddressControls();
                    }
                });
                if (sameAddress === 'True') {
                    disableMailingAddressControls();
                }
                $("#roles").kendoMultiSelect({
                    placeholder: "Select roles...",
                    dataTextField: "Name",
                    dataValueField: "RoleId",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles'
                        }
                    }
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Personnel/GetRolesForUser?userId=' + $('#UserId').val(),
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        var multiSelect = $("#roles").data("kendoMultiSelect");

                        if (multiSelect) {
                            var valuesToAdd = [];
                            for (var i = 0; i < data.length; i++) {
                                valuesToAdd.push(data[i].RoleId);
                            }
                            multiSelect.value(valuesToAdd);
                        }
                    }
                });

                // ── Contact Verification ──────────────────────────────────────────────
                // Map contact type int → widget element IDs
                var verifyMap = {
                    0: { sendBtn: '.rg-email-send-code', codeEntry: '.rg-email-code-entry', codeInput: '.rg-email-verify-code', confirmBtn: '.rg-email-confirm-code', msgSpan: '.rg-email-verify-msg', alert: '#emailVerifyAlert' },
                    1: { sendBtn: '#btnSendMobileCode',  codeEntry: '#mobileCodeEntry',  codeInput: '#mobileVerifyCode',  confirmBtn: '#btnConfirmMobileCode',  msgSpan: '#mobileVerifyMsg',  alert: '#mobileVerifyAlert'  },
                    2: { sendBtn: '#btnSendHomeCode',    codeEntry: '#homeCodeEntry',    codeInput: '#homeVerifyCode',    confirmBtn: '#btnConfirmHomeCode',    msgSpan: '#homeVerifyMsg',    alert: '#homeVerifyAlert'    }
                };

                // Read CSRF token from the hidden field rendered by @Html.AntiForgeryToken()
                function getAntiForgeryToken() {
                    return $('input[name="__RequestVerificationToken"]').val();
                }

                // Send verification code button clicked
                $(document).on('click', '.rg-send-code', function () {
                    var contactType = parseInt($(this).data('contact-type'), 10);
                    var w = verifyMap[contactType];
                    if (!w) return;

                    var $btn = $(w.sendBtn);
                    $btn.prop('disabled', true).text('Sending…');
                    $(w.msgSpan).hide().text('');

                    $.ajax({
                        url: rgVerifySendUrl,
                        type: 'POST',
                        contentType: 'application/json',
                        headers: { 'RequestVerificationToken': getAntiForgeryToken() },
                        data: JSON.stringify({ type: contactType })
                    }).done(function (result) {
                        if (result && result.success) {
                            $(w.msgSpan).text(rgVerifyLabels.sent).css('color', '#31708f').show();
                            $(w.codeEntry).show();
                            $btn.hide();
                        } else {
                            $(w.msgSpan).text(rgVerifyLabels.rateLimited).css('color', '#a94442').show();
                            $btn.prop('disabled', false).text('Verify');
                        }
                    }).fail(function () {
                        $(w.msgSpan).text(rgVerifyLabels.failed).css('color', '#a94442').show();
                        $btn.prop('disabled', false).text('Verify');
                    });
                });

                // Confirm verification code button clicked
                $(document).on('click', '.rg-confirm-code', function () {
                    var contactType = parseInt($(this).data('contact-type'), 10);
                    var w = verifyMap[contactType];
                    if (!w) return;

                    var code = $(w.codeInput).val().trim();
                    if (!code || code.length < 4) {
                        $(w.msgSpan).text(rgVerifyLabels.failed).css('color', '#a94442').show();
                        return;
                    }

                    var $btn = $(w.confirmBtn);
                    $btn.prop('disabled', true).text('Checking…');
                    $(w.msgSpan).hide().text('');

                    $.ajax({
                        url: rgVerifyConfirmUrl,
                        type: 'POST',
                        contentType: 'application/json',
                        headers: { 'RequestVerificationToken': getAntiForgeryToken() },
                        data: JSON.stringify({ type: contactType, code: code })
                    }).done(function (result) {
                        if (result && result.success) {
                            $(w.msgSpan).text(rgVerifyLabels.success).css('color', '#3c763d').show();
                            // Collapse the verify widget and replace the alert with a success badge
                            $(w.codeEntry).hide();
                            $(w.alert)
                                .removeClass('alert-warning alert-info')
                                .addClass('alert-success')
                                .html('<i class="fa fa-check-circle"></i> ' + rgVerifyLabels.success);
                            // Swap the exclamation/info icon in the input-group-addon to a green check
                            $(w.alert).closest('.col-sm-10').find('.input-group-addon i')
                                .removeClass('fa-exclamation-circle fa-info-circle text-danger text-muted')
                                .addClass('fa-check-circle text-success');
                        } else {
                            $(w.msgSpan).text(rgVerifyLabels.failed).css('color', '#a94442').show();
                            $btn.prop('disabled', false).text('Save');
                        }
                    }).fail(function () {
                        $(w.msgSpan).text(rgVerifyLabels.failed).css('color', '#a94442').show();
                        $btn.prop('disabled', false).text('Save');
                    });
                });
                // ── End Contact Verification ──────────────────────────────────────────
            });

            function disableMailingAddressControls() {
                $("#MailingAddress1").attr("disabled", "disabled");
                $("#MailingAddress2").attr("disabled", "disabled");
                $("#MailingCity").attr("disabled", "disabled");
                $("#MailingState").attr("disabled", "disabled");
                $("#MailingPostalCode").attr("disabled", "disabled");
                $("#MailingCountry").attr("disabled", "disabled");
            }
            edituserprofile.disableMailingAddressControls = disableMailingAddressControls;
            function enableMailingAddressControls() {
                $("#MailingAddress1").removeAttr("disabled");
                $("#MailingAddress2").removeAttr("disabled");
                $("#MailingCity").removeAttr("disabled");
                $("#MailingState").removeAttr("disabled");
                $("#MailingPostalCode").removeAttr("disabled");
                $("#MailingCountry").removeAttr("disabled");
            }
            edituserprofile.enableMailingAddressControls = enableMailingAddressControls;
        })(edituserprofile = home.edituserprofile || (home.edituserprofile = {}));
    })(home = resgrid.home || (resgrid.home = {}));
})(resgrid || (resgrid = {}));
