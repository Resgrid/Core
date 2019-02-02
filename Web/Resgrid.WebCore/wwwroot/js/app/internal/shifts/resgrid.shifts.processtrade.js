
var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var requesttrade;
        (function (requesttrade) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Process Trade');
                $('#note').change(function () {
                    $('#reject_button').attr('href', resgrid.absoluteBaseUrl + '/User/Shifts/RejectTrade?shiftTradeId=' + shiftTradeId + '&reason=' + encodeURIComponent($("#note").val()));
                });
                $("#dates").kendoMultiSelect({
                    placeholder: "Select dates...",
                    dataTextField: "Title",
                    dataValueField: "ShiftSignupId",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftDaysUserIsOn?shiftTradeId=' + shiftTradeId
                        }
                    }
                });
            });
        })(requesttrade = shifts.requesttrade || (shifts.requesttrade = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
