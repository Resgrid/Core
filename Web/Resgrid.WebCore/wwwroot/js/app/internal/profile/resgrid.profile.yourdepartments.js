
var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var yourDepartments;
        (function (yourDepartments) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Your Departments');
                $("#submit-button").click(function (event) {
                    var deparmentId = $('#deparmentId').val();
                    var departmentCode = $('#departmentCode').val();
                    if (deparmentId && departmentCode) {
                        $('#submit-button').attr("disabled", "disabled");
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Profile/JoinDepartment?id=' + deparmentId + '&code=' + departmentCode,
                            contentType: 'application/json; charset=utf-8',
                            type: 'GET'
                        }).done(function (result) {
                            if (result) {
                                $("#submit-button").removeAttr("disabled");
                                $('#errorMessage').show();
                                $('#errorMessage').text(result);
                            }
                            else {
                                $('#errorMessage').hide();
                                var form$ = $("#joinDepartmentForm");
                                form$.submit();
                            }
                        }).fail(function (jqXHR, textStatus, errorThrown) {
                            $("#submit-button").removeAttr("disabled");
                            $('#errorMessage').show();
                            $('#errorMessage').text("Unknown department id or department code. Please reach out to an administrator of the department you want to join and ensure the values are correct.");
                        });
                    }
                    else {
                        $('#errorMessage').show();
                        $('#errorMessage').text("Department Id and Code are both required to attempt to join the department.");
                    }
                    // prevent the form from submitting with the default action
                    return false;
                });
            });
            function switchActiveDepartment(departmentId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Profile/SetActiveDepartment',
                    data: JSON.stringify({
                        DepartmentId: departmentId
                    }),
                    contentType: 'application/json',
                    type: 'POST'
                });
            }
            yourDepartments.switchActiveDepartment = switchActiveDepartment;
        })(yourDepartments = profile.yourDepartments || (profile.yourDepartments = {}));
    })(profile = resgrid.profile || (resgrid.profile = {}));
})(resgrid || (resgrid = {}));
