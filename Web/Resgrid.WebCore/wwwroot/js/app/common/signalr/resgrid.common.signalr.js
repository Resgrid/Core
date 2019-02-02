//interface ConnectionOptions {
//	transport?: string | Array<string> | Transport;
//	callback?: Function;
//	waitForPageLoad?: boolean;
//	jsonp?: boolean;
//	withCredentials: boolean;
//	pingInterval?: number;
//}
var resgrid;
(function (resgrid) {
    var common;
    (function (common) {
        var signalr;
        (function (signalr) {
            function init(callCallback, actionCallback, staffingCallback, unitCallback) {
                if (callCallback) {
                    callsUpdatedCallback = callCallback;
                }
                if (actionCallback) {
                    personnelActionUpdatedCallback = actionCallback;
                }
                if (staffingCallback) {
                    personnelStaffingUpdatedCallback = staffingCallback;
                }
                if (unitCallback) {
                    unitStatusUpdatedCallback = unitCallback;
                }
                //$.connection.hub.url = absoluteBaseUrl + '/signalr';
                $.connection.hub.url = 'https://api.resgrid.com/signalr';
                //$.connection.hub.url = 'http://resgridapi.local/signalr';
                eventHub = $.connection.eventingHub;
                registerClientMethods();
                startConnection();
            }
            signalr.init = init;
            function startConnection() {
                if (departmentId && departmentId > 0) {
                    $.connection.hub.disconnected(function () {
                        console.log('disconnected');
                        setTimeout(function () {
                            console.log('reconnecting');
                            $.connection.hub.start().done(function () {
                                console.log('connected');
                                //$rootScope.$broadcast(CONSTS.EVENTS.CONNECTED);
                                eventHub.server.connect(departmentId);
                            }).fail(function () { console.log('Could not connect'); });
                        }, 5000); // Restart connection after 5 seconds.
                    });
                    $.connection.hub.start({ withCredentials: false }).done(function () {
                        console.log('connected');
                        //$rootScope.$broadcast(CONSTS.EVENTS.CONNECTED);
                        eventHub.server.connect(departmentId);
                    }).fail(function () { console.log('Could not connect'); });
                }
            }
            function registerClientMethods() {
                eventHub.client.onConnected = function (id) {
                    connectionId = id;
                };
                eventHub.client.callsUpdated = function (id) {
                    if (callsUpdatedCallback) {
						callsUpdatedCallback(id);
                    }
                };
                eventHub.client.personnelStatusUpdated = function (id) {
                    if (personnelActionUpdatedCallback) {
                        personnelActionUpdatedCallback();
                    }
                };
                eventHub.client.unitStatusUpdated = function (id) {
                    if (unitStatusUpdatedCallback) {
                        unitStatusUpdatedCallback();
                    }
                };
                eventHub.client.personnelStaffingUpdated = function (id) {
                    if (personnelStaffingUpdatedCallback) {
                        personnelStaffingUpdatedCallback();
                    }
                };
            }
        })(signalr = common.signalr || (common.signalr = {}));
    })(common = resgrid.common || (resgrid.common = {}));
})(resgrid || (resgrid = {}));
