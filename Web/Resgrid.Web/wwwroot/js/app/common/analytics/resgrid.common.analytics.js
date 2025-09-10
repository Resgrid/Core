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

                    if (typeof Countly !== "undefined") {
                        Countly.q.push(['set_id', userId]);
                        Countly.q.push(['user_details', {
                            "name": name,
                            "email": email,
                            "organization": departmentName,
                            "custom": {
                                "createdOn": new Date(createdOn * 1000),
                                "departmentId": departmentId
                            }]);
                        Countly.q.push(['track_sessions']);
                        Countly.q.push(['track_pageview']);
                    }
                }
            }
            analytics.register = register;
            function track(event) {
                if (window.location.host.indexOf('resgrid.local') < 0 && window.location.host.indexOf('localhost') < 0) {
                    if (typeof posthog !== "undefined") {
                        posthog.capture(event)
                    }

                    if (typeof Aptabase !== 'undefined') {
                        Aptabase.trackEvent(event);
                    }

                    if (typeof Countly !== 'undefined') {
                        Countly.q.push(['add_event', {
                            "key": event
                        }]);
                    }
                }
            }
            analytics.track = track;
        })(analytics = common.analytics || (common.analytics = {}));
    })(common = resgrid.common || (resgrid.common = {}));
})(resgrid || (resgrid = {}));
