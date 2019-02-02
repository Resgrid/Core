var resgrid;
(function (resgrid) {
    var home;
    (function (home) {
        var dashboard;
        (function (dashboard) {
            $(document).ready(function () {
                resgrid.common.analytics.register(userId, departmentId, fullName, email, departmentName, createdOn);
                resgrid.common.analytics.track('Dashboard');
                resgrid.common.signalr.init(null, reloadPersonnelTable, reloadPersonnelTable, null);
                dashboard.wndCalls = $("#respondToACallWindow")
                    .kendoWindow({
                    title: "Respond to Call",
                    modal: true,
                    visible: false,
                    resizable: false,
                    content: resgrid.absoluteBaseUrl + '/User/Dispatch/SmallActiveCallGrid',
                    width: 750,
                    height: 465
                }).data("kendoWindow");
                dashboard.wndStations = $("#respondToAStationWindow")
                    .kendoWindow({
                    title: "Respond to Station",
                    modal: true,
                    visible: false,
                    resizable: false,
                    content: resgrid.absoluteBaseUrl + '/User/Department/SmallStationGroupsGrid',
                    width: 750,
                    height: 465
                }).data("kendoWindow");
                $('.respondToACallWindow').on('respondToCall', function (e, data) {
                    dashboard.wndCalls.close();
                    reloadPersonnelTable();
                });
                $('.respondToAStationWindow').on('respondToAStation', function (e, data) {
                    dashboard.wndStations.close();
                    reloadPersonnelTable();
                });
                reloadPersonnelTable();
                verifySubscriptionLimits();
            });
            function reloadPersonnelTable() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/GetUserStatusTable',
                    cache: false,
                    dataType: "html",
                    success: function (data) {
                        $('#personnelGrid').html(data);
                        kendo.ui.progress($("#personnelGrid"), false);
                    }
                });
            }
            dashboard.reloadPersonnelTable = reloadPersonnelTable;
            function verifySubscriptionLimits() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Department/GetSubscriptionLimitWarning',
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (data && data.length > 0) {
                        resgrid.common.notifications.showWarning('Department Limits', 'The department is at or has exceeded some limits for the current subscription plan. Some functionality may be impacted.');
                    }
                });
            }
            dashboard.verifySubscriptionLimits = verifySubscriptionLimits;
            function showCalls() {
                dashboard.wndCalls.center().open();
            }
            dashboard.showCalls = showCalls;
            function showStations() {
                dashboard.wndStations.center().open();
            }
            dashboard.showStations = showStations;
            function actionResponding() {
                kendo.ui.progress($("#personnelGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomAction?actionType=2',
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    reloadPersonnelTable();
                });
            }
            dashboard.actionResponding = actionResponding;
            function actionNotResponding() {
                kendo.ui.progress($("#personnelGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomAction?actionType=1',
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    reloadPersonnelTable();
                });
            }
            dashboard.actionNotResponding = actionNotResponding;
            function actionAvailable() {
                kendo.ui.progress($("#personnelGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomAction?actionType=0',
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    reloadPersonnelTable();
                });
            }
            dashboard.actionAvailable = actionAvailable;
            function actionAvailableStation() {
                kendo.ui.progress($("#personnelGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomAction?actionType=4',
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    reloadPersonnelTable();
                });
            }
            dashboard.actionAvailableStation = actionAvailableStation;
            function actionOnScene() {
                kendo.ui.progress($("#personnelGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomAction?actionType=3',
                    contentType: 'application/json; charset=utf-8',
                    type: 'POST'
                }).done(function (results) {
                    reloadPersonnelTable();
                });
            }
            dashboard.actionOnScene = actionOnScene;
            function customAction(actionId) {
                kendo.ui.progress($("#personnelGrid"), true);
                var note = $("#actionNote").val();
                if (note) {
                    note = encodeURIComponent(note);
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomAction?actionType=' + actionId + "&note=" + note,
                        //contentType: 'application/json; charset=utf-8',
                        type: 'GET'
                    }).done(function (results) {
                        resgrid.home.dashboard.reloadPersonnelTable();
                    });
                }
                else {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomAction?actionType=' + actionId,
                        //contentType: 'application/json; charset=utf-8',
                        type: 'GET'
                    }).done(function (results) {
                        resgrid.home.dashboard.reloadPersonnelTable();
                    });
                }
            }
            dashboard.customAction = customAction;
            function customStaffing(userId, staffingLevel) {
                kendo.ui.progress($("#personnelGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomStaffing?userId=' + userId + '&staffingLevel=' + staffingLevel,
                    //contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (results) {
                    resgrid.home.dashboard.reloadPersonnelTable();
                });
            }
            dashboard.customStaffing = customStaffing;
            function customUserAction(userId, actionId) {
                kendo.ui.progress($("#personnelGrid"), true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Home/SetCustomUserAction?userId=' + userId + '&actionType=' + actionId,
                    //contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (results) {
                    resgrid.home.dashboard.reloadPersonnelTable();
                });
            }
            dashboard.customUserAction = customUserAction;
        })(dashboard = home.dashboard || (home.dashboard = {}));
    })(home = resgrid.home || (resgrid.home = {}));
})(resgrid || (resgrid = {}));
