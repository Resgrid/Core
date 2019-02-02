
var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var requesttrade;
        (function (requesttrade) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Request Trade');
                $("#users").kendoMultiSelect({
                    placeholder: "Select users...",
                    dataTextField: "Name",
                    dataValueField: "UserId",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelNotOnShiftDay?shiftSignupId=' + shiftSignupId + '&shiftDayId=' + shiftDayId
                        }
                    }
                });
            });
        })(requesttrade = shifts.requesttrade || (shifts.requesttrade = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
