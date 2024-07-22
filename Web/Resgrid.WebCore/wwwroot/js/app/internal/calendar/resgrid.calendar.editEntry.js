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
                $('#Item_RecurrenceEnd').datetimepicker({ step: 15 });

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
