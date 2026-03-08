
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
                $("#dates").select2({
                    placeholder: "Select dates...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftDaysUserIsOn?shiftTradeId=' + shiftTradeId,
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (d) { return { id: d.ShiftSignupId, text: d.Title }; }) };
                        }
                    }
                });
            });
        })(requesttrade = shifts.requesttrade || (shifts.requesttrade = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
