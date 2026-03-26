var resgrid;
(function (resgrid) {
    var common;
    (function (common) {
        var analytics;
        (function (analytics) {
            function register(userId, departmentId, name, email, departmentName, createdOn) {
                if (window.location.host.indexOf('resgrid.local') < 0 && window.location.host.indexOf('localhost') < 0) {
                    if (window.Countly && typeof window.Countly.set_id === 'function') {
                        window.Countly.set_id(userId);
                        window.Countly.user_details({
                            "name": name,
                            "email": email,
                            "organization": departmentName,
                            "custom": {
                                "createdOn": new Date(createdOn * 1000),
                                "departmentId": departmentId
                            }
                        });
                    }
                }
            }
            analytics.register = register;
            function track(event) {
                if (window.location.host.indexOf('resgrid.local') < 0 && window.location.host.indexOf('localhost') < 0) {
                    if (window.Countly && typeof window.Countly.add_event === 'function') {
                        window.Countly.add_event({
                            "key": event
                        });
                    }
                }
            }
            analytics.track = track;
        })(analytics = common.analytics || (common.analytics = {}));
    })(common = resgrid.common || (resgrid.common = {}));
})(resgrid || (resgrid = {}));
