var resgrid;
(function (resgrid) {
    var common;
    (function (common) {
        var signalr;
        var eventHub;

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

                var options = {
                    //transport: signalR.HttpTransportType.ServerSentEvents,
                    transport: signalR.HttpTransportType.None,
                    logging: signalR.LogLevel.Trace,
                    // The eventing hub authenticates every connection. The page itself never holds an
                    // API token any more, so mint a short-lived one through the same BFF endpoint the
                    // React surfaces use. Automatic reconnect re-runs this, which is what keeps the
                    // connection alive past the token's short lifetime.
                    accessTokenFactory: getEventingToken
                };

                eventHub = new signalR.HubConnectionBuilder()
                    .withUrl(resgrid.absoluteEventingBaseUrl + '/eventingHub', options)
                    .withAutomaticReconnect()
                    .build();
                eventHub.serverTimeoutInMilliseconds = 9999999999999;
                eventHub.keepAliveIntervalInMilliseconds = 1000;

                eventHub.onreconnected(function () {
                    if (departmentId && departmentId > 0) {
                        eventHub.invoke("Connect", Number(departmentId)).catch(function (err) {
                            return console.error(err.toString());
                        });
                    }
                });

                // withAutomaticReconnect() only covers drops after a connection is up, and it stops
                // after its own short schedule. Nothing was listening for that, so an exhausted
                // reconnect left the board silently stale until someone reloaded the page.
                eventHub.onclose(function (err) {
                    if (err) {
                        console.error(err.toString());
                    }

                    console.log('disconnected');
                    scheduleReconnect();
                });

                registerClientMethods();
                startConnection();

                //if ($ && $.connection && $.connection.hub) {
                //    $.connection.hub.url = resgrid.absoluteApiBaseUrl + '/signalr';
                //    eventHub = $.connection.eventingHub;
                //    registerClientMethods();
                //    startConnection();
                //}
            }
            signalr.init = init;
            function getEventingToken() {
                var meta = document.querySelector('meta[name="request-verification-token"]');
                var headers = { 'Accept': 'application/json' };

                if (meta && meta.content) {
                    headers['RequestVerificationToken'] = meta.content;
                }

                return fetch('/api/web-bff/eventing-token', {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: headers
                }).then(function (response) {
                    return response.ok ? response.json() : null;
                }).then(function (body) {
                    return body && typeof body.accessToken === 'string' ? body.accessToken : '';
                }).catch(function () {
                    return '';
                });
            }
            // A failed initial start() is not covered by withAutomaticReconnect at all, so retry it
            // here. Bounded and backed off: a down eventing server should not be hammered, and a
            // page left open overnight should not retry forever.
            var MAX_START_ATTEMPTS = 10;
            var startAttempts = 0;

            function scheduleReconnect() {
                if (startAttempts >= MAX_START_ATTEMPTS) {
                    console.error('Eventing hub gave up after ' + startAttempts + ' connection attempts, reload the page to retry.');
                    return;
                }

                startAttempts++;
                var delay = Math.min(30000, 2000 * startAttempts);
                console.log('Retrying eventing hub connection in ' + delay + 'ms (attempt ' + startAttempts + ' of ' + MAX_START_ATTEMPTS + ')');
                setTimeout(startConnection, delay);
            }
            function startConnection() {
                if (departmentId && departmentId > 0) {
                    Object.defineProperty(WebSocket, 'OPEN', { value: 1 });
                    eventHub.start().then(function () {
                        console.log('connected');
                        startAttempts = 0;
                        eventHub.invoke("Connect", Number(departmentId)).catch(function (err) {
                            return console.error(err.toString());
                        });
                    }).catch(function (err) {
                        console.log('Could not connect');
                        console.error(err.toString());
                        scheduleReconnect();
                    });


                    //$.connection.hub.disconnected(function () {
                    //    console.log('disconnected');
                    //    setTimeout(function () {
                    //        console.log('reconnecting');
                    //        $.connection.hub.start().done(function () {
                    //            console.log('connected');
                    //            //$rootScope.$broadcast(CONSTS.EVENTS.CONNECTED);
                    //            eventHub.server.connect(departmentId);
                    //        }).fail(function () { console.log('Could not connect'); });
                    //    }, 5000); // Restart connection after 5 seconds.
                    //});
                    //$.connection.hub.start({ withCredentials: false }).done(function () {
                    //    console.log('connected');
                    //    //$rootScope.$broadcast(CONSTS.EVENTS.CONNECTED);
                    //    eventHub.server.connect(departmentId);
                    //}).fail(function () { console.log('Could not connect'); });
                }
            }
            function registerClientMethods() {
                eventHub.on("onConnected", function (id) {
                    //connectionId = id;
                });

                eventHub.on("PersonnelStatusUpdated", function (id) {
                    if (personnelActionUpdatedCallback) {
                        personnelActionUpdatedCallback();
                    }
                });

                eventHub.on("PersonnelStaffingUpdated", function (id) {
                    if (personnelStaffingUpdatedCallback) {
                        personnelStaffingUpdatedCallback();
                    }
                });

                eventHub.on("UnitStatusUpdated", function (id) {
                    if (unitStatusUpdatedCallback) {
                        unitStatusUpdatedCallback();
                    }
                });

                eventHub.on("CallsUpdated", function (id) {
                    if (callsUpdatedCallback) {
                        callsUpdatedCallback(id);
                    }
                });

                eventHub.on("CallAdded", function (id) {
                    if (callsUpdatedCallback) {
                        callsUpdatedCallback(id);
                    }
                });

                eventHub.on("CallClosed", function (id) {
                    if (callsUpdatedCallback) {
                        callsUpdatedCallback(id);
                    }
                });

                eventHub.on("DepartmentUpdated", function (id) {

                });

                // if (eventHub && eventHub.client) {
                //eventHub.client.onConnected = function (id) {
                //    connectionId = id;
                //};
                //eventHub.client.callsUpdated = function (id) {
                //    if (callsUpdatedCallback) {
                //        callsUpdatedCallback(id);
                //    }
                //};
                //eventHub.client.personnelStatusUpdated = function (id) {
                //    if (personnelActionUpdatedCallback) {
                //        personnelActionUpdatedCallback();
                //    }
                //};
                //eventHub.client.unitStatusUpdated = function (id) {
                //    if (unitStatusUpdatedCallback) {
                //        unitStatusUpdatedCallback();
                //    }
                //};
                //eventHub.client.personnelStaffingUpdated = function (id) {
                //    if (personnelStaffingUpdatedCallback) {
                //        personnelStaffingUpdatedCallback();
                //    }
                //};
                //}
            }
        })(signalr = common.signalr || (common.signalr = {}));
    })(common = resgrid.common || (resgrid.common = {}));
})(resgrid || (resgrid = {}));
