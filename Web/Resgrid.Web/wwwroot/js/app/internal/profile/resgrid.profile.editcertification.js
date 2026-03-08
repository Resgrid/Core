
var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var editcertification;
        (function (editcertification) {
            $(document).ready(function () {
                $('#RecievedOn').datetimepicker({ step: 60 });
                $('#ExpiresOn').datetimepicker({ step: 60 });
                // Native HTML file input is used - no JS initialization needed
            });
        })(editcertification = profile.editcertification || (profile.editcertification = {}));
    })(profile = resgrid.profile || (resgrid.profile = {}));
})(resgrid || (resgrid = {}));
