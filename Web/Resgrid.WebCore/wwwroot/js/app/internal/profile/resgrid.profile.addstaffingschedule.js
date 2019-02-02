
var resgrid;
(function (resgrid) {
    var profile;
    (function (profile) {
        var schedule;
        (function (schedule) {
            $(document).ready(function () {
                $('#SpecifcDateTime').kendoDateTimePicker({
                    interval: 15
                });
                $('#DayOfWeekTime').kendoTimePicker({
                    interval: 15
                });
                if ($('#specific_datetime').is(':checked')) {
                    $("#specificdatetimearea").show();
                    $("#daysoftheweekarea").hide();
                    $("#daysoftheweekarea_time").hide();
                }
                else if ($('#days_of_the_week').is(':checked')) {
                    $("#specificdatetimearea").hide();
                    $("#daysoftheweekarea").show();
                    $("#daysoftheweekarea_time").show();
                }
                else {
                    $("#specificdatetimearea").hide();
                    $("#daysoftheweekarea").hide();
                    $("#daysoftheweekarea_time").hide();
                }
                $("#specific_datetime").change(function (e) {
                    switch ($(this).val()) {
                        case "True":
                            $("#specificdatetimearea").show();
                            $("#daysoftheweekarea").hide();
                            $("#daysoftheweekarea_time").hide();
                            break;
                    }
                });
                $("#days_of_the_week").change(function (e) {
                    switch ($(this).val()) {
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
