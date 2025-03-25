var resgrid;
(function (resgrid) {
    var personnel;
    (function (personnel) {
        var editRole;
        (function (editRole) {
            $(document).ready(function () {
                $("#users").kendoMultiSelect({
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
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Personnel/GetMembersForRole?id=' + $('#Role_PersonnelRoleId').val(),
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        var multiSelect = $("#users").data("kendoMultiSelect");
                        var valuesToAdd = [];
                        for (var i = 0; i < data.length; i++) {
                            valuesToAdd.push(data[i]);
                        }
                        multiSelect.value(valuesToAdd);
                    }
                });
            });
        })(editRole = personnel.editRole || (personnel.editRole = {}));
    })(personnel = resgrid.personnel || (resgrid.personnel = {}));
})(resgrid || (resgrid = {}));
