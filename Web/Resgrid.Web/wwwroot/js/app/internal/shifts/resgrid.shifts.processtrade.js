var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var processtrade;
        (function (processtrade) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Process Trade');

                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var shiftTradeId = $('#shiftSignupTradeId').val();

                // The days offered back are the caller's own upcoming signups, loaded once; they post as "dates".
                $("#dates").select2({
                    placeholder: i18n.selectDates || "Select dates...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftDaysUserIsOn?shiftTradeId=' + encodeURIComponent(shiftTradeId),
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data || [], function (d) { return { id: d.ShiftSignupId, text: d.Title }; }) };
                        }
                    }
                });
            });
        })(processtrade = shifts.processtrade || (shifts.processtrade = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
