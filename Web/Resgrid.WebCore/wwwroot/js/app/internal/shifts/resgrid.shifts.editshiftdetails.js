
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

                $("#Shift_StartTime").kendoTimePicker({
                    interval: 15
                });
                $("#Shift_StartTime").keypress(function (e) {
                    e.preventDefault();
                });
                $("#Shift_EndTime").kendoTimePicker({
                    interval: 15
                });
                $("#Shift_EndTime").keypress(function (e) {
                    e.preventDefault();
                });
                $("#shiftPersonnel").kendoMultiSelect({
                    placeholder: "Select Non-Group Personnel...",
                    dataTextField: "Name",
                    dataValueField: "UserId",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter'
                        }
                    }
                });
                $('.groupPersonnelSelect').each(function (i, obj) {
                    var that = this;
                    $(that).kendoMultiSelect({
                        placeholder: "Select Personnel for Group...",
                        dataTextField: "Name",
                        dataValueField: "UserId",
                        autoBind: false,
                        dataSource: {
                            type: "json",
                            transport: {
                                read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter'
                            }
                        }
                    });
                    var groupId = $(that).attr("name");
                    groupId = groupId.replace('groupPersonnel_', '');
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelForShift?shiftId=' + $('#Shift_ShiftId').val() + '&groupId=' + groupId,
                        contentType: 'application/json',
                        type: 'GET'
                    }).done(function (data) {
                        if (data) {
                            var multiSelect = $(that).data("kendoMultiSelect");
                            var valuesToAdd = [];
                            for (var i = 0; i < data.length; i++) {
                                valuesToAdd.push(data[i].UserId);
                            }
                            multiSelect.value(valuesToAdd);
                        }
                    });
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelForShift?shiftId=' + $('#Shift_ShiftId').val() + '&groupId=0',
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        var multiSelect = $("#shiftPersonnel").data("kendoMultiSelect");
                        var valuesToAdd = [];
                        for (var i = 0; i < data.length; i++) {
                            valuesToAdd.push(data[i].UserId);
                        }
                        multiSelect.value(valuesToAdd);
                    }
                });
            });
        })(editshiftdetails = shifts.editshiftdetails || (shifts.editshiftdetails = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
