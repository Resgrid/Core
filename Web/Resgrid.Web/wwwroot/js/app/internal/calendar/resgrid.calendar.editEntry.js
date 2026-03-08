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
                // Stores full datetime values so they can be restored when all-day is unchecked.
                var savedStartValue = '';
                var savedEndValue = '';

                function stripTimePart(value) {
                    // Accepts 'Y/m/d H:i' or 'Y/m/d' and returns only the date portion.
                    if (!value) return '';
                    return value.split(' ')[0];
                }

                function ensureTimePart(value) {
                    // If the value has no time component, append a default time of 00:00.
                    if (!value) return '';
                    return value.indexOf(' ') === -1 ? value + ' 00:00' : value;
                }

                function applyAllDayMode(allDay) {
                    if (allDay) {
                        // Save current full datetime values before stripping time.
                        savedStartValue = $('#StartTime').val();
                        savedEndValue = $('#EndTime').val();

                        $('#StartTime').datetimepicker({ timepicker: false, format: 'Y/m/d', step: 15 });
                        $('#EndTime').datetimepicker({ timepicker: false, format: 'Y/m/d', step: 15 });

                        // Update displayed values to date-only.
                        $('#StartTime').val(stripTimePart(savedStartValue));
                        $('#EndTime').val(stripTimePart(savedEndValue));
                    } else {
                        $('#StartTime').datetimepicker({ timepicker: true, format: 'Y/m/d H:i', step: 15 });
                        $('#EndTime').datetimepicker({ timepicker: true, format: 'Y/m/d H:i', step: 15 });

                        // Restore the previously saved datetime values, ensuring a time component
                        // is present so the user can see they need to specify a time.
                        var startVal = ensureTimePart(savedStartValue || $('#StartTime').val());
                        var endVal = ensureTimePart(savedEndValue || $('#EndTime').val());
                        $('#StartTime').val(startVal);
                        $('#EndTime').val(endVal);
                    }
                }

                // Apply on page load (for edit mode where IsAllDay may already be true).
                applyAllDayMode($('#Item_IsAllDay').is(':checked'));

                $('#Item_IsAllDay').on('change', function () {
                    applyAllDayMode($(this).is(':checked'));
                });

                $("#Item_RepeatOnDay").attr({ type: 'number', min: 1, max: 31, step: 1 });

                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newCalEntryForm', function () {
                    $('#Item_Description').val(quill.root.innerHTML);

                    return true;
                });

                $("#entities").select2({
                    placeholder: "Select department or groups...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Calendar/GetDepartmentEnitites',
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (e) { return { id: e.Id, text: e.Name }; }) };
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
