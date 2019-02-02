
var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var editcertification;
        (function (editcertification) {
            $(document).ready(function () {
                $('#RecievedOn').kendoDatePicker();
                $('#ExpiresOn').kendoDatePicker();
                $("#fileToUpload").kendoUpload({
                    multiple: false,
                    localization: {
                        select: "Select File"
                    }
                });
                $("#Type").kendoComboBox({
                    minLength: 3,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Profile/GetDepartmentCertificationTypes'
                        }
                    },
                    filter: "contains",
                    suggest: true
                });
            });
        })(editcertification = profile.editcertification || (profile.editcertification = {}));
    })(profile = resgrid.profile || (resgrid.profile = {}));
})(resgrid || (resgrid = {}));
