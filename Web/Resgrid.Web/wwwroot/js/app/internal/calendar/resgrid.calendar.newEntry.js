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
