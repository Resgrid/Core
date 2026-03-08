var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var editshiftdetails;
        (function (editshiftdetails) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Edit');
                $('.sl2').select2();

                $("#colorPicker").minicolors({
                    animationSpeed: 50,
                    animationEasing: 'swing',
                    changeDelay: 0,
                    control: 'hue',
                    defaultValue: '#0080ff',
                    format: 'hex',
                    showSpeed: 100,
                    hideSpeed: 100,
                    inline: false,
                    theme: 'bootstrap'
                });

                $("#Shift_StartTime").datetimepicker({ datepicker: false, format: 'H:i', step: 15 });
                $("#Shift_StartTime").keypress(function (e) {
                    e.preventDefault();
                });
                $("#Shift_EndTime").datetimepicker({ datepicker: false, format: 'H:i', step: 15 });
                $("#Shift_EndTime").keypress(function (e) {
                    e.preventDefault();
                });

                function initPersonnelSelect2(selector) {
                    $(selector).select2({
                        placeholder: "Select Personnel...",
                        allowClear: true,
                        multiple: true,
                        ajax: {
                            url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                            dataType: 'json',
                            processResults: function (data) {
                                return { results: $.map(data, function (u) { return { id: u.UserId, text: u.Name }; }) };
                            }
                        }
                    });
                }

                initPersonnelSelect2("#shiftPersonnel");

                $('.groupPersonnelSelect').each(function () {
                    var that = this;
                    initPersonnelSelect2(that);
                    var groupId = $(that).attr("name").replace('groupPersonnel_', '');
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelForShift?shiftId=' + $('#Shift_ShiftId').val() + '&groupId=' + groupId,
                        contentType: 'application/json', type: 'GET'
                    }).done(function (data) {
                        if (data) {
                            data.forEach(function (u) { $(that).append(new Option(u.Name, u.UserId, true, true)); });
                            $(that).trigger('change');
                        }
                    });
                });

                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelForShift?shiftId=' + $('#Shift_ShiftId').val() + '&groupId=0',
                    contentType: 'application/json', type: 'GET'
                }).done(function (data) {
                    if (data) {
                        data.forEach(function (u) { $("#shiftPersonnel").append(new Option(u.Name, u.UserId, true, true)); });
                        $("#shiftPersonnel").trigger('change');
                    }
                });
            });
        })(editshiftdetails = shifts.editshiftdetails || (shifts.editshiftdetails = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
