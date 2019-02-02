var resgrid;
(function (resgrid) {
    var common;
    (function (common) {
        var analytics;
        (function (analytics) {
            function register(userId, departmentId, name, email, departmentName, createdOn) {
                if (window.location.host.indexOf('resgrid.local') < 0 && window.location.host.indexOf('localhost') < 0) {
                    if (typeof mixpanel !== "undefined") {
                        mixpanel.identify(userId);
                        mixpanel.register_once({
                            "DepartmentId": departmentId //,
                        });
                        mixpanel.people.set({
                            "$email": email,
                            "$created": new Date(createdOn * 1000),
                            "$last_login": new Date(),
                            "departmentId": departmentId,
                            "departmentName": departmentName,
                            "$name": name
                        });
                    }
                }
            }
            analytics.register = register;
            function track(event) {
                if (window.location.host.indexOf('resgrid.local') < 0 && window.location.host.indexOf('localhost') < 0) {
                    if (typeof mixpanel !== "undefined") {
                        mixpanel.track(event);
                    }
                }
            }
            analytics.track = track;
        })(analytics = common.analytics || (common.analytics = {}));
    })(common = resgrid.common || (resgrid.common = {}));
})(resgrid || (resgrid = {}));
