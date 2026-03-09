
var resgrid;
(function (resgrid) {
    var dlists;
    (function (dlists) {
        var editlist;
        (function (editlist) {
            $(document).ready(function () {
                if (currentType == 'Internal') {
                    $('#resgridEmail').show();
                }
                $('#List_EmailAddress').blur(function () {
                    validateEmailAddress($('#List_EmailAddress').val());
                });
                $("#listMembers").select2({
                    placeholder: "Select Members...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (u) { return { id: u.UserId, text: u.Name }; }) };
                        }
                    }
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/DistributionLists/GetMembersForList?id=' + $('#List_DistributionListId').val(),
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        data.forEach(function (u) { $("#listMembers").append(new Option(u.Name, u.UserId, true, true)); });
                        $("#listMembers").trigger('change');
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
                            url: resgrid.absoluteBaseUrl + '/User/DistributionLists/ValidateAddress?id=' + $('#List_DistributionListId').val() + '&emailAddress=' + emailAddress,
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
            editlist.validateEmailAddress = validateEmailAddress;
            var expression = /^(([^<>()[\]\\.,;:\s@\"]+(\.[^<>()[\]\\.,;:\s@\"]+)*)|(\".+\"))$/;
        })(editlist = dlists.editlist || (dlists.editlist = {}));
    })(dlists = resgrid.dlists || (resgrid.dlists = {}));
})(resgrid || (resgrid = {}));
