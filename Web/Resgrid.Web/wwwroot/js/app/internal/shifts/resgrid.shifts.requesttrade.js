
var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var requesttrade;
        (function (requesttrade) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Request Trade');
                $("#users").select2({
                    placeholder: "Select users...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelNotOnShiftDay?shiftSignupId=' + shiftSignupId + '&shiftDayId=' + shiftDayId,
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (u) { return { id: u.UserId, text: u.Name }; }) };
                        }
                    }
                });
            });
        })(requesttrade = shifts.requesttrade || (shifts.requesttrade = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
