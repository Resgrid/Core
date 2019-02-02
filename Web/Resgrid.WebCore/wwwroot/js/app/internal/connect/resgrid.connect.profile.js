var resgrid;
(function (resgrid) {
    var connect;
    (function (connect) {
        var profile;
        (function (profile) {
            $(document).ready(function () {
                $("#Profile_Founded").kendoDatePicker({});
            });
        })(profile = connect.profile || (connect.profile = {}));
    })(connect = resgrid.connect || (resgrid.connect = {}));
})(resgrid || (resgrid = {}));
