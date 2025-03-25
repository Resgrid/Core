
var resgrid;
(function (resgrid) {
    var contacts;
    (function (contacts) {
        var addcontact;
        (function (addcontact) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Add Contact');

                let quillDscription = new Quill('#description-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                let quillOtherInfo = new Quill('#otherInfo-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#addContactForm', function () {
                    $('#Contact_Description').val(quillDscription.root.innerHTML);
                    $('#Contact_OtherInfo').val(quillOtherInfo.root.innerHTML);

                    return true;
                });

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
            });
            function disableMailingAddressControls() {
                $("#MailingAddress1").attr("disabled", "disabled");
                $("#MailingAddress2").attr("disabled", "disabled");
                $("#MailingCity").attr("disabled", "disabled");
                $("#MailingState").attr("disabled", "disabled");
                $("#MailingPostalCode").attr("disabled", "disabled");
                $("#MailingCountry").attr("disabled", "disabled");
            }
            addcontact.disableMailingAddressControls = disableMailingAddressControls;
            function enableMailingAddressControls() {
                $("#MailingAddress1").removeAttr("disabled");
                $("#MailingAddress2").removeAttr("disabled");
                $("#MailingCity").removeAttr("disabled");
                $("#MailingState").removeAttr("disabled");
                $("#MailingPostalCode").removeAttr("disabled");
                $("#MailingCountry").removeAttr("disabled");
            }
            addcontact.enableMailingAddressControls = enableMailingAddressControls;
        })(addcontact = contacts.addcontact || (contacts.addcontact = {}));
    })(home = resgrid.contacts || (resgrid.contacts = {}));
})(resgrid || (resgrid = {}));
