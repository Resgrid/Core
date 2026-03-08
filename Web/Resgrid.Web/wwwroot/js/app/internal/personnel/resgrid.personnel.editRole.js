var resgrid;
(function (resgrid) {
    var personnel;
    (function (personnel) {
        var editRole;
        (function (editRole) {
            $(document).ready(function () {
                $("#users").select2({
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
                    url: resgrid.absoluteBaseUrl + '/User/Personnel/GetMembersForRole?id=' + $('#Role_PersonnelRoleId').val(),
                    contentType: 'application/json', type: 'GET'
                }).done(function (data) {
                    if (data) {
                        data.forEach(function (userId) { $("#users").append(new Option(userId, userId, true, true)); });
                        $("#users").trigger('change');
                    }
                });
            });
        })(editRole = personnel.editRole || (personnel.editRole = {}));
    })(personnel = resgrid.personnel || (resgrid.personnel = {}));
})(resgrid || (resgrid = {}));
