
var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var addcertification;
        (function (addcertification) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Add Certification');
                $('#RecievedOn').datetimepicker({ step: 60 });
                $('#ExpiresOn').datetimepicker({ step: 60 });
                // Native HTML file input is used - no JS initialization needed
            });
        })(addcertification = profile.addcertification || (profile.addcertification = {}));
    })(profile = resgrid.profile || (resgrid.profile = {}));
})(resgrid || (resgrid = {}));
