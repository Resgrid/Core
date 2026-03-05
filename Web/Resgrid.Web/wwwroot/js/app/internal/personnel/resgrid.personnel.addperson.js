
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
                $("#roles").select2({
                    placeholder: "Select roles...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles',
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (r) { return { id: r.RoleId, text: r.Name }; }) };
                        }
                    }
                });
            });
        })(addperson = personnel.addperson || (personnel.addperson = {}));
    })(personnel = resgrid.personnel || (resgrid.personnel = {}));
})(resgrid || (resgrid = {}));
