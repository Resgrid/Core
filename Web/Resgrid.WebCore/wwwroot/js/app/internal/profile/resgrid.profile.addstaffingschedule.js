
var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var schedule;
        (function (schedule) {
            $(document).ready(function () {
                $('#SpecifcDateTime').datetimepicker({ step: 15 });
                $('#DayOfWeekTime').kendoTimePicker({
                    interval: 15
                });
                var selectedValue = $('#SpecificDatetime').val();
                if (selectedValue === 'True') {
                    $("#specificdatetimearea").show();
                    $("#daysoftheweekarea").hide();
                    $("#daysoftheweekarea_time").hide();
                }
                else if (selectedValue === 'False') {
                    $("#specificdatetimearea").hide();
                    $("#daysoftheweekarea").show();
                    $("#daysoftheweekarea_time").show();
                }
                else {
                    $("#specificdatetimearea").hide();
                    $("#daysoftheweekarea").hide();
                    $("#daysoftheweekarea_time").hide();
                }
                $("#SpecificDatetime").change(function (e) {
                    switch ($(this).val()) {
                        case "True":
                            $("#specificdatetimearea").show();
                            $("#daysoftheweekarea").hide();
                            $("#daysoftheweekarea_time").hide();
                            break;
                        case "False":
                            $("#specificdatetimearea").hide();
                            $("#daysoftheweekarea").show();
                            $("#daysoftheweekarea_time").show();
                            break;    
                    }
                });
            });
        })(schedule = profile.schedule || (profile.schedule = {}));
    })(profile = resgrid.profile || (resgrid.profile = {}));
})(resgrid || (resgrid = {}));
