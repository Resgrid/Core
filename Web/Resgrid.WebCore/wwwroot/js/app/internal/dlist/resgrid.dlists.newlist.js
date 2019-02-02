
var resgrid;
(function (resgrid) {
    var dlists;
    (function (dlists) {
        var newlist;
        (function (newlist) {
            $(document).ready(function () {
                $('#List_EmailAddress').blur(function () {
                    validateEmailAddress($('#List_EmailAddress').val());
                });
                $("#listMembers").kendoMultiSelect({
                    placeholder: "Select Members...",
                    dataTextField: "Name",
                    dataValueField: "UserId",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter'
                        }
                    }
                });
            });
            function switchInputs(value) {
                if (value) {
                    if (value === "Internal") {
                        $('#resgridEmail').show();
                        $('#externalEmail').hide();
                    }
                    else {
                        $('#resgridEmail').hide();
                        $('#externalEmail').show();
                        $('#emailError').hide();
                        $("#submit_action").removeAttr("disabled");
                        $('#List_EmailAddress').removeClass('input-validation-error');
                    }
                }
            }
            function validateEmailAddress(emailAddress) {
                if (emailAddress) {
                    if (!expression.test(emailAddress)) {
                        $('#emailError').text('Your email address looks invalid. Ensure there are no spaces or special characters.');
                        $('#emailError').show();
                        $('#List_EmailAddress').addClass('input-validation-error');
                        $('#submit_action').attr("disabled", "disabled");
                    }
                    else {
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/DistributionLists/ValidateAddress?id=0' + '&emailAddress=' + emailAddress,
                            contentType: 'application/json',
                            type: 'GET'
                        }).done(function (data) {
                            if (data) {
                                $('#emailError').text(data);
                                $('#emailError').show();
                                $('#List_EmailAddress').addClass('input-validation-error');
                                $('#submit_action').attr("disabled", "disabled");
                            }
                            else {
                                $('#emailError').hide();
                                $("#submit_action").removeAttr("disabled");
                                $('#List_EmailAddress').removeClass('input-validation-error');
                            }
                        });
                    }
                }
                else if ($('#Type').select2('data').text === 'Internal') {
                    $('#emailError').text('You need to specify an email address for the Internal type..');
                    $('#emailError').show();
                    $('#List_EmailAddress').addClass('input-validation-error');
                    $('#submit_action').attr("disabled", "disabled");
                }
                else {
                    $('#emailError').hide();
                    $("#submit_action").removeAttr("disabled");
                    $('#List_EmailAddress').removeClass('input-validation-error');
                }
            }
            newlist.validateEmailAddress = validateEmailAddress;
            var expression = /^(([^<>()[\]\\.,;:\s@\"]+(\.[^<>()[\]\\.,;:\s@\"]+)*)|(\".+\"))$/;
        })(newlist = dlists.newlist || (dlists.newlist = {}));
    })(dlists = resgrid.dlists || (resgrid.dlists = {}));
})(resgrid || (resgrid = {}));
