
var resgrid;
(function (resgrid) {
    var personnel;
    (function (personnel) {
        var addperson;
        (function (addperson) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Add Personnel');
                $('#Carrier').select2();
                $('#UserGroup').select2();
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
            });
        })(addperson = personnel.addperson || (personnel.addperson = {}));
    })(personnel = resgrid.personnel || (resgrid.personnel = {}));
})(resgrid || (resgrid = {}));
