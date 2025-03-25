var resgrid;
(function (resgrid) {
    var connect;
    (function (connect) {
        var messages;
        (function (messages) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Connect - Messages');
                //$('#editor').kendoEditor();
                $('#Article_StartOn').datetimepicker({ step: 15 });
                $('#Article_ExpiresOn').datetimepicker({ step: 15 });
            });
        })(messages = connect.messages || (connect.messages = {}));
    })(connect = resgrid.connect || (resgrid.connect = {}));
})(resgrid || (resgrid = {}));
