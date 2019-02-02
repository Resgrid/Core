var resgrid;
(function (resgrid) {
    var connect;
    (function (connect) {
        var messages;
        (function (messages) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Connect - Messages');
                $('#editor').kendoEditor();
                $('#Article_StartOn').kendoDateTimePicker({
                    interval: 1
                });
                $('#Article_ExpiresOn').kendoDateTimePicker({
                    interval: 1
                });
            });
        })(messages = connect.messages || (connect.messages = {}));
    })(connect = resgrid.connect || (resgrid.connect = {}));
})(resgrid || (resgrid = {}));
