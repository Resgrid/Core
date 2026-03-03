var resgrid;
(function (resgrid) {
    var calendar;
    (function (calendar) {
        var newEntry;
        (function (newEntry) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Calendar New Entry');

                $('#recurrenceEndBlock').hide();
                $('#daysOfTheWeekBlock').hide();
                $('#monthlyBlock').hide();

                $('#Item_Start').datetimepicker({ step: 15 });
                $('#Item_End').datetimepicker({ step: 15 });
                $('#Item_RecurrenceEnd').datetimepicker({ step: 15 });

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
                        savedStartValue = $('#Item_Start').val();
                        savedEndValue = $('#Item_End').val();

                        $('#Item_Start').datetimepicker({ timepicker: false, format: 'Y/m/d', step: 15 });
                        $('#Item_End').datetimepicker({ timepicker: false, format: 'Y/m/d', step: 15 });

                        // Update displayed values to date-only.
                        $('#Item_Start').val(stripTimePart(savedStartValue));
                        $('#Item_End').val(stripTimePart(savedEndValue));
                    } else {
                        $('#Item_Start').datetimepicker({ timepicker: true, format: 'Y/m/d H:i', step: 15 });
                        $('#Item_End').datetimepicker({ timepicker: true, format: 'Y/m/d H:i', step: 15 });

                        // Restore the previously saved datetime values, ensuring a time component
                        // is present so the user can see they need to specify a time.
                        var startVal = ensureTimePart(savedStartValue || $('#Item_Start').val());
                        var endVal = ensureTimePart(savedEndValue || $('#Item_End').val());
                        $('#Item_Start').val(startVal);
                        $('#Item_End').val(endVal);
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

                var quill = new Quill('#editor-container', {
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
            });

            function switchInputs(v) {
                var value = $('#Item_RecurrenceType').val();
                if (value) {
                    if (value == "0") { // None
                        $('#recurrenceEndBlock').hide();
                        $('#daysOfTheWeekBlock').hide();
                        $('#monthlyBlock').hide();
                    }
                    else if (value == "1") { // Weekly
                        $('#recurrenceEndBlock').show();
                        $('#daysOfTheWeekBlock').show();
                        $('#monthlyBlock').hide();
                    }
                    else if (value == "2") { // Monthly
                        $('#recurrenceEndBlock').show();
                        $('#daysOfTheWeekBlock').hide();
                        $('#monthlyBlock').show();
                    }
                    else if (value == "3") { // Yearly
                        $('#recurrenceEndBlock').show();
                        $('#daysOfTheWeekBlock').hide();
                        $('#monthlyBlock').hide();
                    }
                }
            }
            newEntry.switchInputs = switchInputs;
        })(newEntry = calendar.newEntry || (calendar.newEntry = {}));
    })(calendar = resgrid.calendar || (resgrid.calendar = {}));
})(resgrid || (resgrid = {}));
