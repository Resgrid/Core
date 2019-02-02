var resgrid;
(function (resgrid) {
    var connect;
    (function (connect) {
        var addPost;
        (function (addPost) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Connect - Add Post');
                $('#editor').kendoEditor();
                $('#Article_StartOn').kendoDateTimePicker({
                    interval: 1
                });
                $('#Article_ExpiresOn').kendoDateTimePicker({
                    interval: 1
                });
            });
        })(addPost = connect.addPost || (connect.addPost = {}));
    })(connect = resgrid.connect || (resgrid.connect = {}));
})(resgrid || (resgrid = {}));
