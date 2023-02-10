
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var deletecall;
        (function (deletecall) {
            $(document).ready(function () {
                $("#DeleteCallNotes").kendoEditor();
            });
        })(deletecall = dispatch.deletecall || (dispatch.deletecall = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
