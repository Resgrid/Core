
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
