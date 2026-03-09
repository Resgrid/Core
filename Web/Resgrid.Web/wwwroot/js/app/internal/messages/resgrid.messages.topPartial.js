
var resgrid;
(function (resgrid) {
    var message;
    (function (message) {
        var topPartial;
        (function (topPartial) {
            $(document).ready(function () {
                reloadUnreadMessagesList();
                reloadActiveCallsList();
            });
            function reloadUnreadMessagesList() {
                $('#unreadMessages').load(resgrid.absoluteBaseUrl + '/User/Messages/GetTopUnreadMessages', function () {
                    resgrid.showProgress($("#unreadMessages"), false);
                });
            }
            topPartial.reloadUnreadMessagesList = reloadUnreadMessagesList;
            function reloadActiveCallsList() {
                $('#activeCalls').load(resgrid.absoluteBaseUrl + '/User/Dispatch/GetTopActiveCalls', function () {
                    resgrid.showProgress($("#activeCalls"), false);
                });
            }
            topPartial.reloadActiveCallsList = reloadActiveCallsList;
        })(topPartial = message.topPartial || (message.topPartial = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
