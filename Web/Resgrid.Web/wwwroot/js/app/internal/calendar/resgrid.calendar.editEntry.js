var resgrid;
(function (resgrid) {
    var calendar;
    (function (calendar) {
        var editEntry;
        (function (editEntry) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Calendar Edit Entry');

                $('#recurrenceEndBlock').hide();
                $('#daysOfTheWeekBlock').hide();
                $('#monthlyBlock').hide();

                $('#isRepeatChildWarning').hide();
                $('#isRepeatParentWarning').hide();
                $('#repeatSelection').show();

                $('#StartTime').datetimepicker({ step: 15 });
                $('#EndTime').datetimepicker({ step: 15 });
                $('#RecurrenceEndLocal').datetimepicker({ step: 15 });

                // All-day toggle: switch Start/End pickers between datetime and date-only modes.
                function applyAllDayMode(allDay) {
                    if (allDay) {
                        $('#StartTime').datetimepicker({ timepicker: false, format: 'Y/m/d', step: 15 });
                        $('#EndTime').datetimepicker({ timepicker: false, format: 'Y/m/d', step: 15 });
                    } else {
                        $('#StartTime').datetimepicker({ timepicker: true, format: 'Y/m/d H:i', step: 15 });
                        $('#EndTime').datetimepicker({ timepicker: true, format: 'Y/m/d H:i', step: 15 });
                    }
                }

                // Apply on page load (for edit mode where IsAllDay may already be true).
                applyAllDayMode($('#Item_IsAllDay').is(':checked'));

                $('#Item_IsAllDay').on('change', function () {
                    applyAllDayMode($(this).is(':checked'));
                });

                $("#Item_RepeatOnDay").kendoNumericTextBox({
                    format: "#",
                    min: 1,
                    max: 31,
                    step: 1
                });

                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newCalEntryForm', function () {
                    $('#Item_Description').val(quill.root.innerHTML);

                    return true;
                });

                $("#entities").kendoMultiSelect({
                    placeholder: "Select department or groups...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Calendar/GetDepartmentEnitites'
                        }
                    }
                });

                $('#Item_RecurrenceType').on("change", function (e) { switchInputs(e); });

                switchInputs();
                setInitalMessage();
            });

            function setInitalMessage() {
                if (recurranceParentId !== '') {
                    $('#isRepeatChildWarning').show();
                    $('#isRepeatParentWarning').hide();
                    $('#repeatSelection').hide();
                    $('#recurrenceEndBlock').hide();
                    $('#daysOfTheWeekBlock').hide();
                    $('#monthlyBlock').hide();
                } else if (recurranceParentId === '' && isRepeatParent === 'True') {
                    $('#isRepeatChildWarning').hide();
                    $('#isRepeatParentWarning').show();
                    $('#repeatSelection').hide();
                    $('#recurrenceEndBlock').hide();
                    $('#daysOfTheWeekBlock').hide();
                    $('#monthlyBlock').hide();
                } else {
                    $('#isRepeatChildWarning').hide();
                    $('#isRepeatParentWarning').hide();
                    $('#repeatSelection').show();
                }
            }

            function switchInputs(v) {
                var value = $('#Item_RecurrenceType').val();
                if (value) {
                    if (value == "0") {
                        $('#recurrenceEndBlock').hide();
                        $('#daysOfTheWeekBlock').hide();
                        $('#monthlyBlock').hide();
                    }
                    else if (value == "1") {
                        $('#recurrenceEndBlock').show();
                        $('#daysOfTheWeekBlock').show();
                        $('#monthlyBlock').hide();
                    }
                    else if (value == "2") {
                        $('#recurrenceEndBlock').show();
                        $('#daysOfTheWeekBlock').hide();
                        $('#monthlyBlock').show();
                    }
                    else if (value == "3") {
                        $('#recurrenceEndBlock').show();
                        $('#daysOfTheWeekBlock').hide();
                        $('#monthlyBlock').hide();
                    }
                }
            }
            editEntry.switchInputs = switchInputs;
        })(editEntry = calendar.editEntry || (calendar.editEntry = {}));
    })(calendar = resgrid.calendar || (resgrid.calendar = {}));
})(resgrid || (resgrid = {}));
