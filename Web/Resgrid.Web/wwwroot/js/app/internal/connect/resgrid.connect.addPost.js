var resgrid;
(function (resgrid) {
    var connect;
    (function (connect) {
        var addPost;
        (function (addPost) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Connect - Add Post');
                //$('#editor').kendoEditor();
                $('#Article_StartOn').datetimepicker({ step: 15 });
                $('#Article_ExpiresOn').datetimepicker({ step: 15 });
            });
        })(addPost = connect.addPost || (connect.addPost = {}));
    })(connect = resgrid.connect || (resgrid.connect = {}));
})(resgrid || (resgrid = {}));
