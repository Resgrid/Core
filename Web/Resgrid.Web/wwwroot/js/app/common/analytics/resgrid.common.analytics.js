var resgrid;
(function (resgrid) {
    var common;
    (function (common) {
        var analytics;
        (function (analytics) {
            function register(userId, departmentId, name, email, departmentName, createdOn) {
                if (window.location.host.indexOf('resgrid.local') < 0 && window.location.host.indexOf('localhost') < 0) {
                    if (typeof posthog !== "undefined") {
                        posthog.identify(
                            userId,
                            { email: email, name: name, createdOn: new Date(createdOn * 1000), departmentId: departmentId, departmentName: departmentName } // optional: set additional person properties
                        );
                    }
                }
            }
            analytics.register = register;
            function track(event) {
                if (window.location.host.indexOf('resgrid.local') < 0 && window.location.host.indexOf('localhost') < 0) {
                    if (typeof posthog !== "undefined") {
                        posthog.capture(event)
                    }
                }
            }
            analytics.track = track;
        })(analytics = common.analytics || (common.analytics = {}));
    })(common = resgrid.common || (resgrid.common = {}));
})(resgrid || (resgrid = {}));
